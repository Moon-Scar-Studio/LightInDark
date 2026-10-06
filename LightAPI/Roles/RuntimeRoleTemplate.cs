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
        /// <summary>职业模板（每个职业用 MyRole 单例覆写返回）。</summary>
        public abstract RoleTemplate Role { get; }

        /// <summary>绑定的原版玩家对象。</summary>
        public PlayerControl Owner { get; }

        /// <summary>绑定的玩家包装。</summary>
        public Player MyPlayer { get; }

        public bool AmOwner => MyPlayer?.AmOwner ?? false;
        public bool IsDeadObject => MyPlayer?.IsDeadObject ?? true;
        public bool IsActive { get; private set; }

        protected RuntimeRoleTemplate(PlayerControl owner, RoleTemplate template)
        {
            Owner = owner;
            try { MyPlayer = LightInDark.Game.GameManager.Instance.GetPlayer(owner.PlayerId); }
            catch { MyPlayer = null; }
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
        /// <summary>自己的胜利条件：主机每帧轮询，返回 true 即结束并判本职业（或本队）胜。</summary>
        public virtual bool CheckWin() => false;

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
