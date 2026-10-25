using System;
using System.Collections.Generic;
using LightInDark.Core;
using LightInDark.Game;
using UnityEngine;

namespace LightInDark.Visual
{
    /// <summary>
    /// 玩家外观覆盖：小黑人（匿名外观）。
    /// 效果：清空帽子 / 皮肤 / 面罩 / 宠物，身体改成灰色，名字隐藏；可恢复原样。
    /// </summary>
    public static class PlayerAppearance
{
    /// <summary>
    /// 匿名（小黑人）用的**外观层标签** ✓
    /// <para>全场用 <see cref="GlobalTag"/>；单个玩家 / 按 Buff 用各自 tag（可以并存 ✓ 互不干扰 ✓）</para>
    /// </summary>
    public const string GlobalTag = "appearance.anonymous.global";

    /// <summary>匿名外观的**身体颜色**：AU 原版调色板第 6 个（深灰黑 `#3F474E`）✓</summary>
    public const int AnonymousColorId = 6;

    /// <summary>匿名外观的优先级（比常规外观层高 ✓ 数字大优先）</summary>
    public const int AnonymousPriority = 100;

    private static bool _globalAnonymous;

    /// <summary>全场小黑人是否开启。</summary>
    public static bool IsGlobalAnonymous => _globalAnonymous;

    /// <summary>这个玩家现在是不是小黑人 ✓（全场开关 **或** 自己身上有覆盖层）</summary>
    public static bool IsAnonymous(Player player)
    {
        if (player == null || player.Control == null) return false;
        if (_globalAnonymous) return true;
        return OutfitStack.Has(player.Control, GlobalTag);
    }

    /// <summary>
    /// 造一层"小黑人"外观（照 NOS 的 `Camouflage`：**无帽 / 无皮肤 / 无面罩 / 无宠物 + 深灰身体 + 空名字** ✓）
    /// <para>对应 NOS：`new PlayerOutfitData(100, "", 38, "", "", "", "")` →
    ///   「优先级 100 / 名字空 / 颜色 38 / 帽空 / 面罩空 / 皮肤空 / 宠物空」✓
    ///   我们颜色用 AU 原版第 6 号（深灰黑，合法索引 ✓ —— NOS 的 38 是它自己的扩展调色板 ✗ 我们不能照抄数字）</para>
    /// </summary>
    /// <param name="tag">标签（同一个标签再推 = 覆盖 ✓）</param>
    /// <param name="priority">优先级（默认 <see cref="AnonymousPriority"/> ✓）</param>
    public static OutfitLayer CreateAnonymousLayer(string tag, int priority = AnonymousPriority)
        => new()
        {
            Tag = tag,
            Priority = priority,
            Name = "",                                  // 名字变空 = 看不到名字 ✓（比 SetActive(false) 稳 ✓）
            ColorId = AnonymousColorId,
            HatId = CosmeticsLayer.EMPTY_HAT_ID,
            SkinId = CosmeticsLayer.EMPTY_SKIN_ID,
            VisorId = CosmeticsLayer.EMPTY_VISOR_ID,
            PetId = CosmeticsLayer.EMPTY_PET_ID,
        };

    /// <summary>全场生效：把**所有玩家**都变成小黑人（或全部恢复）✓ 可重复调、可嵌套 ✓</summary>
    public static void SetAnonymousAll(bool enabled)
    {
        _globalAnonymous = enabled;

        foreach (var player in Player.AllPlayers)
        {
            try { SetAnonymous(player, enabled); }
            catch { }
        }

        LightLogger.Log($"[PlayerAppearance] 全场小黑人 {(enabled ? "开启" : "关闭")}");
    }

    /// <summary>开启 / 关闭**单个玩家**的小黑人外观 ✓（走外观栈，不影响别人 ✓）</summary>
    public static void SetAnonymous(Player player, bool enabled)
    {
        try
        {
            var control = player?.Control;
            if (control == null) return;

            if (enabled) OutfitStack.Push(control, CreateAnonymousLayer(GlobalTag));
            else OutfitStack.Pop(control, GlobalTag);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[PlayerAppearance] SetAnonymous 失败: {ex.Message}");
        }
    }

    /// <summary>低频兜底：原版换装 / 场景重载 / 会议重置后把栈顶重新压回去 ✓</summary>
    public static void EnsureApplied(Player player)
    {
        try
        {
            var control = player?.Control;
            if (control == null) return;
            OutfitStack.Refresh(control);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[PlayerAppearance] EnsureApplied 失败: {ex.Message}");
        }
    }

    /// <summary>换局 / 离开：恢复全部被改过的玩家 ✓</summary>
    public static void ClearAll()
    {
        try { OutfitStack.ClearAll(); }
        catch { }
        _globalAnonymous = false;
    }
}
}
