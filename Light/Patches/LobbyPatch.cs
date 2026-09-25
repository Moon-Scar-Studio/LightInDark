using HarmonyLib;
using LightInDark.Core;
using Light.Utilities;
using UnityEngine;

namespace Light.Patches;

[HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
public static class AddLobbyDecorations
{
    // ============ 装饰位置设置（在这里微调） ============
    // 门横幅（ReadyRoomBanner.png），挂在大厅根节点
    private static readonly Vector3 BannerPosition = new(0f, 4.1f, 0f);
    private static readonly Vector3 BannerScale = new(1f, 1.1f, 1f);

    // 箱子蛋糕（ReadyRoomCake.png），挂在左侧箱子 Leftbox 上
    private static readonly Vector3 CakePosition = new(0f, 0.7f, 0f);
    private static readonly Vector3 CakeScale = new(0.8f, 0.7f, 1f);
    // ===================================================

    public static void Postfix(LobbyBehaviour __instance)
    {
        try
        {
            // 原有 logo
            SpawnSprite(__instance.transform, "MyLobbyDecor", "Light.Resources.Lobby.LightInDark.png",
                new Vector3(0f, 3.5f, 0f), new Vector3(0.4f, 0.4f, 0.5f));

            // 门横幅
            SpawnSprite(__instance.transform, "ReadyRoomBanner", "Light.Resources.Map.Ready.ReadyRoomBanner.png",
                BannerPosition, BannerScale);

            // 箱子蛋糕
            var box = __instance.transform.FindChild("Leftbox");
            SpawnSprite(box != null ? box : __instance.transform, "ReadyRoomCake",
                "Light.Resources.Map.Ready.ReadyRoomCake.png", CakePosition, CakeScale, 100);
        }
        catch (System.Exception)
        {
        }
    }

    /// <summary>在指定父节点下创建一张精灵图</summary>
    private static void SpawnSprite(Transform parent, string name, string resource, Vector3 localPosition, Vector3 localScale, int sortingOrder = 0)
    {
        var sprite = ResourceHelper.LoadSpriteFromResource(resource);
        if (sprite == null) return;

        var obj = new GameObject(name);
        obj.transform.SetParent(parent);
        obj.transform.localPosition = localPosition;
        obj.transform.localScale = localScale;

        var renderer = obj.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = sortingOrder;
    }
}
