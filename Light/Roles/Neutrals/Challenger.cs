using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Light.Utilities;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Game;
using LightInDark.Roles;
using LightInDark.RPCs;
using LightInDark.UI.Ability;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Light.Roles.Neutrals;

public class Challenger : RoleTemplate
{
    public const string Code = "Challenger";

    public static readonly Challenger MyRole = new();

    static Challenger() => ChallengerState.Initialize();

    public override string CodeName => Code;
    public override LightInDark.Color Color => new(79, 125, 0);
    public override RoleCategory RoleCategory => RoleCategory.Neutral;
    public override NeutralType NeutralType => NeutralType.Evil;
    public override NeutralWinCondition NeutralVictory => NeutralWinCondition.Manual;
    public override string TeamCode => Code;
    public override bool CanKill => true;
    public override AllocationParameters Allocation => new() { MaxCount = 1, Chance = 30 };

    // ★ 适应当前 `RoleTemplate`：`From` abstract ✓ + 配置改成 **`RoleConfig` 对象 + 数组绑定**（AGENTS §13.11 ✓）
    public override string From => null;

    static readonly RoleFloatConfig ChallengeCooldownCfg =
        RoleConfig.Float("challengeCooldown", 30f, 10f, 60f, 5f, ConfigSuffix.Second);
    static readonly RoleIntConfig KillsForWinCfg =
        RoleConfig.Int("killsForWin", 3, 1, 10);
    static readonly RoleFloatConfig KillCooldownCfg =
        RoleConfig.Float("killCooldown", 20f, 5f, 60f, 2.5f, ConfigSuffix.Second);
    static readonly RoleFloatConfig DuelDurationCfg =
        RoleConfig.Float("duelDuration", 30f, 10f, 60f, 5f, ConfigSuffix.Second);

    public override RoleConfigBase[] RoleConfiguration =>
        [ChallengeCooldownCfg, KillsForWinCfg, KillCooldownCfg, DuelDurationCfg];

    public override float KillCooldown => KillCooldownCfg.Value;

    public override RuntimeRoleTemplate CreateRuntime(PlayerControl owner) => new RuntimeInstance(owner, this);

    public class RuntimeInstance : RuntimeRoleTemplate
    {
        private LightInDark.UI.Ability.EffectButton _challenge;
        private LightInDark.UI.Ability.AbilityButton _kill;
        private PlayerTracker _tracker;

        public RuntimeInstance(PlayerControl owner, RoleTemplate template) : base(owner, template) { }

        protected override void OnActivated()
        {
            ChallengerState.ResetAll();
            ChallengerState.ChallengerId = MyPlayer?.Control?.PlayerId ?? byte.MaxValue;
            ChallengerState.Needed = ConfigRegistry.GetInt($"role.{CodeName}.killsForWin");
            ChallengerState.DuelSeconds = ConfigRegistry.GetFloat($"role.{CodeName}.duelDuration");
            ChallengerState.DestroyArena();

            if (!AmOwner) return;

            // ⚠️ 2026-11-01 合并 `/LightInDark (2)` 时删除了 `NormalizeVanillaRole()` 调用 ✗→✓
            //   本项目已**完全接管**原版角色分配（`RoleSelectPatch.Prefix` 直接 return false ✓），
            //   原版技能职业从根源上不会出现 → 该方法已被移除 ✓（见 RuntimeRoleTemplate 的说明）

            _tracker = new PlayerTracker(MyPlayer, RoleUtils.KillDistance(), p => !p.IsDead);
            RoleButtonManager.RegisterTracker(_tracker);

            _challenge = AbilityButtonFactory.CreateEffect(this, new RoleButtonConfig
            {
                Label = "挑战",
                Hotkey = KeyCode.F,
                StartOnCooldown = false,
                Cooldown = ConfigRegistry.GetFloat($"role.{CodeName}.challengeCooldown"),
                Icon = ChallengerState.ChallengeIcon,
                CanShow = () => !ChallengerState.Dueling && RoleUtils.AlivePlayers().Count() > 2,
                CanUse = () => !ChallengerState.Busy && _tracker?.CurrentTarget != null && PlayerControl.LocalPlayer.CanMove,
            }, StartChallenge, ChallengerState.ChallengeDelay);   // ★ 适应当前 4 参重载（effectDuration 必填 ✓）

            _challenge.AllowCancelByReclick = false;
            _challenge.ShowEffectCountdown = false;
            _challenge.ShowEffectRing = false;

            _kill = AbilityButtonFactory.Create(this, new RoleButtonConfig
            {
                IsKillButton = true,
                ArrangedAsKillButton = true,
                LabelType = ButtonLabelType.Impostor,
                Hotkey = KeyCode.Q,
                Label = "击杀",
                StartOnCooldown = false,
                Cooldown = Role.KillCooldown,
                CanShow = () => !ChallengerState.Busy && RoleUtils.AlivePlayers().Count() <= 2,
                CanUse = () => !ChallengerState.Busy && _tracker?.CurrentTarget != null && PlayerControl.LocalPlayer.CanMove,
            }, KillDirect);

            ChallengerState.WinsChanged += RefreshWins;
            RefreshWins();
        }

        protected override void OnInactivated()
        {
            ChallengerState.WinsChanged -= RefreshWins;
            ChallengerState.DestroyArena();
        }

        private void RefreshWins()
        {
            _challenge?.ShowUsesIcon($"{ChallengerState.Wins}/{ChallengerState.Needed}");
        }

        private void StartChallenge()
        {
            var target = _tracker?.CurrentTarget;
            var me = MyPlayer?.Control;
            if (me == null || target?.Control == null) return;
            if (ChallengerState.Busy) return;

            RpcDefinitions.ChallengerSetRival(me.PlayerId, target.Control.PlayerId);
        }

        private void KillDirect()
        {
            var target = _tracker?.CurrentTarget;
            var me = MyPlayer?.Control;
            if (me == null || target?.Control == null) return;
            if (ChallengerState.Busy) return;

            RpcDefinitions.MurderPlayer(me, target.Control, PlayerState.BeKilled);
        }

        public override bool CheckWin()
        {
            var me = MyPlayer?.Control;
            return me != null
                && MyPlayer != null
                && me.PlayerId == ChallengerState.ChallengerId
                && ChallengerState.Wins >= ChallengerState.Needed
                && !MyPlayer.IsDead;
        }
    }
}

internal enum ChallengerPhase
{
    Idle,
    Challenge,
    Duel,
}

internal static class ChallengerState
{
    public const byte NoOne = byte.MaxValue;
    public const float ChallengeDelay = 10f;

    public static byte ChallengerId = NoOne;
    public static byte RivalId = NoOne;
    public static ChallengerPhase Phase = ChallengerPhase.Idle;
    public static byte ChallengerChoice;
    public static byte RivalChoice;
    public static int Wins;
    public static int Needed = 3;
    public static float DuelSeconds = 30f;

    private static float _phaseEnds;
    private static GameObject? _arena;
    private static AssetBundle? _bundle;
    private static bool _bundleTried;
    private static Sprite? _fallbackSprite;
    private static bool _visuals;
    private static readonly Dictionary<byte, Vector3> _savedPositions = new();
    private static readonly List<LightInDark.UI.Ability.AbilityButton> _rps = new();
    private static Sprite _challengeIcon;
    private static Sprite _rockIcon;
    private static Sprite _paperIcon;
    private static Sprite _scissorsIcon;

    public static bool Busy => Phase != ChallengerPhase.Idle;
    public static bool Dueling => Phase == ChallengerPhase.Duel;

    public static event Action? WinsChanged;

    public static Sprite ChallengeIcon => _challengeIcon ??= ResourceHelper.LoadSpriteFromResource(
        "Light.Resources.Roles.ChallengerChallenge.png", 90f);

    private static Sprite RockIcon => _rockIcon ??= ResourceHelper.LoadSpriteFromResource(
        "Light.Resources.Roles.ChallengerRock.png", 90f);

    private static Sprite PaperIcon => _paperIcon ??= ResourceHelper.LoadSpriteFromResource(
        "Light.Resources.Roles.ChallengerPaper.png", 90f);

    private static Sprite ScissorsIcon => _scissorsIcon ??= ResourceHelper.LoadSpriteFromResource(
        "Light.Resources.Roles.ChallengerScissors.png", 90f);

    static ChallengerState()
    {
        RpcDefinitions.OnChallengerSetRival += ApplySetRival;
        RpcDefinitions.OnChallengerSelect += ApplySelect;
        RpcDefinitions.OnChallengerResult += ApplyResult;
    }

    public static void Initialize() { }

    public static void ResetAll()
    {
        Wins = 0;
        Reset();
        WinsChanged?.Invoke();
    }

    public static void Reset()
    {
        EndDuelScene();
        ChallengerId = NoOne;
        RivalId = NoOne;
        Phase = ChallengerPhase.Idle;
        ChallengerChoice = 0;
        RivalChoice = 0;
        _phaseEnds = 0f;
        ReleaseRps();
    }

    private static void ApplySetRival(byte challengerId, byte rivalId)
    {
        if (Phase != ChallengerPhase.Idle) return;

        ChallengerId = challengerId;
        RivalId = rivalId;
        ChallengerChoice = 0;
        RivalChoice = 0;
        Phase = ChallengerPhase.Challenge;
        _phaseEnds = Time.time + ChallengeDelay;

        if (PlayerControl.LocalPlayer != null
            && (PlayerControl.LocalPlayer.PlayerId == challengerId || PlayerControl.LocalPlayer.PlayerId == rivalId))
            Tell("决斗将在 10 秒后开始。");
    }

    private static void ApplySelect(byte playerId, byte choice)
    {
        if (Phase != ChallengerPhase.Duel) return;
        if (playerId == ChallengerId) ChallengerChoice = choice;
        else if (playerId == RivalId) RivalChoice = choice;
    }

    private static void ApplyResult(byte result)
    {
        if (result == 1)
        {
            Wins++;
            WinsChanged?.Invoke();
        }

        EndDuelScene();
        Phase = ChallengerPhase.Idle;
        RivalId = NoOne;
        ChallengerChoice = 0;
        RivalChoice = 0;
        _phaseEnds = 0f;
        ReleaseRps();

        if (result == 1) Tell("挑战者赢得了决斗。");
        else if (result == 2) Tell("挑战者输掉了决斗。");
        else if (result == 3) Tell("决斗双方同归于尽。");
        else Tell("决斗平局。");
    }

    public static void Tick()
    {
        if (ChallengerId != NoOne && _arena == null
            && PlayerControl.LocalPlayer != null && ShipStatus.Instance != null)
            EnsureArena();

        if (Phase == ChallengerPhase.Idle) return;

        var local = PlayerControl.LocalPlayer;
        if (local == null) return;

        if (Phase == ChallengerPhase.Challenge)
        {
            var challenger = Find(ChallengerId);
            var rival = Find(RivalId);
            if (challenger == null || rival == null || challenger.Data.IsDead || rival.Data.IsDead)
            {
                Reset();
                return;
            }

            if (Time.time < _phaseEnds) return;

            Phase = ChallengerPhase.Duel;
            ChallengerChoice = 0;
            RivalChoice = 0;
            _phaseEnds = Time.time + DuelSeconds;
            BeginDuelScene();
            EnsureRps(local);
            if (local.PlayerId == ChallengerId || local.PlayerId == RivalId)
                Tell("决斗开始：选择石头、剪刀或布。");
            return;
        }

        var c = Find(ChallengerId);
        var r = Find(RivalId);
        if (c == null || r == null)
        {
            if (local.PlayerId == ChallengerId) RpcDefinitions.ChallengerResult(0);
            return;
        }

        if (c.Data.IsDead || r.Data.IsDead)
        {
            if (local.PlayerId == ChallengerId) RpcDefinitions.ChallengerResult(0);
            return;
        }

        if (local.PlayerId != ChallengerId) return;

        if (ChallengerChoice != 0 && RivalChoice != 0)
            Resolve(false);
        else if (r.isDummy && ChallengerChoice != 0)
            Resolve(true);
        else if (Time.time >= _phaseEnds)
            Resolve(true);
    }

    private static void Resolve(bool timedOut)
    {
        var challenger = Find(ChallengerId);
        var rival = Find(RivalId);
        if (challenger == null || rival == null) return;

        byte result;
        if (timedOut)
        {
            bool cChose = ChallengerChoice != 0;
            bool rChose = RivalChoice != 0;
            if (cChose && !rChose) result = 1;
            else if (rChose && !cChose) result = 2;
            else result = 3;
        }
        else
        {
            int outcome = Compare(ChallengerChoice, RivalChoice);
            result = outcome > 0 ? (byte)1 : outcome < 0 ? (byte)2 : (byte)0;
        }

        RpcDefinitions.ChallengerResult(result);

        if (result == 1)
            RpcDefinitions.MurderPlayer(challenger, rival, PlayerState.BeKilled);
        else if (result == 2)
            RpcDefinitions.MurderPlayer(rival, challenger, PlayerState.BeKilled);
        else if (result == 3)
        {
            RpcDefinitions.MurderPlayer(challenger, rival, PlayerState.BeKilled);
            RpcDefinitions.MurderPlayer(rival, challenger, PlayerState.BeKilled);
        }
    }

    private static int Compare(byte a, byte b)
    {
        if (a == b) return 0;
        if (a == 1) return b == 3 ? 1 : -1;
        if (a == 2) return b == 1 ? 1 : -1;
        return b == 2 ? 1 : -1;
    }

    private static void EnsureRps(PlayerControl local)
    {
        if (_rps.Count > 0) return;
        if (local.PlayerId != ChallengerId && local.PlayerId != RivalId) return;

        var player = LightInDark.Game.Player.LocalPlayer;
        if (player == null) return;

        _rps.Add(CreateRps(player, local, 1, "石头", KeyCode.Alpha1, RockIcon));
        _rps.Add(CreateRps(player, local, 2, "剪刀", KeyCode.Alpha2, ScissorsIcon));
        _rps.Add(CreateRps(player, local, 3, "布", KeyCode.Alpha3, PaperIcon));
    }

    private static LightInDark.UI.Ability.AbilityButton CreateRps(LightInDark.Game.Player player,
        PlayerControl local, byte choice, string label, KeyCode hotkey, Sprite icon)
    {
        return LightInDark.UI.Ability.AbilityButton.CreateForPlayer(player, new RoleButtonConfig
        {
            Label = label,
            Hotkey = hotkey,
            StartOnCooldown = false,
            Icon = icon,
            CanShow = () => Phase == ChallengerPhase.Duel && MyChoice(local.PlayerId) == 0,
            CanUse = () => Phase == ChallengerPhase.Duel && MyChoice(local.PlayerId) == 0,
        }, () => RpcDefinitions.ChallengerSelect(local.PlayerId, choice));
    }

    private static byte MyChoice(byte playerId)
        => playerId == ChallengerId ? ChallengerChoice : RivalChoice;

    private static void ReleaseRps()
    {
        foreach (var button in _rps) button?.Release();
        _rps.Clear();
    }

    public static void EnsureArena()
    {
        if (_arena != null) return;

        var local = PlayerControl.LocalPlayer;
        if (local == null) return;
        var parent = local.transform.parent;

        try
        {
            if (_bundle == null && !_bundleTried)
            {
                _bundleTried = true;
                _bundle = LoadBundle();
            }

            GameObject? prefab = null;
            if (_bundle != null)
                prefab = _bundle.LoadAsset<GameObject>("challenger_Arena.prefab")
                         ?? _bundle.LoadAsset<GameObject>("challenger_Arena")
                         ?? _bundle.LoadAsset<GameObject>("assets/challenger/challenger_arena.prefab");

            if (prefab != null)
            {
                _arena = parent != null ? Object.Instantiate(prefab, parent) : Object.Instantiate(prefab);
                _arena.name = "ChallengerArena";
                _arena.transform.position = new Vector3(40f, 0f, 1f);
                LightLogger.Log($"[ChallengerArena] spawned prefab={prefab.name} pos={_arena.transform.position}");
                return;
            }

            CreateFallbackArena(parent);
            LightLogger.Log("[ChallengerArena] prefab missing, fallback used");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ChallengerArena.Ensure]", ex);
        }
    }

    private static void CreateFallbackArena(Transform? parent)
    {
        _arena = new GameObject("ChallengerArena");
        if (parent != null) _arena.transform.SetParent(parent, false);
        _arena.transform.position = new Vector3(40f, 0f, 1f);

        var edge = _arena.AddComponent<EdgeCollider2D>();
        edge.points = new[]
        {
            new Vector2(2.81f, 1.6f), new Vector2(2.81f, -1.09f), new Vector2(3.32f, -1.64f),
            new Vector2(10.1f, -1.64f), new Vector2(10.58f, -1.0f), new Vector2(10.58f, 1.62f),
            new Vector2(10.1f, 2.11f), new Vector2(3.32f, 2.11f), new Vector2(2.81f, 1.6f),
        };

        AddBox(_arena, new Vector2(3.5352f, 1.5906f), new Vector2(6.7f, 1.8333f));
        AddBox(_arena, new Vector2(1.9676f, 0.8262f), new Vector2(3.9478f, 2.2199f));
        AddBox(_arena, new Vector2(0.5342f, 0.8248f), new Vector2(8.735f, 2.2149f));
        AddBox(_arena, new Vector2(0.9601f, 0.8047f), new Vector2(8.99f, -0.5552f));
        AddBox(_arena, new Vector2(0.2512f, 2.1943f), new Vector2(6.6917f, -0.6157f));
        AddBox(_arena, new Vector2(7.7744f, 0.1533f), new Vector2(6.697f, 0.455f));

        var child = new GameObject("PokeYanken");
        child.transform.SetParent(_arena.transform, false);
        child.transform.localPosition = new Vector3(4.44f, 0f, 1f);

        var renderer = child.AddComponent<SpriteRenderer>();
        renderer.sprite = FallbackSprite!;
    }

    private static Sprite FallbackSprite => _fallbackSprite ??= ResourceHelper.LoadSpriteFromResource(
        "Light.Resources.Challenger.PokeYanken.png", 96.3293685913086f);

    private static void AddBox(GameObject target, Vector2 size, Vector2 offset)
    {
        var box = target.AddComponent<BoxCollider2D>();
        box.size = size;
        box.offset = offset;
    }

    private static AssetBundle? LoadBundle()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("Light.Resources.Challenger.arena.bundle");
            if (stream == null) return null;

            var dir = Path.Combine(Path.GetTempPath(), "LightInDark");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "challenger_arena.bundle");

            using (var file = File.Create(path))
                stream.CopyTo(file);

            var bundle = AssetBundle.LoadFromFile(path);
            LightLogger.Log($"[ChallengerArena] bundle={(bundle != null)} path={path}");
            return bundle;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ChallengerArena.LoadBundle]", ex);
            return null;
        }
    }

    public static void DestroyArena()
    {
        if (_arena != null) Object.Destroy(_arena);
        _arena = null;
    }

    private static void BeginDuelScene()
    {
        if (_visuals) return;
        _visuals = true;
        DestroyArena();
        EnsureArena();

        foreach (var pc in PlayerControl.AllPlayerControls)
        {
            if (pc == null) continue;

            _savedPositions[pc.PlayerId] = pc.transform.position;
            float z = pc.transform.position.z;
            Vector3 target = pc.PlayerId == ChallengerId
                ? new Vector3(45.26f, 0f, z)
                : pc.PlayerId == RivalId
                    ? new Vector3(48f, 0f, z)
                    : new Vector3(46.7f, 1f, z);

            if (pc.AmOwner) pc.NetTransform.SnapTo(target);
            else pc.transform.position = target;

            try
            {
                pc.RawSetColor(6);
                pc.cosmetics?.SetBodyCosmeticsVisible(false);
                pc.cosmetics?.SetPetVisible(false);
                if (pc.cosmetics?.nameText != null) pc.cosmetics.nameText.gameObject.SetActive(false);
            }
            catch { }
        }

        var camera = Camera.main;
        if (camera != null) camera.GetComponent<FollowerCamera>()?.SnapToTarget();

        ToneSfx.PlayDuelMusic();
    }

    private static void EndDuelScene()
    {
        if (!_visuals) return;
        _visuals = false;

        foreach (var pc in PlayerControl.AllPlayerControls)
        {
            if (pc == null) continue;

            if (_savedPositions.TryGetValue(pc.PlayerId, out var position))
            {
                if (pc.AmOwner) pc.NetTransform.SnapTo(position);
                else pc.transform.position = position;
            }

            try
            {
                if (pc.Data != null) pc.RawSetColor(pc.Data.DefaultOutfit.ColorId);
                pc.cosmetics?.SetBodyCosmeticsVisible(true);
                pc.cosmetics?.SetPetVisible(true);
                if (pc.cosmetics?.nameText != null) pc.cosmetics.nameText.gameObject.SetActive(true);
            }
            catch { }
        }

        _savedPositions.Clear();

        var camera = Camera.main;
        if (camera != null) camera.GetComponent<FollowerCamera>()?.SnapToTarget();

        ToneSfx.StopDuelMusic();
    }

    private static PlayerControl Find(byte playerId)
    {
        if (playerId == NoOne) return null;
        foreach (var pc in PlayerControl.AllPlayerControls)
            if (pc != null && pc.PlayerId == playerId) return pc;
        return null;
    }

    private static void Tell(string text)
    {
        var local = PlayerControl.LocalPlayer;
        var hud = HudManager.Instance;
        if (local == null || hud == null || hud.Chat == null) return;

        var name = local.name;
        local.SetName("System");
        hud.Chat.AddChat(local, text, false);
        local.SetName(name);
    }
}

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
public static class ChallengerTickPatch
{
    public static void Postfix() => ChallengerState.Tick();
}
