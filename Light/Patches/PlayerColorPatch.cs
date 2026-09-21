using System;
using HarmonyLib;
using LightInDark.Core;
using UnityEngine;

namespace Light.Patches;

/// <summary>是否允许玩家颜色重复（未来接配置项/UI，默认允许）。</summary>
public static class AllowColorDuplicate
{
    public static bool Enabled = true;
}

/// <summary>
/// 服务端采纳同色：PlayerControl.CheckColor 原本会把撞色顺延到下一个未被占用颜色，
/// 这里直接采用传入颜色广播（配合 PlayerColorUI 完全接管前端选择）。
/// </summary>
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckColor))]
public static class PlayerControlColorRepeatPatch
{
    public static bool Prefix(PlayerControl __instance, byte bodyColor)
    {
        try
        {
            if (!AllowColorDuplicate.Enabled) return true;
            if (bodyColor >= Palette.PlayerColors.Length) return true;
            __instance.RpcSetColor(bodyColor);
            return false;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PlayerControlColorRepeatPatch.Prefix]", ex);
            return true;
        }
    }
}

/// <summary>
/// [TODO] 自定义调色板/调色盘系统（半架空原版颜色系统的下一步）。
///  - 已接管前端：PlayerColorUI 自绘色块；
///  - 未来：在玩家换装右上角加"调色盘"按钮（先留 TODO，暂不动手），
///    打开后允许自定义 RGB 并维护"玩家Id → 覆盖颜色"表，Palette.PlayerColors 可运行时覆写。
/// </summary>
public static class CustomColorSystem
{
}
