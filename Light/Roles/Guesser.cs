using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Light.Utilities;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Events;
using LightInDark.Game;
using LightInDark.Roles;
using LightInDark.RPCs;
using LightInDark.UI.Ability;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Light.Roles;

public abstract class GuesserRole : RoleTemplate
{
    public virtual UnityEngine.Color FlashColor => RoleCategory == RoleCategory.Impostor
        ? UnityEngine.Color.red
        : UnityEngine.Color.yellow;

    // ── 职业专属配置（照 NOS：配置对象本身就是配置项，`.Value` 直接取值 ✓）──
    // ⚠️ 用**实例字段**而不是 static：NiceGuesser / EvilGuesser 是两个不同的职业实例，
    //    各自需要各自的注册绑定（static 会让第二个职业复用第一个的 ConfigItem → 键/前缀错位 ✗）
    public readonly RoleIntConfig ShotsCfg = RoleConfig.Int("shots", 2, 1, 15);
    public readonly RoleIntConfig SafeGuessesCfg = RoleConfig.Int("safeGuesses", 0, 0, 10);
    public readonly RoleBoolConfig MultiShotCfg = RoleConfig.Bool("multipleShotsPerMeeting", false);

    /// <summary>数组绑定 ✓（键自动补 `role.&lt;CodeName&gt;.` 前缀）</summary>
    public override RoleConfigBase[] RoleConfiguration => [ShotsCfg, SafeGuessesCfg, MultiShotCfg];

    public override RuntimeRoleTemplate CreateRuntime(PlayerControl owner) => new GuesserRuntime(owner, this);
}

internal sealed class GuesserRuntime : RuntimeRoleTemplate
{
    private int _shots;
    private int _safe;
    private bool _multi;
    private bool _used;
    private PlayerControl? _target;
    private GuesserPanel? _panel;

    internal GuesserRuntime(PlayerControl owner, RoleTemplate role) : base(owner, role) { }

    protected override void OnActivated()
    {
        if (!AmOwner) return;

        // （不再需要 NormalizeVanillaRole ✓ —— 分配机已被完全接管，见 RuntimeRoleTemplate ✓）

        _shots = (Role as GuesserRole)?.ShotsCfg.Value ?? 2;            // ★ 配置对象取值 ✓
        _safe = (Role as GuesserRole)?.SafeGuessesCfg.Value ?? 0;
        _multi = (Role as GuesserRole)?.MultiShotCfg.Value ?? false;

        var config = new MeetingTargetButtonConfig
        {
            Icon = ResourceHelper.LoadSpriteFromResource("Light.Resources.Roles.GuesserButton.png", 115f),
            CanAddTarget = CanTarget,
            CanUseTarget = CanTarget,
            CanShow = CanGuess,
            OnHover = () => Light.UI.Window.VanillaAsset.PlayUiHover(),
            OnClickFeedback = () => Light.Utilities.ToneSfx.PlayGunload(),
        };
        AbilityButtonFactory.CreateMeetingTarget(this, config, (meeting, target) => Open(meeting, target));
    }

    protected override void OnInactivated() => Close();

    protected override void OnMeetingStarted(MeetingStartEvent e)
    {
        _used = false;
        Close();
    }

    protected override void OnMeetingEnded(MeetingEndEvent e) => Close();

    /// <summary>
    /// 这个玩家**要不要给赌怪建按钮**（false = 连按钮都没有 ✓）
    /// <para>⚠️ 2026-10-25 用户要求："不要赌怪猜中时检测 CanBeGuessed，就这个为 false 时按钮都不创建" ✓</para>
    /// <para>对照 NOS（`Nebula\Roles\ComplexRoles\Guesser.cs:172`）：
    /// NOS 也是**建 UI 时**就 `if (!role.IsGuessableRole) continue;` —— **不给它建按钮** ✓，
    /// 而不是"点了再判"✗。我们的"赌杀免疫"是**玩家级**（`Player.CanBeGuessed`），所以过滤放在这里 ✓</para>
    /// </summary>
    private bool CanTarget(PlayerControl p)
    {
        var me = MyPlayer?.Control;
        if (p == null || me == null || p.PlayerId == me.PlayerId) return false;
        if (p.Data == null || p.Data.IsDead || p.Data.Disconnected) return false;

        // ★ 不能被赌杀的玩家 → **不建按钮**（不是"建了再拒绝" ✗）
        var target = LightInDark.Game.Player.Get(p.PlayerId);
        if (target != null && !target.CanBeGuessed) return false;

        return true;
    }

    private bool CanGuess()
    {
        if (_shots <= 0) return false;
        if (_used && !_multi) return false;

        var meeting = MeetingHud.Instance;
        if (meeting == null) return false;
        if (meeting.state != MeetingHud.MeetingStates.Voted && meeting.state != MeetingHud.MeetingStates.NotVoted) return false;

        return MyPlayer != null && !MyPlayer.IsDead;
    }

    private void Open(MeetingHud meeting, PlayerControl target)
    {
        if (meeting == null || target == null || !CanGuess()) return;

        Close();
        _target = target;
        _panel = new GuesserPanel(meeting, Role, Shoot);
        _panel.Open();
    }

    private void Close()
    {
        _panel?.Close();
        _panel = null;
    }

    private void Shoot(RoleTemplate guessed)
    {
        var target = _target;
        Close();
        if (target == null) return;

        var me = MyPlayer?.Control;
        if (me == null) return;

        var template = LightInDark.Game.Player.Get(target.PlayerId)?.Role?.Role;
        string actual;
        if (template != null) actual = template.CodeName;
        else
        {
            var data = target.Data;
            actual = data != null && data.Role != null && data.Role.IsImpostor ? "impostor" : "crewmate";
        }

        if (string.Equals(actual, guessed.CodeName, StringComparison.OrdinalIgnoreCase))
        {
            // ⚠️ 2026-10-25 用户要求：**这里不再检测 `CanBeGuessed`** ✗
            //   免疫的玩家在 `CanTarget` 里就**根本不会建按钮** ✓（点击入口都不存在 ✓）
            _shots--;
            _used = true;
            Light.Utilities.ToneSfx.PlayGunfire();
            RpcDefinitions.GuesserShoot(me.PlayerId, target.PlayerId, guessed.CodeName);
            Tell("Guesser.hit");
            return;
        }

        if (_safe > 0)
        {
            _safe--;
            Flash();
            Tell("Guesser.safe");
            return;
        }

        _shots--;
        _used = true;
        Light.Utilities.ToneSfx.PlayGunfire();
        RpcDefinitions.GuesserShoot(me.PlayerId, me.PlayerId, guessed.CodeName);
        Tell("Guesser.miss");
    }

    private void Flash()
    {
        var color = Role is GuesserRole guesser ? guesser.FlashColor : UnityEngine.Color.red;
        Dispatcher.Instance?.StartCoroutine(CoFlash(color).WrapToIl2Cpp());
    }

    private static IEnumerator CoFlash(UnityEngine.Color color)
    {
        var hud = HudManager.Instance;
        if (hud == null || hud.FullScreen == null) yield break;

        var flash = Object.Instantiate(hud.FullScreen, hud.transform);
        flash.name = "GuesserSafeFlash";
        flash.gameObject.SetActive(true);

        const float duration = 0.5f;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            if (flash == null) yield break;
            float alpha = Mathf.Lerp(0.75f, 0f, t / duration);
            flash.color = new UnityEngine.Color(color.r, color.g, color.b, alpha);
            yield return null;
        }

        if (flash != null) Object.Destroy(flash.gameObject);
    }

    private static void Tell(string key)
    {
        var local = PlayerControl.LocalPlayer;
        if (local == null) return;
        RpcDefinitions.ShowSystemMessage(local.PlayerId, LightInDark.Language.Language.GetStringOrKey(key, key));
    }
}

internal sealed class GuesserPanel
{
    private const int PerPage = 40;

    private static readonly Vector3 ButtonPos = new(-3.47f, 1.5f, -200f);
    private static readonly RoleCategory[] Tabs = { RoleCategory.Crewmate, RoleCategory.Impostor, RoleCategory.Neutral };

    private static Sprite _plate;
    private static Sprite _plateTab;

    private readonly MeetingHud _meeting;
    private readonly RoleTemplate _self;
    private readonly Action<RoleTemplate> _pick;

    private Transform? _root;
    private TextMeshPro? _labelTemplate;
    private PlayerVoteArea[]? _areas;

    private readonly Dictionary<RoleCategory, List<Transform>> _roles = new();
    private readonly Dictionary<RoleCategory, SpriteRenderer> _tabs = new();
    private readonly List<SpriteRenderer> _pages = new();
    private readonly List<Transform> _buttons = new();

    private RoleCategory _tab = RoleCategory.Crewmate;
    private int _page = 1;
    private Transform? _selected;

    internal GuesserPanel(MeetingHud meeting, RoleTemplate self, Action<RoleTemplate> pick)
    {
        _meeting = meeting;
        _self = self;
        _pick = pick;
    }

    private static Sprite Plate => _plate ??= ResourceHelper.LoadSpriteFromResource("Light.Resources.Roles.GuessPlate.png", 115f);
    private static Sprite PlateTab => _plateTab ??= ResourceHelper.LoadSpriteFromResource("Light.Resources.Roles.GuessPlateKPD.png", 115f);

    internal void Open()
    {
        if (_meeting == null || _meeting.playerStates == null || _meeting.playerStates.Length == 0) return;

        var area = _meeting.playerStates[0];
        if (area == null) return;

        var buttonTemplate = area.transform.FindChild("votePlayerBase");
        var maskTemplate = area.transform.FindChild("MaskArea");
        var cancelTemplate = area.Buttons != null ? area.Buttons.transform.Find("CancelButton") : null;
        if (buttonTemplate == null || maskTemplate == null || cancelTemplate == null) return;

        _labelTemplate = Object.Instantiate(area.NameText);
        _labelTemplate.gameObject.SetActive(true);
        _labelTemplate.text = "";
        _labelTemplate.raycastTarget = false;

        var phone = _meeting.transform.FindChild("MeetingContents/PhoneUI");
        if (phone == null)
        {
            var found = GameObject.Find("PhoneUI");
            phone = found != null ? found.transform : null;
        }

        if (phone != null)
            _root = Object.Instantiate(phone, _meeting.transform);
        else
        {
            _root = new GameObject("GuesserUI").transform;
            _root.SetParent(_meeting.transform, false);
        }

        _root.localPosition = new Vector3(0f, 0f, -200f);

        _areas = _meeting.playerStates;
        foreach (var voteArea in _areas)
            if (voteArea != null) voteArea.gameObject.SetActive(false);

        BuildExit(buttonTemplate, maskTemplate, cancelTemplate);
        BuildTabs(buttonTemplate, maskTemplate);
        BuildPages(buttonTemplate, maskTemplate);
        BuildRoles(buttonTemplate, maskTemplate);

        _root.localScale *= 0.75f;
        SelectTab(RoleCategory.Crewmate);
    }

    internal void Close()
    {
        if (_labelTemplate != null) Object.Destroy(_labelTemplate.gameObject);
        if (_areas != null)
            foreach (var area in _areas)
                if (area != null) area.gameObject.SetActive(true);
        if (_root != null) Object.Destroy(_root.gameObject);

        _labelTemplate = null;
        _areas = null;
        _root = null;
    }

    private void BuildExit(Transform buttonTemplate, Transform maskTemplate, Transform cancelTemplate)
    {
        var parent = NewParent();
        var button = Object.Instantiate(buttonTemplate, parent);
        HideHighlight(button);
        var mask = Object.Instantiate(maskTemplate, parent);
        mask.localScale = new Vector3(2.88f, 0.8f, 1f);
        mask.localPosition = new Vector3(0f, 0f, 1f);

        var renderer = button.GetComponent<SpriteRenderer>();
        renderer.sprite = cancelTemplate.GetComponent<SpriteRenderer>().sprite;

        parent.localPosition = new Vector3(3.88f, 2.12f, -200f);
        parent.localScale = new Vector3(0.22f, 0.9f, 1f);
        parent.SetAsFirstSibling();

        var passive = button.GetComponent<PassiveButton>();
        passive.OnClick = new Button.ButtonClickedEvent();
        passive.OnClick.AddListener((UnityAction)Close);
    }

    private void BuildTabs(Transform buttonTemplate, Transform maskTemplate)
    {
        int i = 0;
        foreach (var category in Tabs)
        {
            var parent = NewParent();
            var button = Object.Instantiate(buttonTemplate, parent);
            HideHighlight(button);
            Object.Instantiate(maskTemplate, parent);

            var renderer = button.GetComponent<SpriteRenderer>();
            renderer.sprite = PlateTab;
            _tabs[category] = renderer;

            var label = NewLabel(button);
            label.text = LightInDark.Language.Language.GetStringOrKey($"role.category.{category.ToString().ToLowerInvariant()}", category.ToString());
            label.color = TabColor(category);

            parent.localPosition = new Vector3(-3.10f + i * 1.47f, 2.225f, -200f);
            parent.localScale = new Vector3(0.53f, 0.53f, 1f);

            var passive = button.GetComponent<PassiveButton>();
            passive.OnClick = new Button.ButtonClickedEvent();
            var picked = category;
            passive.OnClick.AddListener((UnityAction)(() => SelectTab(picked)));

            i++;
        }
    }

    private void BuildPages(Transform buttonTemplate, Transform maskTemplate)
    {
        CreatePage(false, buttonTemplate, maskTemplate);
        CreatePage(true, buttonTemplate, maskTemplate);
    }

    private void CreatePage(bool next, Transform buttonTemplate, Transform maskTemplate)
    {
        var parent = NewParent();
        var button = Object.Instantiate(buttonTemplate, parent);
        HideHighlight(button);
        Object.Instantiate(maskTemplate, parent);

        var label = NewLabel(button);
        label.text = LightInDark.Language.Language.GetStringOrKey(next ? "Guesser.next" : "Guesser.prev", next ? "下一页" : "上一页");
        label.color = Color.white;

        var renderer = button.GetComponent<SpriteRenderer>();
        renderer.sprite = PlateTab;
        _pages.Add(renderer);

        parent.localPosition = next ? new Vector3(3.535f, -2.2f, -200f) : new Vector3(-3.475f, -2.2f, -200f);
        parent.localScale = new Vector3(0.55f, 0.55f, 1f);

        var passive = button.GetComponent<PassiveButton>();
        passive.OnClick = new Button.ButtonClickedEvent();
        passive.OnClick.AddListener((UnityAction)(() =>
        {
            _page += next ? 1 : -1;
            Refresh();
        }));
    }

    private void BuildRoles(Transform buttonTemplate, Transform maskTemplate)
    {
        foreach (var category in Tabs) _roles[category] = new List<Transform>();

        foreach (var role in RoleRegistry.AllRoles.OrderBy(r => r.Id))
        {
            if (role == null || role.IsGhostRole) continue;
            if (ReferenceEquals(role, _self)) continue;
            if (role.CanBeAssigned && !role.IsSpawnable()) continue;
            if (!_roles.TryGetValue(role.RoleCategory, out var list)) continue;

            list.Add(CreateRole(role, list.Count, buttonTemplate, maskTemplate));
        }
    }

    private Transform CreateRole(RoleTemplate role, int index, Transform buttonTemplate, Transform maskTemplate)
    {
        var parent = NewParent();
        var button = Object.Instantiate(buttonTemplate, parent);
        HideHighlight(button);
        Object.Instantiate(maskTemplate, parent);

        var label = NewLabel(button);
        label.text = role.Name;
        label.color = LightInDark.ColorHelper.ToUnityColor(role.Color);

        button.GetComponent<SpriteRenderer>().sprite = Plate;

        int row = index / 5, col = index % 5;
        parent.localPosition = ButtonPos + new Vector3(1.75f * col, -0.45f * row, 0f);
        parent.localScale = new Vector3(0.55f, 0.55f, 1f);

        var passive = button.GetComponent<PassiveButton>();
        passive.OnClick = new Button.ButtonClickedEvent();
        passive.OnClick.AddListener((UnityAction)(() => Click(button, role)));

        _buttons.Add(button);
        return button;
    }

    private void Click(Transform button, RoleTemplate role)
    {
        if (_selected != button)
        {
            _selected = button;
            foreach (var other in _buttons)
            {
                var renderer = other.GetComponent<SpriteRenderer>();
                if (renderer == null) continue;
                renderer.color = other == _selected ? LightInDark.ColorHelper.ToUnityColor(_self.Color) : Color.white;
            }
            return;
        }

        Close();
        _pick?.Invoke(role);
    }

    private void SelectTab(RoleCategory category)
    {
        _tab = category;
        _page = 1;
        Refresh();
    }

    private void Refresh()
    {
        int count = _roles.TryGetValue(_tab, out var current) ? current.Count : 0;
        int pages = Math.Max(1, (count + PerPage - 1) / PerPage);
        _page = Math.Clamp(_page, 1, pages);

        foreach (var pair in _roles)
        {
            int index = 0;
            foreach (var button in pair.Value)
            {
                if (button == null) continue;
                index++;
                bool show = pair.Key == _tab && index > (_page - 1) * PerPage && index <= _page * PerPage;
                button.parent.gameObject.SetActive(show);
            }
        }

        foreach (var pair in _tabs)
            pair.Value.color = pair.Key == _tab ? new Color(0f, 0f, 0f, 1f) : new Color(0f, 0f, 0f, 0.25f);

        if (_pages.Count < 2) return;
        _pages[0].color = new Color(1f, 1f, 1f, _page <= 1 ? 0.1f : 1f);
        _pages[1].color = new Color(1f, 1f, 1f, _page >= pages ? 0.1f : 1f);
    }

    private Transform NewParent()
    {
        var parent = new GameObject().transform;
        parent.SetParent(_root);
        return parent;
    }

    private TextMeshPro NewLabel(Transform button)
    {
        var label = Object.Instantiate(_labelTemplate, button)!;
        label.alignment = TextAlignmentOptions.Center;
        label.transform.localPosition = new Vector3(0f, 0f, label.transform.localPosition.z);
        label.transform.localScale *= 1.6f;
        label.autoSizeTextContainer = true;
        return label;
    }

    private static void HideHighlight(Transform button)
    {
        var highlight = button.FindChild("ControllerHighlight");
        if (highlight != null) highlight.gameObject.SetActive(false);
    }

    private static Color TabColor(RoleCategory category) => category switch
    {
        RoleCategory.Impostor => new Color32(255, 25, 25, byte.MaxValue),
        RoleCategory.Neutral => new Color32(127, 140, 141, byte.MaxValue),
        _ => new Color32(140, 255, 255, byte.MaxValue),
    };
}
