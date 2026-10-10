using System;
using HarmonyLib;
using LightInDark.Core;
using UnityEngine;

namespace Light.Patches
{
    /// <summary>
    /// 挡住原版 <c>SlicedAspectScaler</c> 的每帧 NRE 刷屏,并定位到具体物体。
    ///
    /// ---- 原版机制(19.0 源码) ----
    /// <code>
    /// // SlicedAspectScaler.Update()
    /// this.objectsToScale.ForEach(delegate(AspectScaledAsset ob)
    /// {
    ///     ob.ScaleObject(aspectDiff);      // ← ob 是"已销毁"的死引用 → NRE
    /// });
    ///
    /// // AspectScaledAsset.ScaleObject() —— 另一层会崩的地方
    /// this.allSprites.ForEach(delegate(ScaledSprite s)
    /// {
    ///     s.Sprite.size = ...;             // ← s.Sprite 也被销毁过 → NRE
    /// });
    /// </code>
    ///
    /// ---- 实测堆栈(用户提供) ----
    /// <code>
    /// NullReferenceException
    ///   at SlicedAspectScaler+&lt;&gt;c__DisplayClass4_0.&lt;Start&gt;b__0 (AspectScaledAsset ob) [0x00000]
    ///   at System.Collections.Generic.List`1[T].ForEach
    ///   at SlicedAspectScaler.Update ()
    /// </code>
    /// 崩在偏移 <c>0x00000</c> = lambda 的第一条指令 = <c>ob.ScaleObject(...)</c>,
    /// **说明 <c>ob</c> 自己是 null/已销毁**,不是再深一层的渲染器。
    ///
    /// ---- 这个补丁做什么 ----
    /// <c>Update</c> 的 Prefix:
    /// ① 扫一遍 <c>objectsToScale</c>,发现有死引用 → **返回 false 跳过原版**
    ///    (物体都没了,本来也没什么可缩放的;跳过换来的是"不再每帧刷 NRE")
    /// ② 第一次拦截时把**是哪个物体、第几个元素**记进日志,方便反查谁销毁了它
    /// ③ 一切正常 → 返回 true,原版照常跑,**行为零改动**
    ///
    /// 为什么这不是"掩盖问题":那个物体已经被销毁了,原版的缩放对它毫无意义;
    /// 唯一的效果就是每帧往日志里灌一条异常。真正要查的是"谁销毁了它",
    /// 而 ② 给出的信息正是查它的入口。
    /// </summary>
    [HarmonyPatch(typeof(SlicedAspectScaler))]
    public static class SlicedAspectScalerGuardPatch
    {
        private static bool _reported;
        private static int _blocked;

        [HarmonyPatch(nameof(SlicedAspectScaler.Update))]
        [HarmonyPrefix]
        public static bool Update_Prefix(SlicedAspectScaler __instance)
        {
            try
            {
                if (__instance == null) return true;

                var objs = __instance.objectsToScale;
                if (objs == null || objs.Count == 0) return true;

                for (int i = 0; i < objs.Count; i++)
                {
                    var ob = objs[i];
                    if (ob != null) continue;      // 这个元素是活的,继续查

                    // ── 找到死引用 ──
                    _blocked++;

                    if (!_reported)
                    {
                        _reported = true;
                        LightLogger.LogWarning(
                            "[AspectScaler守卫] 发现 objectsToScale 里有**已销毁**的元素,已跳过原版 Update 以止住 NRE 刷屏。\n" +
                            $"  挂载物体   : {Path(__instance.gameObject)}\n" +
                            $"  场景       : {Scene(__instance.gameObject)}\n" +
                            $"  死引用下标 : [{i}] / 共 {objs.Count} 个\n" +
                            $"  updateAlways = {__instance.updateAlways}\n" +
                            "  → 接下来要查的是「谁销毁了这个元素」,而不是继续放大这里的异常。");
                    }

                    // ★★ 2026-10-06 用户要求「能修还是顺便修一下」—— 这里**真正把它摘掉** ✓✓
                    //    原来只是 `return false` 跳过每帧的原版 Update ✗：
                    //      · 那个 SlicedAspectScaler **从此永久不工作** ✗（缩放功能静默失效 ✓）
                    //      · 死引用一直在列表里 → 每次进来都要重扫 ✓
                    //    现在把它从 `objectsToScale` 移除 ✓ → 列表恢复健康 ✓
                    //    → 下一帧起原版 Update 照常跑（缩放功能回来了 ✓）且不再 NRE ✓
                    //    ⚠️ 摘掉之后**继续扫**（可能还有别的死引用 ✓）→ 所以是 `i--` + `continue` ✓
                    try
                    {
                        objs.RemoveAt(i);
                        i--;
                        LightLogger.Log($"[AspectScaler守卫] 已把死引用（原下标 [{i + 1}]）从 objectsToScale 移除 ✓" +
                                        $"剩余 {objs.Count} 个元素");
                    }
                    catch (Exception ex)
                    {
                        LightLogger.LogWarning($"[AspectScaler守卫] 摘除死引用失败(本帧仍跳过原版): {ex.Message}");
                        return false;
                    }

                    continue;
                }
            }
            catch (Exception ex)
            {
                // 守卫自己出错就放行,绝不影响原版
                LightLogger.LogWarning($"[AspectScaler守卫] 检查失败(放行): {ex.Message}");
            }

            return true;   // 全部健康 → 原版照常
        }

        /// <summary>被拦下的帧数(诊断用)。</summary>
        public static int BlockedFrames => _blocked;

        private static string Path(GameObject go)
        {
            try
            {
                if (go == null) return "<null>";
                var sb = new System.Text.StringBuilder(go.name);
                var t = go.transform.parent;
                int depth = 0;
                while (t != null && depth++ < 8)
                {
                    sb.Insert(0, t.name + "/");
                    t = t.parent;
                }
                return sb.ToString();
            }
            catch { return "<path失败>"; }
        }

        private static string Scene(GameObject go)
        {
            try { return go == null ? "?" : go.scene.name; } catch { return "?"; }
        }
    }
}
