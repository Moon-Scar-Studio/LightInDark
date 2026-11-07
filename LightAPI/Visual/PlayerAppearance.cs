using System;
using System.Collections.Generic;
using LightInDark.Core;
using LightInDark.Game;
using LightInDark.RPCs;
using UnityEngine;

namespace LightInDark.Visual
{
    /// <summary>
    /// 匿名外观（小黑人）管理器。
    ///
    /// 设计要点：
    ///   · **状态**在本地维护（`_global` 全场开关 + `_single` 单人开关集合），外观由 <see cref="OutfitStack"/> 落地；
    ///   · **同步**：房主本地先落地再把状态广播给其他客户端，非房主只本地落地（房主是唯一真相）；
    ///   · **恢复**靠外观栈按标签弹出，弹空后回原版默认外观，不依赖"入栈前快照"；
    ///   · 全场开关关闭时额外清一遍所有残留层，避免玩家离开/换局后层留在旧 PlayerId 上。
    /// </summary>
    public static class PlayerAppearance
    {
        /// <summary>全场匿名使用的标签。</summary>
        public const string GlobalTag = "appearance.anonymous.global";

        /// <summary>单人匿名使用的默认标签（角色 / Buff 可传自己的标签隔离）。</summary>
        public const string SingleTag = "appearance.anonymous.one";

        /// <summary>匿名外观的身体颜色：AU 原版调色板第 6 号（深灰黑）。</summary>
        public const int AnonymousColorId = 6;

        /// <summary>匿名外观的优先级（数值大者优先）。</summary>
        public const int AnonymousPriority = 100;

        private const string RpcAllName = "LID.Appearance.AnonymousAll";
        private const string RpcOneName = "LID.Appearance.AnonymousOne";

        private static bool _global;
        private static readonly HashSet<byte> _single = new();

        private static bool _rpcReady;
        private static RemoteProcess<bool>? _rpcAll;
        private static RemoteProcess<(byte, bool)>? _rpcOne;

        /// <summary>全场小黑人是否开启（本地状态，房主广播后各端一致）。</summary>
        public static bool IsGlobalAnonymous => _global;

        /// <summary>这个玩家现在是不是小黑人（全场开启，或身上有任一层匿名外观）。</summary>
        public static bool IsAnonymous(Player player)
        {
            var control = player?.Control;
            if (control == null) return false;
            if (_global) return true;
            if (_single.Contains(control.PlayerId)) return true;
            return OutfitStack.Has(control, GlobalTag) || OutfitStack.Has(control, SingleTag);
        }

        /// <summary>
        /// 构造一层匿名外观（无帽 / 无皮肤 / 无面罩 / 无宠物 + 深灰身体 + 空名字）。
        /// 空装扮 ID 用字面量：`CosmeticsLayer.EMPTY_*` 是 C# const，编译期内联，
        /// IL2CPP 元数据里没有这些字段，运行时访问会抛异常。
        /// </summary>
        public static OutfitLayer CreateAnonymousLayer(string tag, int priority = AnonymousPriority)
            => new()
            {
                Tag = tag,
                Priority = priority,
                Name = "",
                ColorId = AnonymousColorId,
                HatId = "hat_NoHat",
                SkinId = "skin_None",
                VisorId = "visor_EmptyVisor",
                PetId = "pet_EmptyPet",
            };

        // =====================================================================
        //  对外：全场 / 单人
        // =====================================================================

        /// <summary>全场开关。发起端先落地再广播；接收端只落地不转发。</summary>
        public static void SetAnonymousAll(bool enabled, bool broadcast = true)
        {
            ApplyGlobalLocal(enabled);
            // 不限制房主：房主的 AmClient 为 false，且技能常在客户端触发；
            // 接收端 handler 不会再转发，不会形成风暴
            if (broadcast) RpcAll.Invoke(enabled);
        }

        /// <summary>
        /// 单个玩家开关。不同来源用不同 <paramref name="tag"/> 隔开，互不影响；
        /// 发起端先落地再广播，接收端只落地。
        /// </summary>
        public static void SetAnonymous(Player player, bool enabled,
                                        string tag = SingleTag, bool broadcast = true)
        {
            var control = player?.Control;
            if (control == null || string.IsNullOrEmpty(tag)) return;

            ApplySingleLocal(control.PlayerId, enabled, tag);
            if (broadcast) RpcOne.Invoke((control.PlayerId, enabled));
        }

        // =====================================================================
        //  对外：兜底与清理
        // =====================================================================

        /// <summary>低频兜底：全场开关与栈状态不一致时补齐（换装 / 会议 / 重连后）。</summary>
        public static void EnsureApplied(Player player)
        {
            try
            {
                var control = player?.Control;
                if (control == null) return;

                if (_global)
                {
                    if (!OutfitStack.Has(control, GlobalTag))
                        OutfitStack.Push(control, CreateAnonymousLayer(GlobalTag));
                }
                else if (OutfitStack.Has(control, GlobalTag))
                {
                    OutfitStack.Pop(control, GlobalTag);
                }

                OutfitStack.Refresh(control);
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[PlayerAppearance] EnsureApplied 失败: {ex.Message}");
            }
        }

        /// <summary>换局 / 离开：清空全部外观层并复位状态（各端各自清理，不广播）。</summary>
        public static void ClearAll()
        {
            try { OutfitStack.ClearAll(); } catch { }
            _global = false;
            _single.Clear();
        }

        // =====================================================================
        //  本地落地
        // =====================================================================

        private static void ApplyGlobalLocal(bool enabled)
        {
            _global = enabled;

            int total = 0, applied = 0;
            foreach (var player in Player.AllPlayers)
            {
                total++;
                var control = player?.Control;
                if (control == null) continue;
                try
                {
                    if (enabled) OutfitStack.Push(control, CreateAnonymousLayer(GlobalTag));
                    else OutfitStack.Pop(control, GlobalTag);
                    applied++;
                }
                catch (Exception ex)
                {
                    LightLogger.LogWarning($"[PlayerAppearance] 应用到玩家 {control.PlayerId} 失败: {ex}");
                }
            }

            // 关闭时清掉残留（玩家已离开 / 换了 PlayerId 的层不会被上面的循环覆盖）
            if (!enabled)
            {
                try { OutfitStack.PopAll(GlobalTag); } catch { }
            }

            LightLogger.Log($"[PlayerAppearance] 全场小黑人 {(enabled ? "开启" : "关闭")}（共 {total} 人，成功 {applied}）");
        }

        private static void ApplySingleLocal(byte playerId, bool enabled, string tag)
        {
            if (enabled) _single.Add(playerId);
            else _single.Remove(playerId);

            var control = Player.Get(playerId)?.Control;
            if (control == null) return;

            try
            {
                if (enabled) OutfitStack.Push(control, CreateAnonymousLayer(tag));
                else OutfitStack.Pop(control, tag);
            }
            catch { }
        }

        // =====================================================================
        //  同步
        // =====================================================================

        private static RemoteProcess<bool> RpcAll
        {
            get
            {
                EnsureRpc();
                return _rpcAll!;
            }
        }

        private static RemoteProcess<(byte, bool)> RpcOne
        {
            get
            {
                EnsureRpc();
                return _rpcOne!;
            }
        }

        private static void EnsureRpc()
        {
            if (_rpcReady) return;
            _rpcReady = true;

            try
            {
                // 接收端只落地，不再转发
                _rpcAll = new RemoteProcess<bool>(RpcAllName, (enabled, _) => ApplyGlobalLocal(enabled));
                _rpcOne = new RemoteProcess<(byte, bool)>(RpcOneName, (arg, _) => ApplySingleLocal(arg.Item1, arg.Item2, SingleTag));
            }
            catch (Exception ex)
            {
                _rpcReady = false;
                LightLogger.LogError("[PlayerAppearance] 注册同步 RPC 失败", ex);
            }
        }

        /// <summary>模组加载时调用一次，保证接收端在别的客户端也注册好。</summary>
        public static void EnsureRegistered() => EnsureRpc();
    }
}
