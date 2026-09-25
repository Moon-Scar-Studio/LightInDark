using System;
using System.Collections.Generic;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Events;
using LightInDark.Game;

namespace LightInDark.Roles
{
    /// <summary>
    /// 职业基类（单类声明：一个职业一个类）。
    /// 定义数据用虚属性（注册实例上读取），运行时行为在 <see cref="OnActivated"/> 中实现。
    /// 注册时 RoleRegistry 持有一个"定义实例"（MyPlayer==null）供查询与分配；
    /// 分配时 <see cref="CreateInstance"/> 生成绑定玩家的运行时实例。
    ///
    /// 使用约定：
    ///  - 必须重写 <see cref="CodeName"/>（唯一内部名，兼作语言/配置键前缀）
    ///  - 必须重写 <see cref="IntroBlurbKey"/>（开场白语言键，译文不可为空）
    ///  - 可选重写 <see cref="IntroSFX"/>（相对路径 mp3，YouAreText 出现时播放）
    ///  - 静态配置用 <see cref="RoleOptionAttribute"/> 标记（注册时自动扫描绑定 cfg）
    /// </summary>
    public abstract class Role : ILifespan, IGameOperator, IBindPlayer
    {
        // =====================================================================
        // 定义侧（注册实例上读取）
        // =====================================================================

        /// <summary>唯一内部名（语言/配置键前缀）。必须重写。</summary>
        public abstract string CodeName { get; }

        /// <summary>职业颜色（名字/开场白着色）。</summary>
        public virtual Color Color => Color.White;

        /// <summary>阵营。</summary>
        public virtual RoleCategory Category => RoleCategory.Crewmate;

        /// <summary>注册序号（RPC 用）。</summary>
        public int Id { get; internal set; }

        /// <summary>职业名语言键。可重写，默认 "&lt;CodeName&gt;.name"。</summary>
        public virtual string NameKey => $"{CodeName}.name";

        /// <summary>职业描述语言键。可重写，默认 "&lt;CodeName&gt;.describe"。</summary>
        public virtual string DescriptionKey => $"{CodeName}.describe";

        /// <summary>开场白语言键。必须重写，且译文不可为 null/空（注册时校验）。</summary>
        public abstract string IntroBlurbKey { get; }

        /// <summary>技能介绍语言键。可重写，默认 "&lt;CodeName&gt;.skill"。</summary>
        public virtual string SkillDescriptionKey => $"{CodeName}.skill";

        /// <summary>
        /// 开场音效：相对路径指向打包进 dll 的 mp3 资源（如 "./Resources/SFX/CallerIntro.mp3"）。
        /// 为 null/空时不播放。YouAreText 出现时由 IntroPatch 调用 SfxManager.Play 播放。
        /// </summary>
        public virtual string IntroSFX => null;

        /// <summary>分配参数（默认不参与分配）。</summary>
        public virtual AllocationParameters Allocation => default;

        /// <summary>默认参数（实例化时使用）。</summary>
        public virtual int[] DefaultArguments => Array.Empty<int>();

        /// <summary>该职业是否可在本局生成（分配机调用）。</summary>
        public virtual bool CanSpawnIn() => true;

        /// <summary>职业立绘（帮助详情左上角），为 null 时不显示。</summary>
        public virtual UnityEngine.Sprite IconImage => null;

        // =====================================================================
        // 显示文本（语言键解析）
        // =====================================================================

        /// <summary>显示名（默认按语言键 <see cref="NameKey"/> 解析，缺省回退 CodeName）。</summary>
        public string Name => LightInDark.Language.Language.GetStringOrKey(NameKey, CodeName);

        /// <summary>职业描述（按语言键解析，缺省回退 CodeName）。</summary>
        public string Description => LightInDark.Language.Language.GetStringOrKey(DescriptionKey, CodeName);

        /// <summary>开场白（按语言键解析，缺省回退空串）。</summary>
        public string IntroBlurb => LightInDark.Language.Language.GetStringOrKey(IntroBlurbKey, "");

        /// <summary>技能介绍（按语言键解析）。</summary>
        public string SkillDescription => LightInDark.Language.Language.GetStringOrKey(SkillDescriptionKey, "");

        // =====================================================================
        // 运行时侧（绑定玩家）
        // =====================================================================

        /// <summary>绑定的玩家；定义实例（未分配）为 null。</summary>
        public Player MyPlayer { get; internal set; }

        /// <summary>是否为本地玩家。</summary>
        public bool AmOwner => MyPlayer?.AmOwner ?? false;

        /// <summary>绑定的玩家是否已死亡/断开。</summary>
        public bool IsDeadObject => MyPlayer?.IsDeadObject ?? false;

        /// <summary>角色是否已激活（已分配）。</summary>
        public bool IsActive { get; private set; }

        // =====================================================================
        // 创建与生命周期
        // =====================================================================

        /// <summary>
        /// 创建绑定指定玩家的运行时实例。
        /// 默认通过无参构造生成同类新实例；需要传参初始化的职业可重写。
        /// </summary>
        public virtual Role CreateInstance(Player player, int[] arguments)
        {
            try
            {
                if (player == null) return null;
                var instance = (Role)Activator.CreateInstance(GetType());
                instance.Id = Id;
                instance.MyPlayer = player;
                EventSystem.RegisterInstance(instance);
                instance.Activate();
                return instance;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("Role.CreateInstance", ex);
                return null;
            }
        }

        /// <summary>激活：执行 OnActivated 并刷新名字显示。</summary>
        internal void Activate()
        {
            try
            {
                IsActive = true;
                try { OnActivated(); }
                catch (Exception ex) { LightLogger.LogWarning($"[Role] {CodeName} OnActivated 失败: {ex.Message}"); }
                try { UpdateNameDisplay(); }
                catch (Exception ex) { LightLogger.LogWarning($"[Role] {CodeName} UpdateNameDisplay 失败: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("Role.Activate", ex);
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
                catch (Exception ex) { LightLogger.LogWarning($"[Role] {CodeName} OnInactivated 失败: {ex.Message}"); }
                Release();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("Role.Inactivate", ex);
            }
        }

        /// <summary>子类覆写：激活后挂按钮/监听事件/执行技能。</summary>
        protected virtual void OnActivated() { }

        /// <summary>子类覆写：失活时清理。</summary>
        protected virtual void OnInactivated() { }

        /// <summary>释放：解除事件注册、恢复名字颜色。</summary>
        public void Release()
        {
            try
            {
                EventSystem.UnregisterInstance(this);
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
                LightLogger.LogError("Role.Release", ex);
            }
        }

        void IGameOperator.OnReleased() { }

        /// <summary>
        /// 任务完成事件监听。更新名字显示中的任务计数。
        /// </summary>
        [EventPriority(0)]
        void OnTaskComplete(PlayerTaskCompleteEvent ev)
        {
            try
            {
                if (MyPlayer?.Control != null && ev.Player == MyPlayer.Control)
                    UpdateNameDisplay();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("Role.OnTaskComplete", ex);
            }
        }

        // =====================================================================
        // 名字显示
        // =====================================================================

        private TMPro.TMP_Text _infoText;

        /// <summary>
        /// 更新玩家名字显示。
        /// - 名字颜色 = 角色颜色（仅自己）/ 白色（他人）
        /// - 名字上方创建 Info 子文本，显示：角色名 (已完成/总任务)
        /// </summary>
        public void UpdateNameDisplay()
        {
            try
            {
                var control = MyPlayer?.Control;
                if (control == null) return;
                if (control.cosmetics == null) return;
                if (control.cosmetics.nameText == null) return;

                try
                {
                    control.cosmetics.nameText.color = AmOwner
                        ? ColorHelper.ToUnityColor(Color)
                        : UnityEngine.Color.white;

                    if (_infoText == null)
                    {
                        var nameText = control.cosmetics.nameText;
                        _infoText = UnityEngine.Object.Instantiate(nameText, nameText.transform);
                        _infoText.gameObject.name = "Info";
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
                LightLogger.LogError("Role.UpdateNameDisplay", ex);
            }
        }

        private static string ColorToHex(Color color)
        {
            byte r = (byte)(color.R * 255f);
            byte g = (byte)(color.G * 255f);
            byte b = (byte)(color.B * 255f);
            byte a = (byte)(color.A * 255f);
            return $"{r:X2}{g:X2}{b:X2}{a:X2}";
        }

        // =====================================================================
        // 按钮工厂（简单易用：职业内直接调用即可，无需引用按钮命名空间）
        // =====================================================================

        /// <summary>
        /// 创建普通按钮并注册（点击触发一次技能 + 冷却）。
        /// </summary>
        public LightInDark.UI.Ability.AbilityButton CreateAbilityButton(
            LightInDark.UI.Ability.RoleButtonConfig config, Action onClick)
            => LightInDark.UI.Ability.AbilityButton.Create(this, config, onClick);

        /// <summary>
        /// 创建持续按钮（效果按钮）并注册：点一下开启效果，再点一下取消
        /// （<see cref="LightInDark.UI.Ability.EffectButton.AllowCancelByReclick"/> 默认 true）。
        /// </summary>
        public LightInDark.UI.Ability.EffectButton CreateEffectButton(
            LightInDark.UI.Ability.RoleButtonConfig config, Action onClick)
            => LightInDark.UI.Ability.EffectButton.Create(this, config, onClick);

        /// <summary>
        /// 创建会议目标按钮并注册：会议中每位（满足条件的）玩家一个按钮，点击触发回调。
        /// </summary>
        public LightInDark.UI.Ability.MeetingTargetButton CreateMeetingTargetButton(
            Action<MeetingHud, PlayerControl> onClick,
            Func<PlayerControl, bool> canAdd = null,
            UnityEngine.Sprite icon = null,
            string sfx = null)
            => LightInDark.UI.Ability.MeetingTargetButton.Create(this, onClick, canAdd, icon, sfx);

        /// <summary>
        /// 创建会议右下角按钮并注册：与技能按钮同款外观，仅会议期间显示（默认右下角可配置）。
        /// </summary>
        public LightInDark.UI.Ability.MeetingAbilityButton CreateMeetingAbilityButton(
            LightInDark.UI.Ability.RoleButtonConfig config, Action onClick)
            => LightInDark.UI.Ability.MeetingAbilityButton.Create(this, config, onClick);

        // =====================================================================
        // 便捷工具
        // =====================================================================

        /// <summary>所有存活玩家。</summary>
        public IEnumerable<Player> AlivePlayers() => RoleUtils.AlivePlayers();

        /// <summary>所有玩家。</summary>
        public IEnumerable<Player> AllPlayers() => RoleUtils.AllPlayers();

        /// <summary>最近的可选玩家（可带过滤与最大距离）。</summary>
        public Player ClosestPlayer(Func<Player, bool> predicate = null, float? maxDistance = null)
            => RoleUtils.FindClosestPlayer(MyPlayer, predicate, maxDistance);

        /// <summary>随机存活玩家。</summary>
        public Player RandomAlivePlayer() => RoleUtils.RandomAlivePlayer();

        /// <summary>两个玩家之间的距离。</summary>
        public float DistanceTo(Player other) => RoleUtils.GetDistance(MyPlayer, other);

        /// <summary>判断玩家是否在指定范围内。</summary>
        public bool IsInRange(Player other, float range) => RoleUtils.IsInRange(MyPlayer, other, range);
    }
}
