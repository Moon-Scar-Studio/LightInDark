using LightInDark.Core;
using UnityEngine;

namespace LightInDark.Events
{
    /// <summary>
    /// **门状态观察者**：**不 patch 任何原版方法**，每帧读 `ShipStatus.AllDoors[i].IsOpen` ✓
    ///
    /// ═══════════════════════════════════════════════════════════════════════
    /// 【为什么不用补丁了】（2026-10-25 用户多开测试崩溃，两次稳定复现）
    ///
    /// 崩溃栈：
    /// <code>
    /// Fatal error. System.AccessViolationException: Attempted to read or write protected memory.
    ///    at Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(IntPtr, IntPtr, Void**, IntPtr ByRef)
    ///    at DynamicClass.DMD&lt;AutoOpenDoor::SetDoorway&gt;(AutoOpenDoor, Boolean)
    ///    at DynamicClass.(il2cpp -> managed) SetDoorway(IntPtr, Byte, Il2CppMethodInfo*)
    /// </code>
    ///
    /// 原版（19.0）的继承链决定了一切：
    /// <code>
    /// SomeKindaDoor.SetDoorway(bool)   ← **abstract**（SomeKindaDoor.cs:8）
    ///   PlainDoor.SetDoorway(bool)     ← override（PlainDoor.cs:36）
    ///     AutoOpenDoor.SetDoorway(bool)← 再 override（AutoOpenDoor.cs:7）
    /// </code>
    /// → 补丁把这个**抽象虚方法**的实现变成了托管代码，Harmony 补丁体里再
    ///   `il2cpp_runtime_invoke` 去调原方法时，原生侧直接 AV 崩游戏 ✗
    ///   ⚠️ AV 不是托管异常 → `try/catch` **兜不住**，所以日志里也没有我们的报错行 ✗
    ///
    /// 【现在的行为】
    ///   · 每帧扫一遍门（原版就是 `OpenableDoor[]`，地图里通常 10~20 个 → 很便宜 ✓）
    ///   · 只关心 `false → true`（= 门开了 ✓），触发 <see cref="PlayerOpenDoorEvent"/>
    ///   · 「开门的人」= 离门**最近的存活玩家** ✓（原版没有现成归属信息，这是最接近的近似）
    ///   · ⚠️ `PlayerTryOpenDoorLocalEvent` / `PlayerTryOpenDoorHostEvent`（可取消）**不再触发** ✗
    ///     —— 取消能力必须有补丁，而补丁会崩游戏 → 只能放弃 ✓
    /// ═══════════════════════════════════════════════════════════════════════
    /// </summary>
    internal static class DoorWatcher
    {
        /// <summary>上次记录的门数组（换了飞船/换局 → 引用会变 ✓ 自动重建基线）</summary>
        private static OpenableDoor[] _doors;

        /// <summary>上一帧每扇门的开合状态</summary>
        private static bool[] _open = System.Array.Empty<bool>();

        /// <summary>每帧调一次（由 `GameUpdatePatch` 驱动 ✓）</summary>
        public static void Tick()
        {
            try
            {
                var ship = ShipStatus.Instance;
                if (ship == null)
                {
                    _doors = null;
                    _open = System.Array.Empty<bool>();
                    return;
                }

                var doors = ship.AllDoors;
                if (doors == null || doors.Length == 0)
                {
                    _doors = null;
                    _open = System.Array.Empty<bool>();
                    return;
                }

                // 换局 / 换飞船 / 门数组变了 → 只记基线，这一帧不触发 ✓（避免"一进游戏所有门都算被打开"）
                if (!ReferenceEquals(_doors, doors) || _open.Length != doors.Length)
                {
                    _doors = doors;
                    _open = new bool[doors.Length];
                    for (int i = 0; i < doors.Length; i++)
                    {
                        var d = doors[i];
                        _open[i] = d != null && d.IsOpen;      // 只读属性 ✓ 不碰原版调用链 ✓
                    }
                    return;
                }

                for (int i = 0; i < doors.Length; i++)
                {
                    var door = doors[i];
                    if (door == null) continue;

                    bool now, prev = _open[i];
                    try { now = door.IsOpen; }
                    catch { continue; }                        // 场景切换瞬间取不到 → 跳过本帧 ✓

                    _open[i] = now;
                    if (prev || !now) continue;                // 只关心 false → true ✓

                    var opener = NearestAlivePlayer(door.transform.position);
                    if (opener != null) EventTriggers.OnPlayerOpenDoor(opener, door);
                }
            }
            catch
            {
                // best-effort：门观察失败绝不能影响游戏 ✓（每帧高频，不打日志避免刷屏 ✗）
            }
        }

        /// <summary>离门最近的存活玩家（找不到 null ✓）</summary>
        private static PlayerControl NearestAlivePlayer(Vector3 position)
        {
            PlayerControl best = null;
            float bestDist = float.MaxValue;

            try
            {
                var all = PlayerControl.AllPlayerControls;
                if (all == null) return null;

                foreach (var pc in all)
                {
                    if (pc == null) continue;
                    var data = pc.Data;
                    if (data == null || data.IsDead || data.Disconnected) continue;

                    float dist = (pc.transform.position - position).sqrMagnitude;
                    if (dist >= bestDist) continue;
                    bestDist = dist;
                    best = pc;
                }
            }
            catch { return null; }

            return best;
        }
    }
}
