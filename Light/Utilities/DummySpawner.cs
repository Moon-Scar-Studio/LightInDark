using System;
using System.Collections;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InnerNet;
using UnityEngine;

namespace Light.Utilities;

/// <summary>假人生成器：房主本地生成 Dummy，补足人数便于测试。</summary>
public static class DummySpawner
{
    /// <summary>
    /// 同步生成 count 个假人（开局路径专用）。
    /// 与 <see cref="CoSpawn"/> 的区别：这里**不 yield**，因为 <c>GameStartManager.BeginGame</c>
    /// 是同步流程，假人必须在船生成前就全部进入 GameData。
    /// </summary>
    public static void SpawnImmediate(int count)
    {
        for (int i = 0; i < count; i++)
        {
            try { SpawnOne(); }
            catch (Exception ex)
            {
                LightInDark.Core.LightLogger.LogError($"[DummySpawner] 第 {i + 1} 个假人生成失败", ex);
                break;      // 失败就停，避免刷一屏同样的异常
            }
        }
    }

    /// <summary>逐帧生成 count 个假人，完成时回调提示消息。</summary>
    public static IEnumerator CoSpawn(int count, Action<string> onMessage)
    {
        for (int i = 0; i < count; i++)
        {
            try { SpawnOne(); }
            catch (Exception ex)
            {
                LightInDark.Core.LightLogger.LogError("[DummySpawner]", ex);
                yield break;
            }
            yield return null;
        }
        onMessage?.Invoke($"已添加 {count} 个假人");
    }

    /// <summary>生成单个假人（参考 Nebula AmongUsUtil.SpawnDummy）。</summary>
    private static void SpawnOne()
    {
        sbyte availableId = GameData.Instance.GetAvailableId();
        if (availableId < 0)
        {
            LightInDark.Core.LightLogger.LogWarning("[DummySpawner] 没有可用 PlayerId，停止生成");
            return;
        }

        var pc = UnityEngine.Object.Instantiate(AmongUsClient.Instance.PlayerPrefab);
        byte id = pc.PlayerId = (byte)availableId;
        pc.isDummy = true;

        var info = GameData.Instance.AddDummy(pc);
        pc.transform.position = PlayerControl.LocalPlayer.transform.position;
        pc.GetComponent<DummyBehaviour>().enabled = true;
        pc.isDummy = true;

        pc.SetName(AccountManager.Instance.GetRandomName());
        pc.SetColor(GetUnusedColor());
        pc.SetHat(CosmeticsLayer.EMPTY_HAT_ID, id);
        pc.SetVisor(CosmeticsLayer.EMPTY_VISOR_ID, id);
        pc.SetSkin(CosmeticsLayer.EMPTY_SKIN_ID, id);
        pc.SetPet(CosmeticsLayer.EMPTY_PET_ID, id);

        // 注意：Nebula 在这里调 UncertifiedPlayer.Certify()，
        // 但 **19.0 反编译源码里根本没有 UncertifiedPlayer 类型**（那是旧版本的类）→ 不调用。
        // 假人的认证走 AmongUsClient.Spawn 这条原版路径即可。

        AmongUsClient.Instance.Spawn(pc, -2, SpawnFlags.None);
        info.RpcSetTasks(new Il2CppStructArray<byte>(0));
    }

    /// <summary>取一个未被占用的颜色，全部占用时随机。</summary>
    private static byte GetUnusedColor()
    {
        int count = Palette.PlayerColors.Length;
        for (byte c = 0; c < count; c++)
        {
            bool used = false;
            foreach (var p in PlayerControl.AllPlayerControls)
                if (p.cosmetics != null && p.cosmetics.ColorId == c) { used = true; break; }
            if (!used) return c;
        }
        return (byte)UnityEngine.Random.Range(0, count);
    }
}
