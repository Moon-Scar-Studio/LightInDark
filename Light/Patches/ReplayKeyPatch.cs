using HarmonyLib;
using LightInDark.Core;
using LightInDark.Utilities;
using Light.UI;
using System;

namespace Light.Patches;

/// <summary>
/// 大厅中按 V 键显示/隐藏复盘面板。
/// </summary>
[HarmonyPatch(typeof(KeyboardJoystick), nameof(KeyboardJoystick.Update))]
public static class ReplayKeyPatch
{
    private static bool _isShowing;

    public static void Postfix()
    {
        try
        {
            // 仅在大厅中生效
            if (LobbyBehaviour.Instance == null)
            {
                if (_isShowing)
                {
                    ReplayPanel.Hide();
                    _isShowing = false;
                }
                return;
            }

            // 仅在有复盘数据时生效
            if (LightInDark.Game.LightPlayerDataManager.AllPlayerData.Count == 0) return;

            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.V))
            {
                _isShowing = ReplayPanel.Toggle();
            }
        }
        catch (System.Exception)
        {
        }
    }
}
