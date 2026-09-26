using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Hazel;
using LightInDark.Core;
using LightInDark.Handshake;
using LightInDark.RPCs;
using UnityEngine;

namespace Light.Handshake;

/// <summary>
/// 模组握手系统（Light 侧功能层，防绕过加固版）。
///
/// 协议（挑战-应答）：
///   房主 → 新玩家 : Challenge RPC（携带一次性随机 nonce）
///   玩家 → 服务器 : POST /verify（version + 本机 apiHash/modHash + accountId + nonce）
///   服务器 → 玩家 : 校验 hash 与官方一致 → 签名票据（含 nonce/hash/accountId/过期）
///   玩家 → 房主 : Handshake RPC（本机 hash ×2 + 票据）
///   房主验证  : ①票据签名（内置公钥）②nonce == 自己发的 ③票据hash==上报hash==房主本地hash
///              ④票据 accountId == 玩家 ClientData.ProductUserId（防借票）
///
/// 兜底：玩家加入白名单后 N 秒内未收到有效握手 → 按 HandshakeMode 提示或踢出。
/// 消息使用 AmongUs 原生右下角提示（Notifier.AddDisconnectMessage）。
/// 服务器地址留空 = 整个握手系统禁用。
/// </summary>
public static class HandshakeManager
{
    private const string ChallengeRpcHash = "Light.Handshake.Challenge";
    private const string HandshakeRpcHash = "Light.Handshake";
    private static readonly string CachePath =
        Path.Combine(LightPlugin.LightUserDataPath, "HandshakeCache.json");

    private static int _localApiHash;
    private static int _localModHash;
    private static bool _initialized;

    // 客户端状态
    private static string _ticket = "";
    private static long _ticketExp;
    private static int _pendingNonce;          // 房主发来的挑战 nonce

    // 房主状态
    private static readonly Dictionary<byte, int> _issuedNonces = new();      // playerId → nonce
    private static readonly Dictionary<byte, float> _joinTimes = new();       // playerId → 加入时间
    private static readonly HashSet<byte> _verified = new();                  // 已通过验证的玩家
    private static readonly HashSet<byte> _unverified = new();                // 未通过验证（红名 + 阻止开始）
    private static readonly Dictionary<byte, string> _names = new();          // playerId → 名字（提示用）
    private static readonly Dictionary<byte, UnityEngine.Color> _origNameColors = new(); // 未验证玩家的原名字色
    private static bool _timeoutLoopRunning;

    /// <summary>是否存在未通过验证的玩家（房主用于判断能否开始游戏）。</summary>
    public static bool HasUnverified() => _unverified.Count > 0;

    /// <summary>指定玩家是否未通过验证（用于红名状态）。</summary>
    public static bool IsUnverified(byte playerId) => _unverified.Contains(playerId);

    /// <summary>把指定玩家名字设为红色（未验证时每帧刷新用）。</summary>
    public static void MarkNameRed(byte playerId) => SetNameRed(playerId);

    public static void Initialize()
    {
        try
        {
            if (_initialized) return;
            _initialized = true;

            (_localApiHash, _localModHash) = HandshakeCrypto.ComputeLocalHashes();
            LightLogger.Log($"[Handshake] 本地 hash: api={_localApiHash} mod={_localModHash}");

            LoadCache();
            CustomRPC.Register(ChallengeRpcHash, OnChallengeReceived);
            CustomRPC.Register(HandshakeRpcHash, OnHandshakeReceived);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HandshakeManager.Initialize]", ex);
        }
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // =====================================================================
    // 房主侧：玩家加入 → 发挑战 + 启动超时兜底
    // =====================================================================

    /// <summary>玩家加入房间（房主视角）。生成一次性 nonce 发给对方，并登记超时。</summary>
    public static void OnPlayerJoined(byte playerId, string playerName)
    {
        try
        {
            if (AmongUsClient.Instance?.AmHost != true) return;
            var local = PlayerControl.LocalPlayer;
            if (local == null) return;
            if (playerId == local.PlayerId) return; // 自己（房主）无需验证
            if (string.IsNullOrEmpty(LightPlugin.LightSettingsData.VerifyServerUrl)) return; // 握手禁用

            _names[playerId] = string.IsNullOrEmpty(playerName) ? $"P{playerId}" : playerName;
            _joinTimes[playerId] = Time.time;
            _verified.Remove(playerId);
            _unverified.Add(playerId);

            int nonce = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            _issuedNonces[playerId] = nonce;

            // 广播挑战（不使用定向 RPC：新玩家刚加入时其 OwnerId 可能未就绪，定向发送会丢包）。
            // 客户端根据 playerId 判断是否自己的挑战。
            CustomRPC.SendOnly(ChallengeRpcHash, w =>
            {
                w.Write(playerId);
                w.Write(nonce);
            }, reliable: true);
            LightLogger.Log($"[Handshake] 已向 {_names[playerId]} 广播挑战 nonce={nonce}");

            if (!_timeoutLoopRunning)
            {
                _timeoutLoopRunning = true;
                Dispatcher.Instance?.StartCoroutine(CoTimeoutLoop().WrapToIl2Cpp());
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HandshakeManager.OnPlayerJoined]", ex);
        }
    }

    /// <summary>超时兜底：加入后 1 秒未握手 → 提示/踢出。同时清理已离开的玩家。</summary>
    private static IEnumerator CoTimeoutLoop()
    {
        while (_timeoutLoopRunning)
        {
            yield return new WaitForSeconds(1f);
            try
            {
                if (AmongUsClient.Instance?.AmHost != true) continue;

                float now = Time.time;
                foreach (var kv in new List<KeyValuePair<byte, float>>(_joinTimes))
                {
                    byte pid = kv.Key;
                    if (_verified.Contains(pid)) { _joinTimes.Remove(pid); continue; }
                    if (now - kv.Value < 1f) continue; // 1 秒超时

                    _joinTimes.Remove(pid);
                    string name = _names.TryGetValue(pid, out var n) ? n : $"P{pid}";
                    HandleMismatch(pid, name, MismatchKind.NoHandshake);
                }

                // 清理已离开玩家（红名记录/未验证标记），并恢复其名字颜色
                foreach (var pid in new List<byte>(_unverified))
                {
                    if (GetPlayerControl(pid) == null)
                    {
                        RestoreNameColor(pid);
                        CleanupPlayer(pid);
                        LightLogger.Log($"[Handshake] 玩家离开，清理 pid={pid}");
                    }
                }
                if (_joinTimes.Count == 0) _timeoutLoopRunning = false;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[HandshakeManager.CoTimeoutLoop]", ex);
            }
        }
    }

    // =====================================================================
    // 客户端侧：收到挑战 → 拉票 → 发握手
    // =====================================================================

    private static void OnChallengeReceived(MessageReader reader)
    {
        try
        {
            if (AmongUsClient.Instance?.AmHost == true) return; // 房主忽略自己的广播
            var local = PlayerControl.LocalPlayer;
            if (local == null)
            {
                LightLogger.LogWarning("[Handshake] 收到挑战时本地玩家未就绪，稍后由超时兜底");
                return;
            }

            byte targetId = reader.ReadByte();
            if (targetId != local.PlayerId) return; // 广播给所有人，只有目标玩家响应
            int nonce = reader.ReadInt32();
            _pendingNonce = nonce;
            LightLogger.Log($"[Handshake] 收到房主挑战 nonce={nonce}");

            string url = LightPlugin.LightSettingsData.VerifyServerUrl;
            if (string.IsNullOrEmpty(url))
            {
                LightLogger.Log("[Handshake] 未配置验证服务器，握手跳过（禁用状态）");
                return;
            }

            // 缓存票据仅用于服务器暂时不可达时的宽限；nonce 一次性，必须重新拉票。
            _ = Task.Run(() => FetchTicketAsync(url, nonce));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HandshakeManager.OnChallengeReceived]", ex);
        }
    }

    private static async Task FetchTicketAsync(string url, int nonce)
    {
        try
        {
            if (string.IsNullOrEmpty(url)) return;

            string accountId = GetLocalAccountId();
            var payload = new
            {
                version = HandshakeCrypto.ProtocolVersion,
                apiHash = _localApiHash,
                modHash = _localModHash,
                accountId,
                nonce,
            };
            string json = JsonSerializer.Serialize(payload);

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(8);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await client.PostAsync(url.TrimEnd('/') + "/verify", content);
            string body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                LightLogger.LogWarning($"[Handshake] 服务器拒绝票据: {(int)resp.StatusCode} {body}");
                return;
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.GetProperty("ok").GetBoolean())
            {
                LightLogger.LogWarning("[Handshake] 服务器返回 ok=false");
                return;
            }

            _ticket = doc.RootElement.GetProperty("ticket").GetString() ?? "";
            _ticketExp = doc.RootElement.GetProperty("exp").GetInt64();
            SaveCache();
            LightLogger.Log($"[Handshake] 票据获取成功，exp={_ticketExp}");

            // 回到主线程发握手
            Dispatcher.Instance?.Enqueue(SendHandshake);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[Handshake] 获取票据失败（宽限处理，不影响游玩）: {ex.Message}");
        }
    }

    private static void SendHandshake()
    {
        try
        {
            if (string.IsNullOrEmpty(_ticket)) return;
            if (AmongUsClient.Instance?.AmHost == true) return;
            var player = PlayerControl.LocalPlayer;
            if (player == null) return;

            CustomRPC.SendOnly(HandshakeRpcHash, w =>
            {
                w.Write(player.PlayerId);
                w.Write(_localApiHash);
                w.Write(_localModHash);
                w.Write(_ticket);
            }, reliable: true);

            LightLogger.Log($"[Handshake] 已发送握手 (player={player.PlayerId})");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HandshakeManager.SendHandshake]", ex);
        }
    }

    // =====================================================================
    // 房主侧：验证握手
    // =====================================================================

    private enum MismatchKind
    {
        Tampered,     // 票据无效/篡改
        Version,      // 票据有效但版本不一致
        NoHandshake,  // 超时未握手
    }

    private static void OnHandshakeReceived(MessageReader reader)
    {
        try
        {
            if (AmongUsClient.Instance?.AmHost != true) return;

            byte playerId = reader.ReadByte();
            int apiHash = reader.ReadInt32();
            int modHash = reader.ReadInt32();
            string ticket = reader.ReadString();

            string name = GetPlayerName(playerId);
            _names[playerId] = name;

            // ① 票据签名 + 过期 + 版本
            bool sigOk = HandshakeCrypto.TryParse(ticket,
                out string ticketAccount, out _, out int tkApi, out int tkMod, out _, out int tkNonce);

            if (!sigOk)
            {
                HandleMismatch(playerId, name, MismatchKind.Tampered);
                return;
            }

            // ② nonce 必须是自己发给该玩家的（防票据重放/跨局借用）
            if (!_issuedNonces.TryGetValue(playerId, out int issuedNonce) || tkNonce != issuedNonce)
            {
                HandleMismatch(playerId, name, MismatchKind.Tampered);
                return;
            }
            _issuedNonces.Remove(playerId);

            // ③ 票据内 hash == 上报 hash == 房主本地 hash（版本一致性）
            if (tkApi != apiHash || tkMod != modHash ||
                apiHash != _localApiHash || modHash != _localModHash)
            {
                HandleMismatch(playerId, name, MismatchKind.Version);
                return;
            }

            // ④ 票据 accountId == 该玩家 ClientData.ProductUserId（防借票）
            string realAccount = GetClientAccountId(playerId);
            if (!string.IsNullOrEmpty(realAccount) && !string.IsNullOrEmpty(ticketAccount)
                && realAccount != ticketAccount)
            {
                HandleMismatch(playerId, name, MismatchKind.Tampered);
                return;
            }

            _verified.Add(playerId);
            _joinTimes.Remove(playerId);
            _unverified.Remove(playerId); // 验证通过，解除红名/阻止开始
            RestoreNameColor(playerId);
            LightLogger.Log($"[Handshake] {name} 验证通过 (playerId={playerId})");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HandshakeManager.OnHandshakeReceived]", ex);
        }
    }

    private static void HandleMismatch(byte playerId, string name, MismatchKind kind)
    {
        try
        {
            _joinTimes.Remove(playerId);
            _verified.Remove(playerId);
            _unverified.Add(playerId); // 不匹配：保持红名 + 阻止开始，直到离开
            SetNameRed(playerId);

            string msg;
            string log;
            switch (kind)
            {
                case MismatchKind.Tampered:
                    msg = $"{name} 疑似使用了被篡改的模组！";
                    log = $"[Handshake] {name} 票据无效/身份不匹配 (playerId={playerId})";
                    break;
                case MismatchKind.Version:
                    msg = $"{name} 模组版本不匹配！";
                    log = $"[Handshake] {name} 版本不一致 (playerId={playerId})";
                    break;
                default:
                    msg = $"{name} 模组版本不匹配！";
                    log = $"[Handshake] {name} 超时未完成握手 (playerId={playerId})";
                    break;
            }
            LightLogger.LogWarning(log);

            int mode = LightPlugin.LightSettingsData.HandshakeMode;
            if (mode == (int)HandshakeModeOption.Kick)
            {
                Notify($"{msg} 将在3秒后执行踢出...");
                Dispatcher.Instance?.StartCoroutine(CoKickAfter(playerId, 3f).WrapToIl2Cpp());
            }
            else
            {
                Notify(msg);
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HandshakeManager.HandleMismatch]", ex);
        }
    }

    private static IEnumerator CoKickAfter(byte playerId, float delay)
    {
        yield return new WaitForSeconds(delay);
        try
        {
            RpcDefinitions.KickPlayerWithReason(playerId, "模组版本不匹配或疑似被篡改");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HandshakeManager.CoKickAfter]", ex);
        }
    }

    // =====================================================================
    // 原生右下角提示（与玩家退出提示同款）
    // =====================================================================

    private static void Notify(string message)
    {
        try
        {
            if (HudManager.Instance?.Notifier != null)
            {
                HudManager.Instance.Notifier.AddDisconnectMessage(message);
            }
            else
            {
                LightLogger.LogWarning($"[Handshake][Notify] HudManager 不可用: {message}");
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HandshakeManager.Notify]", ex);
        }
    }

    // =====================================================================
    // 辅助
    // =====================================================================

    private static string GetLocalAccountId()
    {
        try
        {
            if (AmongUsClient.Instance != null)
            {
                var cd = AmongUsClient.Instance.GetClientFromCharacter(PlayerControl.LocalPlayer);
                if (cd != null && !string.IsNullOrEmpty(cd.ProductUserId))
                    return cd.ProductUserId;
            }
        }
        catch { }
        return "";
    }

    private static string GetClientAccountId(byte playerId)
    {
        try
        {
            var target = GetPlayerControl(playerId);
            if (target == null) return "";
            var cd = AmongUsClient.Instance?.GetClientFromCharacter(target);
            return cd?.ProductUserId ?? "";
        }
        catch { return ""; }
    }

    private static PlayerControl? GetPlayerControl(byte playerId)
    {
        try
        {
            foreach (var pc in PlayerControl.AllPlayerControls)
                if (pc.PlayerId == playerId)
                    return pc;
        }
        catch { }
        return null;
    }

    private static string GetPlayerName(byte id)
    {
        try
        {
            foreach (var pc in PlayerControl.AllPlayerControls)
                if (pc.PlayerId == id)
                    return pc.Data?.PlayerName ?? $"P{id}";
        }
        catch { }
        return _names.TryGetValue(id, out var n) ? n : $"P{id}";
    }

    // ======================= 名字变红（头顶显示名） =======================

    /// <summary>把不匹配玩家的头顶名字设为红色；记录原色以便恢复。</summary>
    private static void SetNameRed(byte playerId)
    {
        try
        {
            var pc = GetPlayerControl(playerId);
            if (pc == null || pc.cosmetics == null) return;
            if (!_origNameColors.ContainsKey(playerId))
                _origNameColors[playerId] = pc.cosmetics.nameText.color;
            pc.cosmetics.SetNameColor(UnityEngine.Color.red);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HandshakeManager.SetNameRed]", ex);
        }
    }

    /// <summary>恢复玩家名字原色（验证通过/玩家离开时）。</summary>
    public static void RestoreNameColor(byte playerId)
    {
        try
        {
            if (!_origNameColors.TryGetValue(playerId, out var orig)) return;
            var pc = GetPlayerControl(playerId);
            if (pc?.cosmetics != null)
                pc.cosmetics.SetNameColor(orig);
            _origNameColors.Remove(playerId);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HandshakeManager.RestoreNameColor]", ex);
        }
    }

    /// <summary>玩家离开后清理其红名记录（由加入补丁每帧或离开时调用）。</summary>
    public static void CleanupPlayer(byte playerId)
    {
        _unverified.Remove(playerId);
        _verified.Remove(playerId);
        _joinTimes.Remove(playerId);
        _issuedNonces.Remove(playerId);
        _names.Remove(playerId);
        _origNameColors.Remove(playerId);
    }

    // ======================= 票据缓存 =======================

    private static void SaveCache()
    {
        try
        {
            FileUtil.EnsureDirectoryExists(CachePath);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(new { ticket = _ticket, exp = _ticketExp }));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HandshakeManager.SaveCache]", ex);
        }
    }

    private static void LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return;
            string json = File.ReadAllText(CachePath);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("ticket", out var t)) return;
            long exp = doc.RootElement.TryGetProperty("exp", out var e) ? e.GetInt64() : 0;
            if (exp < Now()) return; // 已过期
            _ticket = t.GetString() ?? "";
            _ticketExp = exp;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HandshakeManager.LoadCache]", ex);
        }
    }
}