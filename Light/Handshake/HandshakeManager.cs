using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Hazel;
using LightInDark.Core;
using LightInDark.Handshake;
using LightInDark.RPCs;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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
/// 是否启用见 <see cref="HandshakeManager.IsEnabled"/>（设置开关 + 服务器地址）。
/// </summary>
public static class HandshakeManager
{
    private const string ChallengeRpcHash = "Light.Handshake.Challenge";
    private const string HandshakeRpcHash = "Light.Handshake";

    private static int _localApiHash;
    private static int _localModHash;
    private static bool _initialized;

    // 房主状态
    private static readonly Dictionary<byte, int> _issuedNonces = new();      // playerId → nonce
    private static readonly Dictionary<byte, float> _joinTimes = new();       // playerId → 加入时间
    private static readonly HashSet<byte> _pending = new();                   // 等待验证中（不红名、不算未通过）
    private static readonly HashSet<byte> _verified = new();                  // 已通过验证的玩家
    private static readonly HashSet<byte> _unverified = new();                // 未通过验证（红名 + 阻止开始）
    private static readonly Dictionary<byte, string> _names = new();          // playerId → 名字（提示用）
    private static readonly Dictionary<byte, UnityEngine.Color> _origNameColors = new(); // 未验证玩家的原名字色
    private static bool _timeoutLoopRunning;

    /// <summary>握手是否启用：设置开关打开且验证服务器地址非空。</summary>
    public static bool IsEnabled =>
        (LightPlugin.LightSettingsData?.EnableHandshake ?? false)
        && !string.IsNullOrEmpty(LightPlugin.LightSettingsData?.VerifyServerUrl);

    /// <summary>是否存在未通过验证的玩家（房主用于判断能否开始游戏）。</summary>
    public static bool HasUnverified() => _unverified.Count > 0;

    /// <summary>是否存在等待验证中的玩家（不计入未通过）。</summary>
    public static bool HasPending() => _pending.Count > 0;

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
            LightLogger.Log($"[Handshake] 本地 hash: api={_localApiHash} mod={_localModHash} 版本={LightPlugin.Version}");

            CustomRPC.Register(ChallengeRpcHash, OnChallengeReceived);
            CustomRPC.Register(HandshakeRpcHash, OnHandshakeReceived);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HandshakeManager.Initialize]", ex);
        }
    }

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
            if (!IsEnabled) return; // 握手未启用

            _names[playerId] = string.IsNullOrEmpty(playerName) ? $"P{playerId}" : playerName;
            _joinTimes[playerId] = Time.time;
            _verified.Remove(playerId);
            _unverified.Remove(playerId);
            _pending.Add(playerId); // 先记为等待验证，不红名、不算失败

            int nonce = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            _issuedNonces[playerId] = nonce;

            // 广播挑战（不使用定向 RPC：新玩家刚加入时其 OwnerId 可能未就绪，定向发送会丢包）。
            // 客户端根据 playerId 判断是否自己的挑战。
            CustomRPC.SendOnly(ChallengeRpcHash, w =>
            {
                w.Write(playerId);
                w.Write(nonce);
            }, reliable: true);
            LightLogger.LogDebug($"[Handshake] 已向 {_names[playerId]} 广播挑战 nonce={nonce}");

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

    /// <summary>超时兜底：加入后超过配置时限仍未握手 → 提示/踢出。同时清理已离开的玩家。</summary>
    private static IEnumerator CoTimeoutLoop()
    {
        while (_timeoutLoopRunning) // 常驻：保证玩家离开后仍能清理残留状态
        {
            yield return new WaitForSeconds(1f);
            try
            {
                if (AmongUsClient.Instance?.AmHost != true) continue;

                float now = Time.time;
                float timeout = Mathf.Clamp(LightPlugin.LightSettingsData?.HandshakeTimeoutSeconds ?? 10f, 1f, 60f);

                foreach (var kv in new List<KeyValuePair<byte, float>>(_joinTimes))
                {
                    byte pid = kv.Key;
                    if (_verified.Contains(pid)) { _joinTimes.Remove(pid); continue; }
                    if (now - kv.Value < timeout) continue; // 窗口内不做任何判定

                    _joinTimes.Remove(pid);
                    _pending.Remove(pid);
                    string name = _names.TryGetValue(pid, out var n) ? n : $"P{pid}";
                    HandleMismatch(pid, name, MismatchKind.NoHandshake);
                }

                // 清理已离开玩家（等待/未通过记录），并恢复其名字颜色
                foreach (var pid in new List<byte>(_pending))
                {
                    if (GetPlayerControl(pid) == null)
                    {
                        RestoreNameColor(pid);
                        CleanupPlayer(pid);
                    }
                }
                foreach (var pid in new List<byte>(_unverified))
                {
                    if (GetPlayerControl(pid) == null)
                    {
                        RestoreNameColor(pid);
                        CleanupPlayer(pid);
                        LightLogger.Log($"[Handshake] 玩家离开，清理 pid={pid}");
                    }
                }
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

            // 挑战必须来自房主：否则任意客户端都能广播假挑战，诱导本机用错误 nonce 应答后被房主误判
            byte sender = CustomRPC.CurrentSender;
            byte hostPid = GetHostPlayerId();
            if (sender != byte.MaxValue && hostPid != byte.MaxValue && sender != hostPid)
            {
                LightLogger.LogWarning($"[Handshake] 忽略非房主发来的挑战（sender={sender}, host={hostPid}）");
                return;
            }

            int nonce = reader.ReadInt32();
            LightLogger.LogDebug($"[Handshake] 收到房主挑战 nonce={nonce}");

            if (!IsEnabled) return; // 握手未启用

            // 缓存票据仅用于服务器暂时不可达时的宽限；nonce 一次性，必须重新拉票。
            string url = LightPlugin.LightSettingsData.VerifyServerUrl;
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
                version = LightPlugin.Version,
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
                AbortHandshakeNoTicket("服务器拒绝");
                return;
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.GetProperty("ok").GetBoolean())
            {
                LightLogger.LogWarning("[Handshake] 服务器返回 ok=false");
                AbortHandshakeNoTicket("服务器返回 ok=false");
                return;
            }

            string ticket = doc.RootElement.GetProperty("ticket").GetString() ?? "";
            long exp = doc.RootElement.GetProperty("exp").GetInt64();
            LightLogger.Log($"[Handshake] 票据获取成功，exp={exp}");

            // 回到主线程发握手；票据随闭包传递，避免多挑战并发时被共享状态覆盖
            Dispatcher.Instance?.Enqueue(() => SendHandshake(ticket));
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[Handshake] 获取票据失败: {ex.Message}");
            AbortHandshakeNoTicket("请求异常");
        }
    }

    /// <summary>
    /// 取不到本次票据时**直接不发握手**：交由房主按超时判定（NoHandshake）。
    /// 若用非本次 nonce 的旧票据发送，会被房主判为"疑似被篡改"，故宁可不发。
    /// </summary>
    private static void AbortHandshakeNoTicket(string why)
    {
        LightLogger.LogWarning($"[Handshake] 未取得本次票据（{why}），本次不发握手，交由房主超时判定");
    }

    private static void SendHandshake(string ticket)
    {
        try
        {
            if (string.IsNullOrEmpty(ticket)) return;
            if (AmongUsClient.Instance?.AmHost == true) return;
            var player = PlayerControl.LocalPlayer;
            if (player == null) return;

            CustomRPC.SendOnly(HandshakeRpcHash, w =>
            {
                w.Write(player.PlayerId);
                w.Write(_localApiHash);
                w.Write(_localModHash);
                w.Write(ticket);
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

            // ⓪ 身份校验：报文自称的 playerId 必须等于真实发送者，防止冒用他人身份栽赃
            byte sender = CustomRPC.CurrentSender;
            if (sender != byte.MaxValue && playerId != sender)
            {
                LightLogger.LogWarning($"[Handshake] 报文 playerId={playerId} 与真实发送者 {sender} 不一致，忽略");
                return;
            }

            string name = GetPlayerName(playerId);
            _names[playerId] = name;

            // ① 票据签名 + 过期 + 版本（版本以本机模组版本为单一来源）
            bool sigOk = HandshakeCrypto.TryParse(ticket, LightPlugin.Version,
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
            _pending.Remove(playerId);     // 等待验证结束
            _unverified.Remove(playerId);  // 验证通过，解除红名/阻止开始
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
                    msg = $"{name} 未完成模组验证！";
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
            // 延迟期间该玩家可能已离开 / 已通过验证 / PlayerId 被新玩家复用 → 踢前复检
            if (!_unverified.Contains(playerId) || GetPlayerControl(playerId) == null) yield break;
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

    /// <summary>取房主的 PlayerId；无法确定时返回 <see cref="byte.MaxValue"/>。</summary>
    private static byte GetHostPlayerId()
    {
        try
        {
            var client = AmongUsClient.Instance?.GetClient(AmongUsClient.Instance.HostId);
            if (client?.Character != null) return client.Character.PlayerId;
        }
        catch { }
        return byte.MaxValue;
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
        _pending.Remove(playerId);
        _unverified.Remove(playerId);
        _verified.Remove(playerId);
        _joinTimes.Remove(playerId);
        _issuedNonces.Remove(playerId);
        _names.Remove(playerId);
        _origNameColors.Remove(playerId);
    }
}