using LightInDark.Core;
using LightInDark.Events;
using LightInDark.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LightInDark.Game
{
    public class GameManager : SimpleLifespan, IGame
    {
        private static GameManager _instance;
        public static GameManager Instance => _instance ??= new GameManager();

        private List<IGameOperator> _entities = new();

        public Player LocalPlayer { get; private set; }

        private GameManager() { }

        /// <summary>
        /// 建立/刷新玩家表。**幂等**：同一名玩家复用已有的 <see cref="Player"/>（连带它的职业一起保留）。
        ///
        /// ⚠️⚠️ 为什么必须幂等（这是"所有人开局后没有职业"的真正原因）：
        ///   本方法每局会被调用**不止一次** ——
        ///     ① 我们在 `RoleManager.SelectRoles` 的后置里调它 → 建好 Player → `StandardRoleAllocator` 分配职业；
        ///     ② 约 10 秒后开场动画结束，API 侧 `GameManager_StartGame_Patch` **又调一次**。
        ///   原来无条件 `_entities.Clear()` + 全部重建 → 新 `Player` 的 `Role` 是 null，
        ///   **刚分配好的职业整套丢掉**，而日志里一切正常（只有"没职业"这个现象）✗
        ///   现在：同 PlayerId 且同一个 PlayerControl 的**复用**；对象已销毁的旧实体才 `Release()` 掉重建。
        /// </summary>
        public void Initialize()
        {
            try
            {
                // ① 先摘掉已销毁的旧实体（换局后 PlayerControl 会被销毁 → IsDeadObject），并成对释放
                for (int i = _entities.Count - 1; i >= 0; i--)
                {
                    if (_entities[i] is not Player p) continue;
                    bool dead;
                    try { dead = p.IsDeadObject; } catch { dead = true; }
                    if (!dead) continue;

                    try { p.Release(); } catch { }
                    _entities.RemoveAt(i);
                }

                // ② 现存有效的 Player，按 PlayerId 建索引（复用它们 = 保住 Role）
                var byId = new Dictionary<byte, Player>();
                foreach (var p in _entities.OfType<Player>())
                {
                    try { byId[p.Control.PlayerId] = p; } catch { }
                }

                int reused = 0, created = 0;

                // ③ 本地玩家
                if (PlayerControl.LocalPlayer != null)
                {
                    byte id = PlayerControl.LocalPlayer.PlayerId;
                    if (byId.TryGetValue(id, out var exist) && exist.Control == PlayerControl.LocalPlayer)
                    {
                        LocalPlayer = exist;
                        reused++;
                    }
                    else
                    {
                        LocalPlayer = new Player(PlayerControl.LocalPlayer);
                        RegisterEntity(LocalPlayer, this);
                        created++;
                    }
                }

                // ④ 其他玩家
                foreach (var pc in PlayerControl.AllPlayerControls)
                {
                    if (pc == null || pc == PlayerControl.LocalPlayer) continue;

                    byte id = pc.PlayerId;
                    if (byId.TryGetValue(id, out var exist) && exist.Control == pc)
                    {
                        reused++;
                        continue;
                    }

                    // 同 Id 但换了对象（新一局 / PlayerId 被复用）→ 旧的先释放再建
                    if (byId.TryGetValue(id, out var stale))
                    {
                        try { stale.Release(); } catch { }
                        _entities.Remove(stale);
                    }

                    var player = new Player(pc);
                    RegisterEntity(player, this);
                    created++;
                }

                LightLogger.Log($"[GameManager] 初始化完成：复用 {reused} 个 Player（职业保留），新建 {created} 个（表内共 {_entities.Count}）");

                // ★ 玩家表刚建好 → 重放"早到的职业 RPC"。
                //   房主在自己的 SelectRoles 里就把 SetRole 发出来了，客户端可能还没建表，
                //   那些 RPC 会被挂起；不在这里重放，客户端就会**整局没有职业**。
                try { LightInDark.RPCs.RpcDefinitions.FlushPendingRoles(); } catch { }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("GameManager.Initialize", ex);
            }
        }

        public Player GetPlayer(byte playerId)
        {
            try
            {
                return _entities.OfType<Player>().FirstOrDefault(p => p.Control.PlayerId == playerId);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("GameManager.GetPlayer", ex);
                return null;
            }
        }

        public IEnumerable<Player> AllPlayers => _entities.OfType<Player>();

        public void RegisterEntity(IGameOperator entity, ILifespan lifespan)
        {
            try
            {
                _entities.Add(entity);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("GameManager.RegisterEntity", ex);
            }
        }

        public void UnregisterEntity(IGameOperator entity)
        {
            try
            {
                _entities.Remove(entity);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("GameManager.UnregisterEntity", ex);
            }
        }

        /// <summary>
        /// 每帧清理（由 `PlayerControl.FixedUpdate` 补丁驱动 —— 它同时也在更新按钮）。
        ///
        /// ⚠️ 2026-10-06 审查 #15：这个方法原来**没有任何调用点**（死代码）。
        /// ⚠️ 另外 `RemoveAll` 之前必须**成对 Release**：实体构造时会 `EventSystem.RegisterInstance(this)`，
        ///    直接丢掉引用会让事件系统永久持有它（跨局累积、仍指向已销毁的 PlayerControl）。
        /// </summary>
        public void Update()
        {
            try
            {
                for (int i = _entities.Count - 1; i >= 0; i--)
                {
                    IGameOperator e;
                    try { e = _entities[i]; } catch { _entities.RemoveAt(i); continue; }
                    if (e == null) { _entities.RemoveAt(i); continue; }

                    bool dead;
                    try { dead = e.IsDeadObject; } catch { dead = true; }
                    if (!dead) continue;

                    try { e.OnReleased(); } catch { }       // 成对释放（注销事件 / 回收按钮）
                    _entities.RemoveAt(i);
                }
                // 按钮更新由 PlayerControl.FixedUpdate 补丁驱动
                // 能力更新也由补丁驱动
            }
            catch (Exception ex)
            {
                LightLogger.LogError("GameManager.Update", ex);
            }
        }

        public new void Release()
        {
            try
            {
                base.Release();
                _instance = null;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("GameManager.Release", ex);
            }
        }
    }
}
