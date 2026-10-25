using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;   // WrapToIl2Cpp()：托管 IEnumerator → IL2CPP 的 IEnumerator
using Il2CppInterop.Runtime.Injection;
using LightInDark.Core;
using UnityEngine;

namespace LightInDark.Core
{
    /// <summary>
    /// **全局协程启动器**（照 NOS 的 `NebulaManager.Instance.StartCoroutine` 做的 ✓）
    ///
    /// ═══════════════════════════════════════════════════════════════════════
    /// 【为什么需要它】
    ///   1. 模组代码（职业、RPC 回调、事件处理）绝大多数是**静态方法/普通类**，
    ///      **没有** MonoBehaviour 可以 `StartCoroutine` ✗
    ///   2. 以前只能借别人的：`HudManager.Instance.StartCoroutine(...)` /
    ///      `Dispatcher.Instance.StartCoroutine(...)` ✗ —— 那些对象**切场景就可能没了**
    ///      （典型症状：大厅能跑的协程，进游戏后直接不跑 / 报空引用 ✗）
    ///   3. NOS 的做法是挂一个**常驻 MonoBehaviour**（`NebulaManager : MonoBehaviour`），
    ///      再 `NebulaManager.Instance.StartCoroutine(...)` ✓ —— 这里照抄这个思路 ✓
    ///
    /// 【用法】
    /// <code>
    /// // ① 普通协程（**托管迭代器直接传** ✓ 内部会自动 WrapToIl2Cpp）
    /// LightCoroutine.Start(CoMyThing(player));
    ///
    /// // ② 协程 + 结束回调（协程被销毁而中断时**不会**触发回调 ✓）
    /// LightCoroutine.Start(CoMyThing(), () =&gt; LightLogger.Log("放完了"));
    ///
    /// // ③ 最常用的两种简写
    /// LightCoroutine.Delay(1.5f, () =&gt; LightLogger.Log("1.5 秒后"));
    /// LightCoroutine.NextFrame(() =&gt; LightLogger.Log("下一帧"));
    ///
    /// // ④ 停止
    /// var co = LightCoroutine.Start(CoLoop());
    /// LightCoroutine.Stop(co);
    /// </code>
    ///
    /// 【跟原版 StartCoroutine 的三个差别（都踩过坑 ✓）】
    ///   · **托管 `IEnumerator` 必须 `WrapToIl2Cpp()`** ✗ 否则 IL2CPP 里会
    ///     "StartCoroutine(托管迭代器) 立刻返回、什么都不跑"（本工程所有旧代码都这么写 ✓）
    ///   · 宿主是 **DontDestroyOnLoad** ✓，但**实测会被场景切换销毁**（见 AGENTS §11.4）
    ///     → 这里做成**自愈**：每次 `Start` 都会 `Ensure()`，宿主没了就重建 ✓
    ///     ⚠️ 宿主被销毁时，**已经跑了一半的协程也会被中断** ✗ —— 这是 Unity 的限制，
    ///        需要"跨场景也一定跑完"的逻辑请用事件/计时器，别用协程 ✗
    ///   · `StopAll()` 只停**本类启动的**协程 ✓（不会误伤原版/别人挂的协程 ✓）
    /// ═══════════════════════════════════════════════════════════════════════
    /// </summary>
    public static class LightCoroutine
    {
        /// <summary>常驻宿主对象名（热重载/重复创建时用它去重 ✓）</summary>
        private const string HostName = "LID_CoroutineHost";

        private static CoroutineHost? _host;

        /// <summary>本类启动过的协程（`StopAll` 只动这些 ✓）</summary>
        private static readonly List<Coroutine> _running = new();

        /// <summary>宿主是否还活着（供诊断用 ✓）</summary>
        public static bool IsReady => _host != null;

        // ─────────────────────────────────────────────────────────────────
        //  启动
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// **启动协程**（托管 `IEnumerator` 直接传 ✓，内部自动 `WrapToIl2Cpp()`）
        /// </summary>
        /// <param name="routine">协程体（`yield return null` / `WaitForSeconds` 等照常用 ✓）</param>
        /// <returns>协程句柄（失败返回 null ✓，可传给 <see cref="Stop"/>）</returns>
        public static Coroutine Start(IEnumerator routine)
        {
            if (routine == null) return null;

            var host = Ensure();
            if (host == null) return null;

            try
            {
                var co = host.StartCoroutine(routine.WrapToIl2Cpp());
                if (co != null)
                {
                    _running.Add(co);
                    // 顺手清掉已结束的句柄（数量很小，够用 ✓）
                    if (_running.Count > 128) _running.RemoveAll(c => c == null);
                }
                return co;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("LightCoroutine.Start", ex);
                return null;
            }
        }

        /// <summary>启动协程，**正常跑完后**回调 <paramref name="onComplete"/> ✓（被中断则不回调 ✓）</summary>
        /// <param name="routine">协程体 ✓</param>
        /// <param name="onComplete">结束回调（可为 null ✓）</param>
        public static Coroutine Start(IEnumerator routine, Action onComplete)
            => onComplete == null ? Start(routine) : Start(CoThen(routine, onComplete));

        /// <summary>**延时执行**（秒，受 `Time.timeScale` 影响 ✓；要真实时间用 <see cref="DelayRealtime"/>）</summary>
        /// <param name="seconds">延迟秒数 ✓</param>
        /// <param name="action">到点执行的动作（null = 只等待 ✓）</param>
        public static Coroutine Delay(float seconds, Action action = null)
            => Start(CoDelay(seconds, action, useRealtime: false));

        /// <summary>**延时执行**（真实时间，`timeScale = 0` 也照走 ✓ —— 会议/暂停里也能用）</summary>
        /// <param name="seconds">延迟秒数 ✓</param>
        /// <param name="action">到点执行的动作 ✓</param>
        public static Coroutine DelayRealtime(float seconds, Action action = null)
            => Start(CoDelay(seconds, action, useRealtime: true));

        /// <summary>**下一帧**执行 ✓</summary>
        /// <param name="action">要执行的动作 ✓</param>
        public static Coroutine NextFrame(Action action) => Start(CoNextFrame(action));

        /// <summary>**等条件成立**再执行（每帧检查 ✓；`timeout` 秒后放弃 → 返回 false 不执行 ✓）</summary>
        /// <param name="condition">条件（返回 true 时继续 ✓）</param>
        /// <param name="action">条件成立后执行 ✓</param>
        /// <param name="timeout">超时秒数（`&lt;= 0` = 不超时 ✓）</param>
        public static Coroutine WaitUntil(Func<bool> condition, Action action = null, float timeout = 0f)
            => Start(CoWaitUntil(condition, action, timeout));

        // ─────────────────────────────────────────────────────────────────
        //  停止
        // ─────────────────────────────────────────────────────────────────

        /// <summary>停止一个协程 ✓（null / 已结束 → 什么都不做 ✓）</summary>
        /// <param name="coroutine"><see cref="Start"/> 的返回值 ✓</param>
        public static void Stop(Coroutine coroutine)
        {
            if (coroutine == null) return;

            try
            {
                if (_host != null) _host.StopCoroutine(coroutine);
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[LightCoroutine.Stop] {ex.Message}");
            }

            _running.Remove(coroutine);
        }

        /// <summary>停掉**本类启动的**全部协程 ✓（不影响原版/别人的协程 ✓）</summary>
        public static void StopAll()
        {
            try
            {
                if (_host != null)
                {
                    foreach (var co in _running)
                        if (co != null) _host.StopCoroutine(co);
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[LightCoroutine.StopAll] {ex.Message}");
            }
            finally
            {
                _running.Clear();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  宿主（自愈 ✓）
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 确保宿主存在（没有就重建 ✓）。每帧驱动里调一次成本 ≈ 一次引用比较 ✓
        /// </summary>
        public static CoroutineHost Ensure()
        {
            try
            {
                // ⚠️ 用 `!= null`（Unity 的假 null 判断 ✓），不要用 `??`（§4.6.1）
                if (_host != null) return _host;

                // 场景里可能已经有（热重载 / 重建后残留 ✓）
                var existing = GameObject.Find(HostName);
                if (existing != null)
                {
                    _host = existing.GetComponent<CoroutineHost>();
                    if (_host != null) return _host;
                }

                var go = new GameObject(HostName);
                UnityEngine.Object.DontDestroyOnLoad(go);
                _host = go.AddComponent<CoroutineHost>();

                // 宿主重建 → 旧协程全没了 ✗，清掉过期句柄 ✓
                _running.Clear();

                LightLogger.Log("[LightCoroutine] 协程宿主已创建 ✓");
                return _host;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("LightCoroutine.Ensure", ex);
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  内部协程体
        // ─────────────────────────────────────────────────────────────────

        private static IEnumerator CoThen(IEnumerator routine, Action onComplete)
        {
            yield return routine;
            try { onComplete(); }
            catch (Exception ex) { LightLogger.LogWarning($"[LightCoroutine] 结束回调抛了：{ex.Message}"); }
        }

        private static IEnumerator CoDelay(float seconds, Action action, bool useRealtime)
        {
            if (seconds > 0f)
            {
                if (useRealtime) yield return new WaitForSecondsRealtime(seconds);
                else yield return new WaitForSeconds(seconds);
            }
            else
            {
                yield return null;      // 0 / 负数 = 下一帧 ✓（保证不是同步执行 ✓）
            }

            try { action?.Invoke(); }
            catch (Exception ex) { LightLogger.LogWarning($"[LightCoroutine.Delay] 回调抛了：{ex.Message}"); }
        }

        private static IEnumerator CoNextFrame(Action action)
        {
            yield return null;
            try { action?.Invoke(); }
            catch (Exception ex) { LightLogger.LogWarning($"[LightCoroutine.NextFrame] 回调抛了：{ex.Message}"); }
        }

        private static IEnumerator CoWaitUntil(Func<bool> condition, Action action, float timeout)
        {
            float start = Time.realtimeSinceStartup;
            while (condition != null)
            {
                bool ok;
                try { ok = condition(); }
                catch (Exception ex)
                {
                    LightLogger.LogWarning($"[LightCoroutine.WaitUntil] 条件抛了：{ex.Message}");
                    yield break;
                }
                if (ok) break;

                if (timeout > 0f && Time.realtimeSinceStartup - start >= timeout) yield break;   // 超时 → 不执行 ✓
                yield return null;
            }

            try { action?.Invoke(); }
            catch (Exception ex) { LightLogger.LogWarning($"[LightCoroutine.WaitUntil] 回调抛了：{ex.Message}"); }
        }

        // ─────────────────────────────────────────────────────────────────
        //  宿主类型
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 常驻宿主（`DontDestroyOnLoad`）✓
        /// <para>⚠️ 自建 MonoBehaviour **必须**先 `ClassInjector.RegisterTypeInIl2Cpp`（AGENTS §11.2），
        /// 放在**静态构造函数**里 ✓（第一次碰到该类型时自动注册 ✓）</para>
        /// </summary>
        public sealed class CoroutineHost : MonoBehaviour
        {
            static CoroutineHost()
            {
                ClassInjector.RegisterTypeInIl2Cpp<CoroutineHost>();
            }
        }
    }
}
