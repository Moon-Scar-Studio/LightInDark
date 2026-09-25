using System;
using System.Collections.Generic;
using LightInDark.Audio;
using LightInDark.Core;
using LightInDark.Game;
using LightInDark.Roles;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LightInDark.UI.Ability
{
    /// <summary>
    /// 会议目标按钮：会议开始时为每位（满足条件的）玩家克隆一个目标按钮
    /// （模板取自原版 CancelButton），点击某位玩家触发回调（如指认/猜身份）。
    /// 会议结束自动清理。
    ///
    /// 职业用法（OnActivated 中）：
    /// <code>
    /// MeetingTargetButton.Create(role,
    ///     onClick: (meeting, target) => DoSomething(target),
    ///     canAdd: p => p.Data != null && !p.Data.IsDead,
    ///     icon: myIcon,
    ///     sfx: "./Resources/SFX/Click.mp3");
    /// </code>
    /// </summary>
    public class MeetingTargetButton : RoleButtonBase
    {
        private sealed class FuncHolder
        {
            public Func<PlayerControl, bool> CanAdd;
            public Action<MeetingHud, PlayerControl> OnClick;
            public Sprite Icon;
            public string OnClickSFX;
        }

        private readonly FuncHolder _holder = new();
        private readonly List<GameObject> _created = new();

        private MeetingTargetButton(Role role, Player player, RoleButtonConfig config, Action onClick)
            : base(role, player, config, onClick) { }

        /// <summary>
        /// 创建会议目标按钮并注册到管理器。
        /// </summary>
        /// <param name="onClick">点击某位玩家时回调（参数为该玩家 PlayerControl）。</param>
        /// <param name="canAdd">是否对该玩家添加按钮（可空，默认排除自己与死者）。</param>
        /// <param name="icon">按钮图标（可空，默认用原版 CancelButton 贴图）。</param>
        /// <param name="sfx">点击音效相对路径（可空）。</param>
        public static MeetingTargetButton Create(Role role,
            Action<MeetingHud, PlayerControl> onClick,
            Func<PlayerControl, bool> canAdd = null,
            Sprite icon = null,
            string sfx = null)
        {
            try
            {
                var button = new MeetingTargetButton(role, role.MyPlayer, new RoleButtonConfig(), null);
                button._holder.OnClick = onClick;
                button._holder.CanAdd = canAdd
                    ?? (p => p != null && p.PlayerId != PlayerControl.LocalPlayer.PlayerId
                        && p.Data != null && !p.Data.IsDead);
                button._holder.Icon = icon;
                button._holder.OnClickSFX = sfx;
                RoleButtonManager.Register(button);
                return button;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[MeetingTargetButton.Create]", ex);
                return null;
            }
        }

        // ---- 会议生命周期（由 RoleButtonManager 的 MeetingHud patch 驱动）----

        internal void OnMeetingStart(MeetingHud meeting)
        {
            try
            {
                ClearCreated();
                if (PlayerControl.LocalPlayer?.Data?.IsDead == true) return;
                if (meeting?.playerStates == null) return;

                for (int i = 0; i < meeting.playerStates.Length; i++)
                {
                    var pva = meeting.playerStates[i];
                    if (pva == null) continue;
                    var player = GetPlayerById(pva.PlayerId);
                    if (player == null || pva.PlayerId == PlayerControl.LocalPlayer.PlayerId) continue;
                    if (!_holder.CanAdd(player)) continue;

                    var btn = CreateButtonForArea(pva);
                    if (btn == null) continue;
                    BindClick(btn, meeting, player);
                    _created.Add(btn);
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[MeetingTargetButton] OnMeetingStart", ex);
            }
        }

        internal void OnMeetingEnd()
        {
            ClearCreated();
        }

        private GameObject CreateButtonForArea(PlayerVoteArea pva)
        {
            var tplRoot = pva?.Buttons?.transform?.Find("CancelButton");
            GameObject tpl = tplRoot != null ? tplRoot.gameObject : null;
            if (tpl == null) return null;

            var go = Object.Instantiate(tpl, pva.transform);
            go.name = "CustomTargetButton";
            go.transform.localPosition = new Vector3(-0.95f, 0.03f, -1.31f);
            return go;
        }

        private void BindClick(GameObject go, MeetingHud meeting, PlayerControl player)
        {
            if (go == null) return;
            var passive = go.GetComponent<PassiveButton>();
            if (passive == null) return;

            if (_holder.Icon != null)
            {
                var sr = go.GetComponent<SpriteRenderer>();
                if (sr != null) sr.sprite = _holder.Icon;
            }

            passive.OnClick = new Button.ButtonClickedEvent();
            passive.OnClick.AddListener((UnityEngine.Events.UnityAction)(() =>
            {
                try
                {
                    SfxManager.Play(_holder.OnClickSFX);
                    _holder.OnClick?.Invoke(meeting, player);
                }
                catch (Exception ex)
                {
                    LightLogger.LogWarning($"[MeetingTargetButton] click {ex.Message}");
                }
            }));
        }

        private void ClearCreated()
        {
            foreach (var go in _created)
                if (go != null) Object.Destroy(go);
            _created.Clear();
        }

        private static PlayerControl GetPlayerById(byte id)
        {
            foreach (var pc in PlayerControl.AllPlayerControls)
                if (pc.PlayerId == id) return pc;
            return null;
        }

        // ---- RoleButtonBase 接口（会议按钮无常驻 UI）----

        protected override void CreateUI()
        {
            // 会议按钮的 UI 由 OnMeetingStart 创建（MeetingHud 已就绪时），此处无需常驻创建
        }

        protected override void DestroyUI()
        {
            ClearCreated();
        }
    }
}