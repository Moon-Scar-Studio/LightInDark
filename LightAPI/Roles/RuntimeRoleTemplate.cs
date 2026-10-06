using System;
using System.Collections.Generic;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Events;
using LightInDark.Game;
using LightInDark.Roles;
using LightInDark.UI.Ability;

namespace LightInDark.Roles
{
    /// <summary>
    /// 职业运行时基类（每局每玩家一个实例）。
    /// 生命周期由框架驱动：<see cref="Activate"/> 时自动扫描注册本实例的全部事件方法
    /// （任意私有方法、单参数且参数类型为 IEvent 子类即被识别），失活/释放时自动注销并回收按钮。
    /// 职业行为写在 <see cref="OnActivated"/>/<see cref="OnInactivated"/> 与各事件方法里。
    /// </summary>
    public abstract class RuntimeRoleTemplate : ILifespan, IGameOperator, IBindPlayer
    {
        /// <summary>
        /// 职业模板（定义侧的 `MyRole` 单例）。
        ///
        /// ⚠️ 2026-10-06 审查 #2：原来这里是 `abstract` 属性，由每个子类再写一遍
        ///   `public override RoleTemplate Role => MyRole;` —— 等于**同一个事实写两遍**：
        ///   ctor 收到的 `template` 参数被**完全丢弃** ✗
        ///   危险在于：一旦调用方传进来的模板与子类硬写的 `MyRole` **不是同一个实例**
        ///   （历史上真的发生过 —— 见 `RoleRegistry` 的单例注册坑），
        ///   `runtime.Role` 与分配器用的模板就会**静默分裂**，以 template 为键的字典、`ReferenceEquals`
        ///   判断全部失配 ✓ 现在改成"ctor 存下来、基类只读属性暴露"，**只有一个真相**。
        ///   （Nebula 也是这个形状：`Assignable`/`Role` 是无状态单例，每玩家状态放 `PlayerData`。）
        /// </summary>
        public RoleTemplate Role { get; }

        /// <summary>绑定的原版玩家对象。</summary>
        public PlayerControl Owner { get; }

        /// <summary>
        /// 绑定的玩家包装。**懒加载 + 自愈**。
        ///
        /// ⚠️⚠️ 原来是在构造函数里**只解析一次**（`MyPlayer = GameManager.GetPlayer(owner.PlayerId)`）：
        ///   若那一刻玩家表还没建好（客户端早到的职业 RPC、建表时序差异），`MyPlayer` 会**永久为 null**
        ///   → `AmOwner` 恒 false、`IsDeadObject` 恒 true，
        ///     `ClosestPlayer` / `DistanceTo` / `IsInRange` / `AlivePlayers` 全链路**静默走空**，
        ///     全程不报错（用户看到的就是"这个职业好像没效果"）✗
        ///   Nebula 从不缓存 PlayerControl —— 它的每条钩子都把 playerId 传进来、需要时现查
        ///   （`Helpers.playerById(id)` / `GameData.AllPlayers[id]`）。这里取同样的思路：用到才查、查不到就重试一次。
        /// </summary>
        public Player MyPlayer
        {
            get
            {
                if (_myPlayer != null) return _myPlayer;
                try
                {
                    byte id = byte.MaxValue;
                    if (Owner != null) id = Owner.PlayerId;      // Owner 是 Unity 对象，别用 ?.（§4.6.1）
                    _myPlayer = LightInDark.Game.GameManager.Instance.GetPlayer(id);
                }
                catch { _myPlayer = null; }

                if (_myPlayer == null && !_warnedNoPlayer)
                {
                    _warnedNoPlayer = true;
                    LightLogger.LogWarning($"[RuntimeRole] {CodeName} 拿不到对应 Player（playerId={(Owner != null ? Owner.PlayerId.ToString() : "null")}）—— 该职业的玩家相关逻辑会走空");
                }
                return _myPlayer;
            }
        }

        private Player? _myPlayer;
        private bool _warnedNoPlayer;

        public bool AmOwner => MyPlayer?.AmOwner ?? false;
        public bool IsDeadObject => MyPlayer?.IsDeadObject ?? true;
        public bool IsActive { get; private set; }

        protected RuntimeRoleTemplate(PlayerControl owner, RoleTemplate template)
        {
            Owner = owner;

            // ★ 唯一的真相来源（审查 #2）：把调用方给的模板存下来，子类不再各自写 `=> MyRole`
            Role = template ?? throw new ArgumentNullException(nameof(template), "运行时职业必须绑定模板");

            // MyPlayer 由上面的属性懒加载（这里不再解析一次，避免"一次失败永久为 null"）
        }

        // ---- 模板便捷访问 ----

        public string CodeName => Role.CodeName;
        public string Name => Role.Name;
        public LightInDark.Color Color => Role.Color;
        public string IntroText => Role.IntroText;
        public string IntroSFX => Role.IntroSFX;
        public RoleCategory RoleCategory => Role.RoleCategory;

        // ---- 生命周期 ----

        /// <summary>激活：登记事件、执行 OnActivated、刷新名字显示（框架内部调用）。</summary>
        internal void Activate()
        {
            try
            {
                IsActive = true;
                EventSystem.RegisterInstance(this);
                try { OnActivated(); }
                catch (Exception ex) { LightLogger.LogWarning($"[RuntimeRole] {CodeName} OnActivated 失败: {ex.Message}"); }
                try { UpdateNameDisplay(); }
                catch (Exception ex) { LightLogger.LogWarning($"[RuntimeRole] {CodeName} UpdateNameDisplay 失败: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RuntimeRoleTemplate.Activate", ex);
            }
        }

        /// <summary>失活（换职/移除时）：执行 OnInactivated 并释放。</summary>
        public void Inactivate()
        {
            try
            {
                if (!IsActive) return;
                IsActive = false;
                try { OnInactivated(); }
                catch (Exception ex) { LightLogger.LogWarning($"[RuntimeRole] {CodeName} OnInactivated 失败: {ex.Message}"); }
                Release();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RuntimeRoleTemplate.Inactivate", ex);
            }
        }

        /// <summary>子类覆写：激活后挂按钮/执行技能。</summary>
        protected virtual void OnActivated() { }

        /// <summary>子类覆写：失活时清理。</summary>
        protected virtual void OnInactivated() { }

        // =====================================================================
        //  具名钩子（2026-10-06 审查 #8）
        //
        //  绑定规则（`EventSystem.CollectListenerMethods`）：**任意实例方法、单参数、
        //  参数类型是 IEvent 子类**就会被自动绑定 —— 会沿继承链一路找，所以基类里声明的这些
        //  `protected virtual` 同样会被绑定 ✓
        //
        //  于是职业作者**不需要知道事件类名**（`PlayerDeathEvent` 这类），
        //  只要 override 下面任意一个就行 —— 原来的写法（自己写一个私有一参方法）依然有效。
        //  这正对着审查 #8 的问题：原来写成两个参数 / 参数不是 IEvent / 名字拼错，
        //  都会**静默不绑定**（现在 EventSystem 会给"0 个监听方法"打 warning ✓）。
        //
        //  ⚠️ 这些事件是**全局**的（所有玩家都会进来），要判"是不是我自己"请用
        //     `e.Player == Owner` 或 `MyPlayer`。
        // =====================================================================

        /// <summary>该局中**任意玩家**死亡时（`e.Player` 是死者、`e.Killer` 可能是空）。</summary>
        protected virtual void OnDied(Events.PlayerDeathEvent e) { }

        /// <summary>任意玩家复活时（`e.Healer` 可能是空）。</summary>
        protected virtual void OnRevived(Events.PlayerReviveEvent e) { }

        /// <summary>任意玩家被放逐时（`e.Exiled` 是被放逐者；平票时为空）。</summary>
        protected virtual void OnExiled(Events.PlayerExileEvent e) { }

        /// <summary>任意玩家断开连接时。</summary>
        protected virtual void OnDisconnected(Events.PlayerDisconnectEvent e) { }

        /// <summary>会议开始时。</summary>
        protected virtual void OnMeetingStarted(Events.MeetingStartEvent e) { }

        /// <summary>会议结束时。</summary>
        protected virtual void OnMeetingEnded(Events.MeetingEndEvent e) { }

        /// <summary>
        /// **职业数据位变化时**（对齐 Nebula `Role.OnUpdateRoleData(int dataId, int newValue)`）。
        ///
        /// 用法：
        /// <code>
        ///   static readonly int VotesId = RoleData.RegisterId("MayorVotes");
        ///   RoleData.Set(MyPlayer.Control.PlayerId, VotesId, 3);   // 写（房主权威，框架同步）
        ///   protected override void OnRoleData(int dataId, int value) { if (dataId == VotesId) ... }
        /// </code>
        /// 这样职业的每玩家状态**不需要自己写 RPC**（见 <see cref="RoleData"/>）。
        /// </summary>
        protected internal virtual void OnRoleData(int dataId, int value) { }

        /// <summary>释放：注销事件、回收本职业按钮、恢复名字颜色、销毁 Info 文本。</summary>
        public void Release()
        {
            try
            {
                EventSystem.UnregisterInstance(this);
                try { RoleButtonManager.ReleaseButtonsOf(this); }
                catch { }
                try
                {
                    if (MyPlayer?.Control?.cosmetics?.nameText != null)
                        MyPlayer.Control.cosmetics.nameText.color = UnityEngine.Color.white;
                }
                catch { }
                if (_infoText != null)
                {
                    try { UnityEngine.Object.Destroy(_infoText.gameObject); } catch { }
                    _infoText = null;
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RuntimeRoleTemplate.Release", ex);
            }
        }

        void IGameOperator.OnReleased() { }

        /// <summary>任务完成时刷新名字上的任务计数。</summary>
        [EventPriority(0)]
        private void OnTaskComplete(PlayerTaskCompleteEvent ev)
        {
            try
            {
                if (MyPlayer?.Control != null && ev.Player == MyPlayer.Control)
                    UpdateNameDisplay();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RuntimeRoleTemplate.OnTaskComplete", ex);
            }
        }

        // ---- 名字显示 ----

        private TMPro.TMP_Text _infoText;

        /// <summary>
        /// 名字颜色 = 角色色（仅自己）/ 白色（他人）；
        /// 名字上方 Info 子文本显示：角色名 (已完成/总任务)。
        /// </summary>
        public void UpdateNameDisplay()
        {
            try
            {
                var control = MyPlayer?.Control;
                if (control?.cosmetics?.nameText == null) return;

                try
                {
                    control.cosmetics.nameText.color = AmOwner
                        ? ColorHelper.ToUnityColor(Color)
                        : UnityEngine.Color.white;

                    if (_infoText == null)
                    {
                        var nameText = control.cosmetics.nameText;

                        // 清掉残留的旧 Info（换职业时旧文本同帧未销毁，会被克隆带进新层级）
                        var leftover = nameText.transform.Find("Info");
                        if (leftover != null)
                            UnityEngine.Object.Destroy(leftover.gameObject);

                        _infoText = UnityEngine.Object.Instantiate(nameText, nameText.transform);
                        _infoText.gameObject.name = "Info";

                        // 克隆会连子对象一起复制，把带进来的旧 Info 层级清掉，
                        // 否则每次换职业嵌套一层、职业名越显越高
                        for (int i = _infoText.transform.childCount - 1; i >= 0; i--)
                        {
                            var child = _infoText.transform.GetChild(i);
                            if (child != null && child.name == "Info")
                                UnityEngine.Object.Destroy(child.gameObject);
                        }

                        _infoText.fontSize = nameText.fontSize * 0.75f;
                        _infoText.transform.localPosition = new UnityEngine.Vector3(0f, 0.15f, 0f);
                        _infoText.alignment = TMPro.TextAlignmentOptions.Bottom;
                        _infoText.enableWordWrapping = false;
                        _infoText.raycastTarget = false;
                    }

                    string roleColorHex = ColorToHex(Color);
                    string roleStr = $"<color=#{roleColorHex}>{Name}</color>";

                    string taskStr = "";
                    if (control.Data?.Tasks != null && control.Data.Tasks.Count > 0)
                    {
                        int completed = 0;
                        int total = control.Data.Tasks.Count;
                        foreach (var task in control.Data.Tasks)
                        {
                            if (task != null && task.Complete) completed++;
                        }
                        taskStr = $" <color=#FAD934FF>({completed}/{total})</color>";
                    }

                    if (AmOwner)
                    {
                        _infoText.text = $"{roleStr}{taskStr}";
                        _infoText.gameObject.SetActive(true);
                    }
                    else
                    {
                        _infoText.text = "";
                        _infoText.gameObject.SetActive(false);
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RuntimeRoleTemplate.UpdateNameDisplay", ex);
            }
        }

        private static string ColorToHex(LightInDark.Color color)
        {
            byte r = (byte)(color.R * 255f);
            byte g = (byte)(color.G * 255f);
            byte b = (byte)(color.B * 255f);
            byte a = (byte)(color.A * 255f);
            return $"{r:X2}{g:X2}{b:X2}{a:X2}";
        }

        // ---- 便捷工具 ----

        public IEnumerable<Player> AlivePlayers() => RoleUtils.AlivePlayers();
        public IEnumerable<Player> AllPlayers() => RoleUtils.AllPlayers();
        public Player ClosestPlayer(Func<Player, bool> predicate = null, float? maxDistance = null)
            => RoleUtils.FindClosestPlayer(MyPlayer, predicate, maxDistance);
        public Player RandomAlivePlayer() => RoleUtils.RandomAlivePlayer();
        public float DistanceTo(Player other) => RoleUtils.GetDistance(MyPlayer, other);
        public bool IsInRange(Player other, float range) => RoleUtils.IsInRange(MyPlayer, other, range);
    }
}
