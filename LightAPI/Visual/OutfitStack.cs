using System;
using System.Collections.Generic;
using LightInDark.Core;
using LightInDark.Game;
using UnityEngine;

namespace LightInDark.Visual
{
    /// <summary>
    /// **一套外观覆盖**（照 NOS 的 `OutfitCandidate` / `PlayerOutfitData` 做的 ✓ 2026-10-25）。
    /// <para>每个字段 `null` = "这一项不动" ✓（只有给了值才覆盖）</para>
    /// </summary>
    public sealed class OutfitLayer
    {
        /// <summary>标签（同一个标签再推一次 = 覆盖旧的 ✓；弹栈按标签找 ✓）</summary>
        public string Tag = "";

        /// <summary>优先级（**数值越大越优先** ✓；栈顶生效）</summary>
        public int Priority = 0;

        /// <summary>名字（null = 不改；`""` = 隐藏名字 ✓）</summary>
        public string Name;

        /// <summary>身体颜色索引（&lt; 0 = 不改 ✓）</summary>
        public int ColorId = -1;

        public string HatId;
        public string SkinId;
        public string VisorId;
        public string PetId;
    }

    /// <summary>
    /// **外观覆盖栈**（每个玩家一条，照 NOS 的 `PlayerModInfo.outfits` 那套 ✓）
    ///
    /// ═══════════════════════════════════════════════════════════════════════
    /// 【为什么要有它】（2026-10-25 用户："隐蔽者的黑人效果问题很大，完全照抄 Nebula"）
    ///
    /// 旧实现（`PlayerAppearance` 直接改装扮 + `SetAnonymousAll` 一个全局 bool ✗）的三个真问题：
    ///   ① **多来源互相踩**：两个 Buff 同时要求匿名 → 先结束的那个就把全场恢复了 ✗
    ///   ② **只改本地**：别的客户端看不到（没有同步）✗
    ///   ③ **恢复靠"记一份原始快照"**：中途换装/换皮肤、会议重置后恢复出来是错的 ✗
    ///
    /// NOS 的做法是**每玩家一条外观栈**（`outfits.Insert` + `UpdateOutfit()`）：
    ///   推入 = 记一层；栈顶生效；弹出 = 重新应用栈顶（空了就回默认外观）✓
    ///   → 天然解决 ①②③：谁推谁弹互不干扰，永远回到"下一层的原样" ✓
    ///
    /// 【用法】
    /// <code>
    /// OutfitStack.Push(player.Control, new OutfitLayer { Tag = "camo", Priority = 100,
    ///     Name = "", ColorId = 6, HatId = "", SkinId = "", VisorId = "", PetId = "" });
    /// OutfitStack.Pop(player.Control, "camo");
    /// </code>
    /// ═══════════════════════════════════════════════════════════════════════
    /// </summary>
    public static class OutfitStack
    {
        private static readonly Dictionary<byte, List<OutfitLayer>> _layers = new();

        /// <summary>推入一层（同 Tag 覆盖 ✓，按 Priority 降序排 ✓，立刻应用栈顶 ✓）</summary>
        /// <param name="control">目标玩家 ✓</param>
        /// <param name="layer">外观层 ✓（Tag 不能空，否则直接忽略 ✗）</param>
        public static void Push(PlayerControl control, OutfitLayer layer)
        {
            if (control == null || layer == null || string.IsNullOrEmpty(layer.Tag)) return;

            try
            {
                byte id = control.PlayerId;
                if (!_layers.TryGetValue(id, out var list))
                {
                    list = new List<OutfitLayer>();
                    _layers[id] = list;
                }

                list.RemoveAll(l => l != null && l.Tag == layer.Tag);
                list.Add(layer);
                list.Sort((a, b) => (b?.Priority ?? 0).CompareTo(a?.Priority ?? 0));

                ApplyTop(control);
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[OutfitStack.Push] {ex.Message}");
            }
        }

        /// <summary>弹出一层（按 Tag ✓；空了就恢复默认外观 ✓）</summary>
        /// <param name="control">目标玩家 ✓</param>
        /// <param name="tag">要移除的标签 ✓</param>
        public static void Pop(PlayerControl control, string tag)
        {
            if (control == null || string.IsNullOrEmpty(tag)) return;

            try
            {
                byte id = control.PlayerId;
                if (!_layers.TryGetValue(id, out var list)) return;

                list.RemoveAll(l => l != null && l.Tag == tag);
                if (list.Count == 0) _layers.Remove(id);

                ApplyTop(control);
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[OutfitStack.Pop] {ex.Message}");
            }
        }

        /// <summary>把某个 Tag 从**所有玩家**身上弹掉（全场效果收尾用 ✓）</summary>
        /// <param name="tag">要移除的标签 ✓</param>
        public static void PopAll(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return;

            var ids = new List<byte>(_layers.Keys);
            foreach (var id in ids)
            {
                var control = Player.Get(id)?.Control;
                if (control != null) Pop(control, tag);
                else _layers.Remove(id);
            }
        }

        /// <summary>重新应用栈顶（原版换装 / 会议重置 / 场景切换后兜底 ✓）</summary>
        /// <param name="control">目标玩家 ✓</param>
        public static void Refresh(PlayerControl control)
        {
            if (control == null) return;
            if (!_layers.ContainsKey(control.PlayerId)) return;   // 没覆盖过的人不用管 ✓
            ApplyTop(control);
        }

        /// <summary>所有被覆盖过的玩家重新应用一遍 ✓</summary>
        public static void RefreshAll()
        {
            var ids = new List<byte>(_layers.Keys);
            foreach (var id in ids)
            {
                var control = Player.Get(id)?.Control;
                if (control == null) { _layers.Remove(id); continue; }
                ApplyTop(control);
            }
        }

        /// <summary>玩家身上是否有某 Tag 的覆盖 ✓</summary>
        public static bool Has(PlayerControl control, string tag)
        {
            if (control == null || string.IsNullOrEmpty(tag)) return false;
            return _layers.TryGetValue(control.PlayerId, out var list)
                && list.Exists(l => l != null && l.Tag == tag);
        }

        /// <summary>清空全部（换局 / 离开时调用 ✓）</summary>
        public static void ClearAll()
        {
            var ids = new List<byte>(_layers.Keys);
            foreach (var id in ids)
            {
                var control = Player.Get(id)?.Control;
                if (control != null)
                {
                    try { ApplyDefault(control); } catch { }
                }
            }
            _layers.Clear();
        }

        // ---- 内部 ----

        private static void ApplyTop(PlayerControl control)
        {
            try
            {
                if (control == null) return;

                if (!_layers.TryGetValue(control.PlayerId, out var list) || list.Count == 0)
                {
                    ApplyDefault(control);
                    return;
                }

                ApplyLayer(control, list[0]);
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[OutfitStack.ApplyTop] {ex.Message}");
            }
        }

        /// <summary>恢复成这个玩家**默认外观**（原版 `Data.DefaultOutfit` ✓，不是"入栈前的快照" ✗）</summary>
        private static void ApplyDefault(PlayerControl control)
        {
            var data = control.Data;
            var def = data?.DefaultOutfit;
            if (def == null) return;

            try
            {
                control.SetName(def.PlayerName);
                control.SetColor(def.ColorId);
                control.SetHat(def.HatId, def.ColorId);
                control.SetSkin(def.SkinId, def.ColorId);
                control.SetVisor(def.VisorId, def.ColorId);
                control.SetPet(def.PetId, def.ColorId);
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[OutfitStack.ApplyDefault] {ex.Message}");
            }
        }

        /// <summary>应用一层（只改有值的项 ✓）</summary>
        private static void ApplyLayer(PlayerControl control, OutfitLayer layer)
        {
            if (layer == null) return;

            int color = layer.ColorId >= 0 ? layer.ColorId
                      : (control.Data?.DefaultOutfit?.ColorId ?? 0);

            try
            {
                if (layer.Name != null) control.SetName(layer.Name);
                if (layer.ColorId >= 0) control.SetColor(layer.ColorId);
                if (layer.HatId != null) control.SetHat(layer.HatId, color);
                if (layer.SkinId != null) control.SetSkin(layer.SkinId, color);
                if (layer.VisorId != null) control.SetVisor(layer.VisorId, color);
                if (layer.PetId != null) control.SetPet(layer.PetId, color);
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[OutfitStack.ApplyLayer] {ex.Message}");
            }
        }
    }
}
