using System;
using Light.UI.Window;
using Light.Utilities;
using LightInDark.Configuration;
using LightInDark.Events;
using LightInDark.Game;
using LightInDark.Roles;
using LightInDark.RPCs;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace Light.Roles.Crewmates;

public class Mayor : RoleTemplate
{
    public const string Code = "Mayor";

    public static readonly Mayor MyRole = new();

    public override string CodeName => Code;
    public override LightInDark.Color Color => new(30, 96, 85);
    public override RoleCategory RoleCategory => RoleCategory.Crewmate;
    public override AllocationParameters Allocation => new() { MaxCount = 1, Chance = 30 };

    // ★ 适应当前 `RoleTemplate`（`From` / `RoleConfiguration` 现在是 abstract ✓）
    public override string From => null;
    public override RoleConfigBase[] RoleConfiguration => Array.Empty<RoleConfigBase>();

    public override RuntimeRoleTemplate CreateRuntime(PlayerControl owner) => new RuntimeInstance(owner, this);

    public class RuntimeInstance : RuntimeRoleTemplate
    {
        internal static RuntimeInstance? LocalMayor;

        private bool _storeMode;
        private GameObject? _panel;
        private TextMeshPro? _countText;
        private string _skipLabel = "";
        private bool _skipLabelSaved;
        private int _currentVote = 1;

        private static Sprite? _leftSprite;
        private static Sprite? _rightSprite;

        public RuntimeInstance(PlayerControl owner, RoleTemplate template) : base(owner, template) { }

        protected override void OnActivated()
        {
            if (!AmOwner) return;

            LocalMayor = this;
            MeetingVoteApi.ConfirmHandler += HandleConfirm;
            MeetingVoteApi.Update += HandleRightClick;
            MeetingVoteApi.FrameUpdate += UpdatePanel;
        }

        protected override void OnInactivated()
        {
            if (ReferenceEquals(LocalMayor, this)) LocalMayor = null;
            MeetingVoteApi.ConfirmHandler -= HandleConfirm;
            MeetingVoteApi.Update -= HandleRightClick;
            MeetingVoteApi.FrameUpdate -= UpdatePanel;
            DestroyPanel();
        }

        protected override void OnMeetingStarted(MeetingStartEvent e)
        {
            _storeMode = false;
            _skipLabelSaved = false;
            _currentVote = 1;
            BuildPanel();
        }

        protected override void OnMeetingEnded(MeetingEndEvent e)
        {
            _storeMode = false;
            DestroyPanel();
        }

        private bool HandleConfirm(byte target)
        {
            var meeting = MeetingHud.Instance;
            var me = MyPlayer?.Control;
            if (meeting == null || me == null || me.Data == null || me.Data.IsDead) return false;
            if (meeting.state != MeetingHud.MeetingStates.NotVoted) return false;
            if (meeting.DidVote(me.PlayerId)) return false;

            byte skipId = (byte)meeting.SkipVoteButton.PlayerId;

            if (_storeMode)
            {
                if (target == skipId) StoreVote(meeting, me);
                return true;
            }

            if (target == skipId) return false;

            int stored = MeetingVoteApi.GetStoredVotes(me.PlayerId);
            int total = stored + 1;

            int weight = Mathf.Clamp(_currentVote, 1, total);
            if (weight <= 1) return false;

            PrepareLocalVote(meeting);
            HidePanel();
            RpcDefinitions.MeetingSetStored(me.PlayerId, Mathf.Max(0, stored - (weight - 1)));
            RpcDefinitions.MeetingCustomVote(me.PlayerId, target, weight);
            return true;
        }

        private void HandleRightClick()
        {
            if (!PointerOverSkip()) return;

            var meeting = MeetingHud.Instance;
            var me = MyPlayer?.Control;
            if (meeting == null || me == null || me.Data == null || me.Data.IsDead) return;
            if (meeting.state != MeetingHud.MeetingStates.NotVoted) return;

            _storeMode = !_storeMode;
            SetSkipLabel(_storeMode ? "存票" : "");
        }

        private void StoreVote(MeetingHud meeting, PlayerControl me)
        {
            int add = MeetingVoteApi.GetStoreTimes(me.PlayerId) + 1;
            PrepareLocalVote(meeting);
            HidePanel();
            RpcDefinitions.MeetingStoreVote(me.PlayerId, 253, add);
        }

        private void SetSkipLabel(string text)
        {
            var skip = MeetingHud.Instance?.SkipVoteButton;
            if (skip == null) return;

            var labels = skip.GetComponentsInChildren<TextMeshPro>(true);
            if (labels == null || labels.Length == 0) return;

            var label = labels[0];
            if (!_skipLabelSaved)
            {
                _skipLabel = label.text;
                _skipLabelSaved = true;
            }

            label.text = string.IsNullOrEmpty(text) ? _skipLabel : text;
        }

        private void UpdatePanel()
        {
            var meeting = MeetingHud.Instance;
            var me = MyPlayer?.Control;
            bool notVoted = meeting != null && me != null && me.Data != null && !me.Data.IsDead
                && meeting.state == MeetingHud.MeetingStates.NotVoted;

            if (_panel == null)
            {
                if (notVoted) BuildPanel();
                return;
            }

            if (_panel.activeSelf != notVoted) _panel.SetActive(notVoted);
        }

        private void BuildPanel()
        {
            DestroyPanel();

            var meeting = MeetingHud.Instance;
            var skip = meeting?.SkipVoteButton;
            var me = MyPlayer?.Control;
            if (meeting == null || skip == null || me == null || me.Data == null || me.Data.IsDead) return;

            _panel = new GameObject("MayorVotePanel");
            _panel.layer = skip.gameObject.layer;
            _panel.transform.SetParent(skip.transform.parent, false);
            _panel.transform.localPosition = skip.transform.localPosition;

            _countText = Object.Instantiate(meeting.TitleText, _panel.transform);
            _countText.gameObject.SetActive(true);
            var translator = _countText.GetComponent<TextTranslatorTMP>();
            if (translator != null) translator.enabled = false;
            _countText.alignment = TextAlignmentOptions.Center;
            _countText.transform.localPosition = new Vector3(2.59f, 0f);
            _countText.color = UnityEngine.Color.white;
            _countText.transform.localScale *= 0.8f;

            UpdateCount();

            if (TotalVotes() <= 1) return;

            CreateArrow(_panel.transform, new Vector3(2f, 0f), LeftSprite, () =>
            {
                _currentVote--;
                UpdateCount();
            });

            CreateArrow(_panel.transform, new Vector3(3.6f, 0f), RightSprite, () =>
            {
                _currentVote++;
                UpdateCount();
            });
        }

        private int TotalVotes()
        {
            var me = MyPlayer?.Control;
            return me == null ? 1 : MeetingVoteApi.GetStoredVotes(me.PlayerId) + 1;
        }

        private void UpdateCount()
        {
            _currentVote = Mathf.Clamp(_currentVote, 1, TotalVotes());
            if (_countText != null) _countText.text = $"{_currentVote}/{TotalVotes()}";
        }

        private static void CreateArrow(Transform parent, Vector3 position, Sprite sprite, Action onClick)
        {
            var go = new GameObject("MayorArrow");
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;

            var collider = go.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(0.6f, 0.6f);

            var button = go.SetUpButton(true);
            button.OnClick.AddListener((UnityAction)(() => onClick()));
            button.OnMouseOver.AddListener((UnityAction)(() => renderer.color = UnityEngine.Color.gray));
            button.OnMouseOut.AddListener((UnityAction)(() => renderer.color = UnityEngine.Color.white));
        }

        private static bool PointerOverSkip()
        {
            var skip = MeetingHud.Instance?.SkipVoteButton;
            if (skip == null) return false;

            var hud = HudManager.Instance;
            var camera = hud != null && hud.UICamera != null ? hud.UICamera : Camera.main;
            if (camera == null) return false;

            var screen = camera.WorldToScreenPoint(skip.transform.position);
            return Vector2.Distance((Vector2)Input.mousePosition, (Vector2)screen) <= 90f;
        }

        private void HidePanel()
        {
            if (_panel != null) _panel.SetActive(false);
        }

        private void DestroyPanel()
        {
            if (_panel != null) Object.Destroy(_panel);
            _panel = null;
            _countText = null;
        }

        private static Sprite LeftSprite => _leftSprite ??= ResourceHelper.LoadSpriteFromResource(
            "Light.Resources.Roles.MayorLeft.png", 100f);

        private static Sprite RightSprite => _rightSprite ??= ResourceHelper.LoadSpriteFromResource(
            "Light.Resources.Roles.MayorRight.png", 100f);

        private static void PrepareLocalVote(MeetingHud meeting)
        {
            foreach (var area in meeting.playerStates)
            {
                if (area == null) continue;
                area.ClearButtons();
                area.VoteComplete = true;
            }

            var skip = meeting.SkipVoteButton;
            if (skip != null)
            {
                skip.ClearButtons();
                skip.VoteComplete = true;
                skip.gameObject.SetActive(false);
            }

            meeting.state = MeetingHud.MeetingStates.Voted;
        }

    }
}
