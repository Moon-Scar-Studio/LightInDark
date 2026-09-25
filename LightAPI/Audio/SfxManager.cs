using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using LightInDark.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace LightInDark.Audio
{
    /// <summary>
    /// 音效管理器。
    /// 所有传入路径均为相对路径（如 "./Resources/SFX/Sth.mp3"），映射到打包进 dll 的嵌入资源
    /// （Light 项目 RootNamespace=Light，资源目录 Resources\SFX\Sth.mp3 的嵌入资源名为
    /// "Light.Resources.SFX.Sth.mp3"）。
    ///
    /// 解码方案：Unity 引擎原生解码（无第三方库）。
    /// 嵌入资源字节先落盘到 %TEMP%\LightInDark\SFX 临时文件，再用
    /// UnityWebRequestMultimedia.GetAudioClip(file://, AudioType.MPEG) 异步加载为 AudioClip，
    /// 解码结果按路径缓存，之后重复播放直接复用。
    /// </summary>
    public static class SfxManager
    {
        // 程序集 → 嵌入资源名列表（懒扫描，可手动注册扩展程序集）
        private static readonly Dictionary<Assembly, string[]> _assemblyResources = new();
        private static bool _assembliesScanned;

        // 相对路径 → 嵌入资源字节 / AudioClip 缓存 / 临时文件路径
        private static readonly Dictionary<string, byte[]> _resourceBytes = new();
        private static readonly Dictionary<string, AudioClip> _clipCache = new();
        private static readonly Dictionary<string, string> _tempFiles = new();

        // 正在异步加载的路径（避免并发重复加载）
        private static readonly HashSet<string> _loading = new();

        // 已确认缺失的路径（避免反复扫描与重复告警）
        private static readonly HashSet<string> _failed = new();

        // 加载期间收到播放请求的路径（即使该加载是预热发起，完成后也要播放）
        private static readonly HashSet<string> _pendingPlay = new();

        private static SfxHost _host;

        /// <summary>
        /// 播放相对路径音效。首次调用异步解码，解码完成后自动播放；之后直接复用缓存。
        /// </summary>
        /// <param name="relativePath">相对路径（如 "./Resources/SFX/Sth.mp3"），null/空/空白时静默忽略。</param>
        /// <param name="volume">音量（1 = 原音量）。</param>
        /// <param name="pitch">音调（1 = 原速）。</param>
        public static void Play(string relativePath, float volume = 1f, float pitch = 1f)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return;
            if (_failed.Contains(relativePath)) return; // 已确认缺失，静默忽略

            try
            {
                if (_clipCache.TryGetValue(relativePath, out var cached) && cached != null)
                {
                    PlayClip(cached, volume, pitch);
                    return;
                }

                if (_loading.Contains(relativePath))
                {
                    _pendingPlay.Add(relativePath); // 加载完成后补播
                    return;
                }

                EnsureHost();
                _loading.Add(relativePath);
                _host.StartCoroutine(CoLoadAndPlay(relativePath, volume, pitch, true).WrapToIl2Cpp());
            }
            catch (Exception ex)
            {
                _loading.Remove(relativePath);
                LightLogger.LogWarning($"[SfxManager] 播放调度失败 {relativePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// 同步检查相对路径音效是否存在（嵌入资源可读）。用于在替换原版音效前确认资源可用。
        /// </summary>
        public static bool ResourceExists(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return false;
            try { return GetBytes(relativePath) != null; }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// 预热音效（只解码不播放）。用于按钮创建时预热点击/冷却音效，避免首次播放的异步延迟。
        /// 已在缓存/加载中/资源缺失时均安全忽略。
        /// </summary>
        public static void Warmup(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return;
            if (_clipCache.ContainsKey(relativePath)) return;
            if (_loading.Contains(relativePath)) return;
            if (_failed.Contains(relativePath)) return; // 已确认缺失，静默忽略

            try
            {
                EnsureHost();
                _loading.Add(relativePath);
                _host.StartCoroutine(CoLoadAndPlay(relativePath, 1f, 1f, false).WrapToIl2Cpp());
            }
            catch (Exception ex)
            {
                _loading.Remove(relativePath);
                LightLogger.LogWarning($"[SfxManager] 预热失败 {relativePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// 手动注册包含嵌入音效资源的程序集（默认自动扫描已加载程序集，一般无需调用）。
        /// </summary>
        public static void RegisterAssembly(Assembly assembly)
        {
            if (assembly == null) return;
            try
            {
                _assemblyResources[assembly] = assembly.GetManifestResourceNames();
                _failed.Clear(); // 新程序集可能带来此前缺失的资源，允许重新尝试
            }
            catch (Exception) { _assemblyResources[assembly] = Array.Empty<string>(); }
        }

        private static void PlayClip(AudioClip clip, float volume, float pitch)
        {
            if (clip == null) return;
            if (SoundManager.Instance == null) return;
            SoundManager.Instance.PlaySoundImmediate(clip, false, volume, pitch);
        }

        private static IEnumerator CoLoadAndPlay(string relativePath, float volume, float pitch, bool playWhenReady)
        {
            byte[] bytes = null;
            try { bytes = GetBytes(relativePath); }
            catch (Exception ex) { LightLogger.LogWarning($"[SfxManager] 读取资源失败 {relativePath}: {ex.Message}"); }

            if (bytes == null || bytes.Length == 0)
            {
                LightLogger.LogWarning($"[SfxManager] 未找到音效资源: {relativePath}");
                _loading.Remove(relativePath);
                yield break;
            }

            string filePath = GetTempFile(relativePath, bytes);
            if (filePath == null)
            {
                _loading.Remove(relativePath);
                yield break;
            }

            // 注意：IL2CPP 环境下 UnityWebRequest 不实现 IDisposable，不能使用 using
            var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(filePath).AbsoluteUri, AudioType.MPEG);
            yield return request.SendWebRequest();

            try
            {
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var handler = request.downloadHandler as DownloadHandlerAudioClip;
                    // IL2CPP 生成器把 Unity 属性 clip 重命名为 audioClip
                    var clip = handler != null ? handler.audioClip : null;
                    if (clip != null)
                    {
                        clip.name = Path.GetFileNameWithoutExtension(relativePath);
                        _clipCache[relativePath] = clip;

                        // 播放条件：本次调用要求播放，或加载期间收到过播放请求（预热后立刻被 Play）
                        bool shouldPlay = playWhenReady || _pendingPlay.Remove(relativePath);
                        if (shouldPlay) PlayClip(clip, volume, pitch);
                    }
                    else
                    {
                        LightLogger.LogWarning($"[SfxManager] 解码结果为空: {relativePath}");
                    }
                }
                else
                {
                    LightLogger.LogWarning($"[SfxManager] 加载失败 {relativePath}: {request.error}");
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[SfxManager] 解码/播放异常 {relativePath}: {ex.Message}");
            }
            finally
            {
                _loading.Remove(relativePath);
                _pendingPlay.Remove(relativePath);
            }
        }

        private static string GetTempFile(string relativePath, byte[] bytes)
        {
            try
            {
                if (_tempFiles.TryGetValue(relativePath, out var cached) && File.Exists(cached)) return cached;

                var dir = Path.Combine(Path.GetTempPath(), "LightInDark", "SFX");
                Directory.CreateDirectory(dir);

                var hash = GetShortHash(relativePath);
                var ext = Path.GetExtension(relativePath);
                if (string.IsNullOrEmpty(ext)) ext = ".mp3";

                var path = Path.Combine(dir, hash + ext);
                if (!File.Exists(path)) File.WriteAllBytes(path, bytes);

                _tempFiles[relativePath] = path;
                return path;
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[SfxManager] 写入临时文件失败 {relativePath}: {ex.Message}");
                return null;
            }
        }

        private static string GetShortHash(string s)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in s) h = h * 31 + c;
                return (h & 0x7FFFFFFF).ToString("X8");
            }
        }

        private static byte[] GetBytes(string relativePath)
        {
            if (_resourceBytes.TryGetValue(relativePath, out var cached)) return cached;
            if (_failed.Contains(relativePath)) return null; // 已确认缺失

            var normalized = Normalize(relativePath); // "./Resources/SFX/Sth.mp3" → "Resources.SFX.Sth.mp3"
            EnsureAssemblies();

            foreach (var kv in _assemblyResources)
            {
                foreach (var resName in kv.Value)
                {
                    if (!Matches(resName, normalized)) continue;

                    using var stream = kv.Key.GetManifestResourceStream(resName);
                    if (stream == null) continue;

                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    var bytes = ms.ToArray();
                    _resourceBytes[relativePath] = bytes;
                    return bytes;
                }
            }

            _failed.Add(relativePath); // 全部程序集均未命中，标记缺失
            return null;
        }

        private static string Normalize(string relativePath)
        {
            var p = relativePath.Replace('\\', '/').TrimStart('.', '/');
            return p.Replace('/', '.');
        }

        private static bool Matches(string resourceName, string normalizedDotted)
        {
            // "Light.Resources.SFX.Sth.mp3" 匹配 "Resources.SFX.Sth.mp3"
            var tail = ".Resources." + normalizedDotted;
            return resourceName.EndsWith(tail, StringComparison.OrdinalIgnoreCase)
                || resourceName.EndsWith(normalizedDotted, StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureAssemblies()
        {
            if (_assembliesScanned) return;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (_assemblyResources.ContainsKey(asm)) continue;
                    try { _assemblyResources[asm] = asm.GetManifestResourceNames(); }
                    catch (Exception) { _assemblyResources[asm] = Array.Empty<string>(); }
                }
            }
            catch (Exception) { }
            _assembliesScanned = true;
        }

        private static void EnsureHost()
        {
            if (_host != null) return;
            var go = new GameObject("LightInDarkSfxHost");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _host = go.AddComponent<SfxHost>();
        }

        private sealed class SfxHost : MonoBehaviour { }
    }
}
