using UnityEngine;
using LightInDark.Roles;
using LightInDark.Game;

namespace LightInDark.Events
{
    /// <summary>
    /// 事件触发入口。主插件和 API 内部通过此类方法触发事件。
    /// 可取消事件返回 bool（true=未被取消，false=被取消）。
    /// </summary>
    public static class EventTriggers
    {
        // ── 大厅（Lobby）──
        public static bool OnLobbyStartGame(int playerCount) { var ev = new LobbyStartGameEvent { PlayerCount = playerCount }; EventSystem.RunEvent(ev); return !ev.IsCanceled; }
        public static void OnLobbyCountdownStart(int playerCount, int duration) => EventSystem.RunEvent(new LobbyCountdownStartEvent { PlayerCount = playerCount, CountdownDuration = duration });
        public static void OnLobbySkipCountdown(int playerCount) => EventSystem.RunEvent(new LobbySkipCountdownEvent { PlayerCount = playerCount });
        public static void OnLobbyCancelStart(int remainingSeconds) => EventSystem.RunEvent(new LobbyCancelStartEvent { RemainingSeconds = remainingSeconds });

        // ── 场景切换 ──
        /// <summary>场景切换后触发（UNITY activeSceneChanged）。</summary>
        public static void OnSceneChanged(string prevScene, string nextScene) => EventSystem.RunEvent(new SceneChangedEvent { PreviousSceneName = prevScene, NextSceneName = nextScene });

        // ── 游戏流程 ──
        public static void OnGameStart(int playerCount) => EventSystem.RunEvent(new GameStartEvent { PlayerCount = playerCount });
        public static void OnGameLoadingStart(int mapId) => EventSystem.RunEvent(new GameLoadingStartEvent { MapId = mapId });
        public static void OnIntroBegin() => EventSystem.RunEvent(new IntroBeginEvent());
        public static void OnIntroEnd() => EventSystem.RunEvent(new IntroEndEvent());
        public static void OnRoleSelectionBegin(int playerCount) => EventSystem.RunEvent(new RoleSelectionBeginEvent { PlayerCount = playerCount });
        public static void OnShipBegin() => EventSystem.RunEvent(new ShipBeginEvent());
        public static void OnPlayersSpawned() => EventSystem.RunEvent(new PlayersSpawnedEvent());
        public static void OnGamePreEnd(bool crewWin, bool impWin, string reason) => EventSystem.RunEvent(new GamePreEndEvent { CrewmatesWin = crewWin, ImpostorsWin = impWin, Reason = reason });
        public static void OnGameEnd(bool crewWin, bool impWin, string reason) => EventSystem.RunEvent(new GameEndEvent { CrewmatesWin = crewWin, ImpostorsWin = impWin, WinReason = reason });
        public static bool OnGameTryEnd(bool crewWin, string reason) { var ev = new GameTryEndEvent { CrewmatesWin = crewWin, Reason = reason }; EventSystem.RunEvent(ev); return !ev.IsCanceled; }
        public static void OnGameUpdate(float dt) => EventSystem.RunEvent(new GameUpdateEvent { DeltaTime = dt });
        public static void OnGameHudUpdate(float dt) => EventSystem.RunEvent(new GameHudUpdateEvent { DeltaTime = dt });
        public static void OnGameLateUpdate(float dt) => EventSystem.RunEvent(new GameLateUpdateEvent { DeltaTime = dt });
        public static void OnEmergencyButtonBroken() => EventSystem.RunEvent(new EmergencyButtonBrokenEvent());
        public static void OnSabotageStart(SystemTypes type) => EventSystem.RunEvent(new SabotageStartEvent { SystemType = type });
        public static void OnSabotageEnd(SystemTypes type) => EventSystem.RunEvent(new SabotageEndEvent { SystemType = type });
        public static void OnHudActiveChange(bool isActive) => EventSystem.RunEvent(new HudActiveChangeEvent { IsActive = isActive });

        // ── 玩家生死 ──
        public static void OnPlayerSuicide(PlayerControl player, string reason, PlayerState state, bool needLog = true) => EventSystem.RunEvent(new PlayerSuicideEvent { Player = player, Reason = reason, NeedLog = needLog, State = state });
        public static void OnPlayerMurder(PlayerControl killer, PlayerControl victim, PlayerState state) => EventSystem.RunEvent(new PlayerMurderEvent { Player = killer, Victim = victim, State = state });
        public static bool OnPlayerTryMurder(PlayerControl killer, PlayerControl victim) { var ev = new PlayerTryMurderEvent { Player = killer, Victim = victim }; EventSystem.RunEvent(ev); return !ev.IsCanceled; }
        public static void OnPlayerDeath(PlayerControl player, DeathReason reason, PlayerControl killer = null, PlayerState state = PlayerState.Dead) => EventSystem.RunEvent(new PlayerDeathEvent { Player = player, Reason = reason, Killer = killer, State = state });
        public static void OnPlayerRevive(PlayerControl player, PlayerControl healer = null) => EventSystem.RunEvent(new PlayerReviveEvent { Player = player, Healer = healer });
        public static void OnPlayerDisconnect(PlayerControl player) => EventSystem.RunEvent(new PlayerDisconnectEvent { Player = player });
        public static PlayerCheckCanKillEvent OnPlayerCheckCanKill(PlayerControl killer, PlayerControl target) { var ev = new PlayerCheckCanKillEvent { Player = killer, Target = target }; EventSystem.RunEvent(ev); return ev; }
        public static bool OnPlayerTryVanillaKill(PlayerControl killer, PlayerControl target) { var ev = new PlayerTryVanillaKillEvent { Player = killer, Target = target }; EventSystem.RunEvent(ev); return !ev.IsCanceled; }
        public static void OnPlayerGuard(PlayerControl player, PlayerControl killer) => EventSystem.RunEvent(new PlayerGuardEvent { Player = player, Murderer = killer });

        // ── 玩家移动 / 交互 ──
        public static void OnPlayerMove(PlayerControl player, Vector2 pos) => EventSystem.RunEvent(new PlayerMoveEvent { Player = player, Position = pos });
        public static void OnPlayerBeginMinigameByConsole(PlayerControl player, Console console) => EventSystem.RunEvent(new PlayerBeginMinigameByConsoleEvent { Player = player, Console = console });
        public static void OnPlayerBeginMinigameByDoor(PlayerControl player, DoorConsole door) => EventSystem.RunEvent(new PlayerBeginMinigameByDoorEvent { Player = player, Door = door });

        // ── 聊天 / 踢出 / 击杀冷却 ──
        public static void OnChatMessage(PlayerControl player, string msg) => EventSystem.RunEvent(new ChatMessageEvent { Player = player, Message = msg });
        public static void OnPlayerKick(PlayerControl player, PlayerControl kicker, string reason = "") => EventSystem.RunEvent(new PlayerKickEvent { Player = player, Kicker = kicker, Reason = reason });
        public static ResetKillCooldownEvent OnResetKillCooldown(PlayerControl player) { var ev = new ResetKillCooldownEvent { Player = player }; EventSystem.RunEvent(ev); return ev; }

        // ── 任务 ──
        public static void OnTaskComplete(PlayerControl player, int completed, int total) => EventSystem.RunEvent(new PlayerTaskCompleteEvent { Player = player, CompletedTasks = completed, TotalTasks = total });
        public static void OnTaskCompleteLocal(PlayerControl player) => EventSystem.RunEvent(new PlayerTaskCompleteLocalEvent { Player = player });
        public static bool OnAllTasksComplete(PlayerControl player) { var ev = new AllTasksCompleteEvent { Player = player }; EventSystem.RunEvent(ev); return !ev.IsCanceled; }
        public static void OnTaskUpdate(PlayerControl player) => EventSystem.RunEvent(new PlayerTaskUpdateEvent { Player = player });
        public static void OnPlayerGetTask(PlayerControl player, NormalPlayerTask task) => EventSystem.RunEvent(new PlayerGetTaskEvent { Player = player, Task = task });
        public static void OnPlayerTaskRemove(PlayerControl player, PlayerTask task) => EventSystem.RunEvent(new PlayerTaskRemoveEvent { Player = player, Task = task });

        // ── 玩家视觉 ──
        public static PlayerAlphaUpdateEvent OnPlayerAlphaUpdate(PlayerControl player, float alpha, float alphaIgnoresWall = 0f) { var ev = new PlayerAlphaUpdateEvent { Player = player, Alpha = alpha, AlphaIgnoresWall = alphaIgnoresWall }; EventSystem.RunEvent(ev); return ev; }
        public static PlayerUpdateVisibilityEvent OnPlayerUpdateVisibility(PlayerControl player, PlayerUpdateVisibilityEvent.VisibilityLevel vis, PlayerUpdateVisibilityEvent.VisibilityLevel last) { var ev = new PlayerUpdateVisibilityEvent { Player = player, Visibility = vis, LastVisibility = last }; EventSystem.RunEvent(ev); return ev; }
        public static PlayerDecorateNameEvent OnPlayerDecorateName(PlayerControl player, string name, bool canSeeAllInfo = false) { var ev = new PlayerDecorateNameEvent { Player = player, Name = name, CanSeeAllInfo = canSeeAllInfo }; EventSystem.RunEvent(ev); return ev; }
        public static PlayerCheckPlayFootSoundEvent OnPlayerCheckPlayFootSound(PlayerControl player) { var ev = new PlayerCheckPlayFootSoundEvent { Player = player }; EventSystem.RunEvent(ev); return ev; }
        public static PlayerUpdateVentStateEvent OnPlayerUpdateVentState(PlayerControl player) { var ev = new PlayerUpdateVentStateEvent { Player = player }; EventSystem.RunEvent(ev); return ev; }

        // ── 角色 ──
        // ── 角色 ──
        public static void OnRoleAssigned(PlayerControl player, Role role, int[] args = null) => EventSystem.RunEvent(new RoleAssignedEvent { Player = player, Role = role, Arguments = args ?? System.Array.Empty<int>() });
        public static bool OnPlayerTryChangeRole(PlayerControl player, Role oldRole, Role newRole) { var ev = new PlayerTryToChangeRoleEvent { Player = player, OldRole = oldRole, NewRole = newRole }; EventSystem.RunEvent(ev); return !ev.IsCanceled; }
        public static void OnPreFixAssignment(IRoleTable table) => EventSystem.RunEvent(new PreFixAssignmentEvent(table));
        public static void OnPlayerRoleSet(PlayerControl player, Role role) => EventSystem.RunEvent(new PlayerRoleSetEvent { Player = player, Role = role });
        public static void OnPlayerRoleSwap(PlayerControl src, PlayerControl dst, Role role, PlayerRoleSwapEvent.SwapType type) => EventSystem.RunEvent(new PlayerRoleSwapEvent { Player = dst, Source = src, Role = role, Type = type });
        public static PlayerCheckWinEvent OnPlayerCheckWin(PlayerControl player, string gameEnd = "") { var ev = new PlayerCheckWinEvent { Player = player, GameEnd = gameEnd }; EventSystem.RunEvent(ev); return ev; }
        public static PlayerCheckExtraWinEvent OnPlayerCheckExtraWin(PlayerControl player, string gameEnd = "") { var ev = new PlayerCheckExtraWinEvent { Player = player, GameEnd = gameEnd }; EventSystem.RunEvent(ev); return ev; }
        public static PlayerBlockWinEvent OnPlayerBlockWin(PlayerControl player, bool isWin, string gameEnd = "") { var ev = new PlayerBlockWinEvent { Player = player, IsWin = isWin, GameEnd = gameEnd }; EventSystem.RunEvent(ev); return ev; }

        // ── 修饰器 ──
        public static void OnModifierAdded(PlayerControl player, Modifiers.Modifier modifier) => EventSystem.RunEvent(new ModifierAddedEvent { Player = player, Modifier = modifier });
        public static void OnModifierRemoved(PlayerControl player, Modifiers.Modifier modifier) => EventSystem.RunEvent(new ModifierRemovedEvent { Player = player, Modifier = modifier });


        // ── 会议 ──
        public static bool OnMeetingTryStart(PlayerControl reporter, NetworkedPlayerInfo body, bool isEmergency) { var ev = new MeetingTryStartEvent { Reporter = reporter, ReportedBody = body, IsEmergencyMeeting = isEmergency }; EventSystem.RunEvent(ev); return !ev.IsCanceled; }
        public static void OnMeetingPreStart(PlayerControl reporter, PlayerControl reported = null) => EventSystem.RunEvent(new MeetingPreStartEvent { Player = reporter, Reported = reported });
        public static void OnReportDeadBody(PlayerControl reporter, PlayerControl reported = null) => EventSystem.RunEvent(new ReportDeadBodyEvent { Player = reporter, Reported = reported });
        public static void OnCalledEmergencyMeeting(PlayerControl reporter) => EventSystem.RunEvent(new CalledEmergencyMeetingEvent { Player = reporter });
        public static CheckCanPushEmergencyButtonEvent OnCheckCanPushEmergencyButton() { var ev = new CheckCanPushEmergencyButtonEvent(); EventSystem.RunEvent(ev); return ev; }
        public static void OnMeetingStart(PlayerControl reporter, NetworkedPlayerInfo body, bool isEmergency) => EventSystem.RunEvent(new MeetingStartEvent { Reporter = reporter, ReportedBody = body, IsEmergencyMeeting = isEmergency });
        public static void OnMeetingDiscussionStart() => EventSystem.RunEvent(new MeetingDiscussionStartEvent());
        public static void OnMeetingVotingStart() => EventSystem.RunEvent(new MeetingVotingStartEvent());
        public static void OnPlayerVote(PlayerControl player, byte votedFor) => EventSystem.RunEvent(new PlayerVoteEvent { Player = player, VotedForPlayerId = votedFor });
        public static void OnPlayerVoteCast(PlayerControl voter, PlayerControl voteFor = null, int vote = 1) => EventSystem.RunEvent(new PlayerVoteCastEvent { Player = voter, VoteFor = voteFor, Vote = vote });
        public static void OnPlayerVoted(PlayerControl player, System.Collections.Generic.IReadOnlyList<PlayerControl> voters) => EventSystem.RunEvent(new PlayerVotedEvent { Player = player, Voters = voters });
        public static bool OnMeetingTryEndVoting(byte exiledPlayerId, bool isTie) { var ev = new MeetingTryEndVotingEvent { ExiledPlayerId = exiledPlayerId, IsTie = isTie }; EventSystem.RunEvent(ev); return !ev.IsCanceled; }
        public static void OnMeetingVoteEnd(MeetingHud.VoterState[] states) => EventSystem.RunEvent(new MeetingVoteEndEvent { VoteStates = states });
        public static void OnMeetingVoteDisclosed(MeetingHud.VoterState[] states) => EventSystem.RunEvent(new MeetingVoteDisclosedEvent { VoteStates = states });
        public static bool OnMeetingTryEnd() { var ev = new MeetingTryEndEvent(); EventSystem.RunEvent(ev); return !ev.IsCanceled; }
        public static void OnMeetingPreEnd() => EventSystem.RunEvent(new MeetingPreEndEvent());
        public static void OnMeetingEnd(byte exiledPlayerId, bool wasTie) => EventSystem.RunEvent(new MeetingEndEvent { ExiledPlayerId = exiledPlayerId, WasTie = wasTie });

        // ── 放逐 ──
        public static bool OnPlayerTryExile(byte playerId, bool isTie) { var ev = new PlayerTryExileEvent { ExilePlayerId = playerId, IsTie = isTie }; EventSystem.RunEvent(ev); return !ev.IsCanceled; }
        public static void OnPlayerExile(PlayerControl exiled, PlayerState state = PlayerState.Exile) => EventSystem.RunEvent(new PlayerExileEvent { Exiled = exiled, State = state });
        public static void OnExileScenePreStart(System.Collections.Generic.IReadOnlyList<PlayerControl> exiled) => EventSystem.RunEvent(new ExileScenePreStartEvent { Exiled = exiled });
        public static void OnExileSceneStart(System.Collections.Generic.IReadOnlyList<PlayerControl> exiled) => EventSystem.RunEvent(new ExileSceneStartEvent { Exiled = exiled });
        public static FixExileTextEvent OnFixExileText(System.Collections.Generic.IReadOnlyList<PlayerControl> exiled) { var ev = new FixExileTextEvent { Exiled = exiled }; EventSystem.RunEvent(ev); return ev; }

        // ── 门 / 地图 / 管道 ──
        public static void OnPlayerOpenDoor(PlayerControl player, OpenableDoor door) => EventSystem.RunEvent(new PlayerOpenDoorEvent { Player = player, Door = door });
        public static bool OnPlayerTryOpenDoorHost(PlayerControl player, OpenableDoor door) { var ev = new PlayerTryOpenDoorHostEvent { Player = player, Door = door }; EventSystem.RunEvent(ev); return !ev.IsCanceled; }
        public static bool OnPlayerTryOpenDoorLocal(PlayerControl player, OpenableDoor door) { var ev = new PlayerTryOpenDoorLocalEvent { Player = player, Door = door }; EventSystem.RunEvent(ev); return !ev.IsCanceled; }
        public static void OnMapOpenNormal() => EventSystem.RunEvent(new MapOpenNormalEvent());
        public static void OnMapOpenAdmin() => EventSystem.RunEvent(new MapOpenAdminEvent());
        public static void OnMapOpenSabotage() => EventSystem.RunEvent(new MapOpenSabotageEvent());
        public static void OnMapClose() => EventSystem.RunEvent(new MapCloseEvent());
        public static void OnVentUsed(PlayerControl player, int ventId) => EventSystem.RunEvent(new VentUsedEvent { Player = player, VentId = ventId });
        public static void OnPlayerVentEnter(PlayerControl player, Vent vent) => EventSystem.RunEvent(new PlayerVentEnterEvent { Player = player, Vent = vent });
        public static void OnPlayerVentExit(PlayerControl player, Vent vent) => EventSystem.RunEvent(new PlayerVentExitEvent { Player = player, Vent = vent });
    }
}
