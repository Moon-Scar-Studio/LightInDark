using System;
using System.Collections.Generic;
using LightInDark.Events;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LightInDark.UI.Ability
{
    public class HudGrid
    {
        private const int MaxColumns = 3;
        private const float EdgeBottom = -2.3f;

        public static HudGrid Instance { get; private set; }

        private readonly List<HudContent>[] _contents = { new List<HudContent>(), new List<HudContent>() };
        private Transform _holder;
        private Transform _staticHolder;
        private int _nextSubPriority;

        public bool IsAlive => _holder != null;

        public static HudGrid Ensure()
        {
            if (Instance != null && Instance.IsAlive) return Instance;

            var hud = HudManager.Instance;
            if (hud == null) return null;
            if (hud.UseButton == null) return null;
            if (hud.UseButton.transform.parent == null) return null;

            var grid = new HudGrid();
            grid.Initialize(hud);
            Instance = grid.IsAlive ? grid : null;
            return Instance;
        }

        private void Initialize(HudManager hud)
        {
            try
            {
                _holder = hud.UseButton.transform.parent;
                _holder.localPosition = Vector3.zero;
                _holder.name = "Buttons";

                var holderObject = _holder.gameObject;
                var arrange = holderObject.GetComponent<GridArrange>();
                if (arrange != null) Object.Destroy(arrange);
                var aspect = holderObject.GetComponent<AspectPosition>();
                if (aspect != null) Object.Destroy(aspect);

                _staticHolder = new GameObject("StaticButtons").transform;
                _staticHolder.SetParent(_holder.parent, false);
                _staticHolder.localPosition = new Vector3(0f, 0f, -90f);

                RegisterVanilla(hud.UseButton, 1000);
                RegisterVanilla(hud.PetButton, 1000);
                RegisterVanilla(hud.SabotageButton, 998);
                RegisterVanilla(hud.ReportButton, 999);
                RegisterVanilla(hud.ImpostorVentButton, 997);
                RegisterVanilla(hud.KillButton, -1, true);
            }
            catch { }
        }

        private void RegisterVanilla(Component button, int priority, bool asKillButton = false)
        {
            if (button == null) return;
            var target = button.gameObject;
            if (target == null) return;

            var content = new HudContent(target);
            content.SetPriority(priority);
            content.ActiveFunc = () => target != null && target.activeSelf;
            if (asKillButton) content.MarkAsKillButtonContent();
            RegisterContent(content, false);
        }

        public void RegisterContent(HudContent content, bool toLeft)
        {
            if (content == null) return;
            try
            {
                _contents[toLeft ? 0 : 1].Add(content);
                content.SetSide(toLeft);
                content.UpdateSubPriority(_nextSubPriority++);

                if (content.IsStaticContent && _staticHolder != null)
                    content.Transform.SetParent(_staticHolder, true);
            }
            catch { }
        }

        public void Tick()
        {
            if (!IsAlive)
            {
                if (Instance == this) Instance = null;
                return;
            }

            try
            {
                Layout();

                foreach (var side in _contents)
                    foreach (var content in side)
                        content.Tick();
            }
            catch { }
        }

        private void Layout()
        {
            // ★ 第 5 批：小 HUD → 整个按钮阵缩放（**只在档位变化时写一次**，避免每帧写 transform，§4.4）
            //   ⚠️ HudGrid 本身不是 MonoBehaviour（每帧由 HudGridTicker 驱动），
            //      所以要缩的是它保存的 `_holder`（= `UseButton.transform.parent` ✓）
            try
            {
                float want = HudContent.SmallHud ? HudContent.SmallHudScale : 1f;
                if (_holder != null && Mathf.Abs(_holder.localScale.x - want) > 0.001f)
                {
                    _holder.localScale = new Vector3(want, want, 1f);
                    LightInDark.Core.LightLogger.Log($"[HudGrid] 小 HUD={(HudContent.SmallHud ? "开" : "关")} → 按钮阵缩放 {want:0.##}");
                }
            }
            catch { }

            for (int side = 0; side < _contents.Length; side++)
            {
                var list = _contents[side];
                list.RemoveAll(c => !c.IsAlive);

                list.Sort((a, b) =>
                {
                    int diff = b.Priority - a.Priority;
                    if (diff == 0) diff = a.SubPriority - b.SubPriority;
                    return diff;
                });

                if (list.Count == 0) continue;

                bool killButtonPlaced = false;
                int row = 0;
                int column = 0;

                bool Visible(HudContent c)
                {
                    if (!c.IsActive) return false;
                    bool inMeeting = MeetingHud.Instance != null || ExileController.Instance != null;
                    if (inMeeting && (!c.IsActiveInHierarchy || !c.IsStaticContent)) return false;
                    return true;
                }

                void ForRest(int from, Action<HudContent> action)
                {
                    for (int i = from; i < list.Count; i++)
                        if (Visible(list[i])) action(list[i]);
                }

                for (int i = 0; i < list.Count; i++)
                {
                    var content = list[i];
                    if (!Visible(content)) continue;

                    if (content.ShouldBeInLastLine)
                    {
                        int remaining = 0;
                        ForRest(i, _ => remaining++);

                        if (remaining <= MaxColumns - column)
                        {
                            ForRest(i, c =>
                            {
                                c.CurrentPos = new Vector2(column, row);
                                column++;
                            });
                        }
                        else
                        {
                            float span = MaxColumns - column - 1;
                            float gap = remaining - 1;

                            // ⚠️ remaining == 1 时 gap == 0 → 下面 `span * index / gap` 会算出 **NaN 位置**
                            //    （按钮直接消失/错位，且不会有任何报错）。
                            //    Nebula 的 HudGrid 对 `numOfLastLineContents == 1` 有单独的兜底分支：直接放当前列。
                            if (gap < 0.5f)
                            {
                                ForRest(i, c => c.CurrentPos = new Vector2(column, row));
                            }
                            else
                            {
                                if (span < 0.1f) span = Mathf.Min(gap, MaxColumns) * 0.1f;
                                int index = 0;
                                ForRest(i, c =>
                                {
                                    c.CurrentPos = new Vector2(column + span * index / gap, row);
                                    index++;
                                });
                            }
                        }
                        break;
                    }

                    if (!killButtonPlaced && content.MarkedAsKillButtonContent)
                    {
                        killButtonPlaced = true;
                        content.CurrentPos = new Vector2(0f, 1f);
                        continue;
                    }

                    content.CurrentPos = new Vector2(column, row);

                    if (column < MaxColumns - 1 && !content.OccupiesLine)
                    {
                        column++;
                    }
                    else
                    {
                        row++;
                        column = 0;
                        if (row == 1 && killButtonPlaced) column = 1;
                    }
                }
            }
        }
    }

    public class HudContent
    {
        private const float FollowSpeed = 5.2f;

        private readonly GameObject _gameObject;
        private int _priority;
        private int _subPriority;
        private bool _lastLine;
        private bool _placed;
        private bool _wasActive;

        public HudContent(GameObject gameObject)
        {
            _gameObject = gameObject;
            CurrentPos = new Vector2(-1f, -1f);
        }

        public Vector2 CurrentPos { get; set; }
        public bool OccupiesLine;
        public bool IsStaticContent;
        public bool MarkedAsKillButtonContent { get; private set; }
        public bool IsLeftSide { get; private set; }
        public Func<bool> ActiveFunc;

        public bool IsAlive => _gameObject != null;
        public Transform Transform => _gameObject != null ? _gameObject.transform : null;

        public int Priority => (OccupiesLine ? 20000 : MarkedAsKillButtonContent ? 10000 : 0) + _priority;
        public int SubPriority => _subPriority;

        public bool ShouldBeInLastLine
        {
            get => !OccupiesLine && _lastLine;
            set => _lastLine = value;
        }

        public bool IsActive => ActiveFunc != null
            ? ActiveFunc()
            : _gameObject != null && _gameObject.activeSelf;

        public bool IsActiveInHierarchy => _gameObject != null && _gameObject.activeInHierarchy;

        // =====================================================================
        //  第 5 批：**按钮自动排版优化**（用户 2026-10-06 点名的"自己的特色"）
        //  照抄 Nebula 的两项（`NebulaPluginNova\Modules\HudGrid.cs`）：
        //    ① 小 HUD：整个按钮阵缩到 0.72 倍 + 换一套边距公式（Nebula :55-66 / :210-226）
        //    ② 排列档位：把按钮整体/左侧一列抬高 0.85（Nebula :234-235）
        //  ⚠️ 两者都由**配置项**驱动，**默认关闭/0 档 = 与改动前逐字节一致** ✓（纯增量）
        // =====================================================================

        /// <summary>小 HUD 时的整体缩放（与 Nebula 一致 = 0.72）。</summary>
        internal const float SmallHudScale = 0.72f;

        /// <summary>排列档位带来的抬高量（与 Nebula 一致 = 0.85）。</summary>
        internal const float ArrangementRaiseY = 0.85f;

        /// <summary>是否启用"小 HUD"布局（配置 `lid.hud.smallGrid`，默认 false ✓）。</summary>
        internal static bool SmallHud
        {
            get
            {
                try { return LightInDark.Configuration.ConfigRegistry.Get("lid.hud.smallGrid")?.GetBool() ?? false; }
                catch { return false; }
            }
        }

        /// <summary>按钮排列档位（配置 `lid.hud.buttonArrangement`：0=默认 1=只抬高左侧 2=全部抬高 ✓）。</summary>
        internal static int Arrangement
        {
            get
            {
                try
                {
                    int v = LightInDark.Configuration.ConfigRegistry.Get("lid.hud.buttonArrangement")?.GetInt() ?? 0;
                    if (v < 0) return 0;
                    return v > 2 ? 2 : v;
                }
                catch { return 0; }
            }
        }

        /// <summary>
        /// 右边距（Nebula `HudContent.EdgeX` 的等价物）。
        /// ★ 第 5 批：加了**小 HUD** 分支 —— 用户开了"小 HUD"时整个按钮阵会缩到 0.72 倍，
        ///   边距也必须跟着换公式，否则缩完之后会飘到屏幕外（Nebula `HudGrid.cs:210-226`）✓
        /// </summary>
        private static float EdgeRight => SmallHud
            ? (3.0f / SmallHudScale) * Screen.width / Screen.height - 0.67f
            : 3f * Screen.width / Screen.height - 0.8f;

        /// <summary>下边距（Nebula `HudContent.EdgeY` 的等价物，普通 HUD = -2.3f ✓）。</summary>
        private static float EdgeBottom => SmallHud
            ? -(3.0f / SmallHudScale - 0.65f)
            : -2.3f;

        public Vector3 TargetLocalPos => new Vector3(
            (EdgeRight - CurrentPos.x) * (IsLeftSide ? -1f : 1f),
            EdgeBottom + CurrentPos.y + ArrangementOffsetY,
            CurrentPos.x * 0.05f);

        /// <summary>按钮排列档位带来的整体抬高量（0 = 不动 ✓ 默认）。</summary>
        private float ArrangementOffsetY
        {
            get
            {
                if (Arrangement == 0) return 0f;
                if (MeetingHud.Instance != null) return 0f;        // 会议里不抬高（原版布局优先）✓
                if (Arrangement == 2) return ArrangementRaiseY;    // 全部抬高
                return IsLeftSide ? ArrangementRaiseY : 0f;        // 只抬高左侧那一列
            }
        }

        public HudContent SetPriority(int priority)
        {
            _priority = priority;
            return this;
        }

        public HudContent UpdateSubPriority(int subPriority)
        {
            _subPriority = subPriority;
            return this;
        }

        public HudContent MarkAsKillButtonContent(bool mark = true)
        {
            MarkedAsKillButtonContent = mark;
            return this;
        }

        public HudContent SetSide(bool toLeft)
        {
            IsLeftSide = toLeft;
            return this;
        }

        public void Tick()
        {
            var transform = Transform;
            if (transform == null) return;

            if (!_gameObject.activeSelf)
            {
                _wasActive = false;
                _placed = false;
                CurrentPos = new Vector2(-1f, -1f);
                return;
            }

            if (!_wasActive)
            {
                _wasActive = true;
                _placed = false;
                CurrentPos = new Vector2(-1f, -1f);
            }

            if (CurrentPos.x < 0f) return;

            if (!_placed)
            {
                transform.localPosition = TargetLocalPos;
                _placed = true;
            }
            else
            {
                transform.localPosition += (TargetLocalPos - transform.localPosition) * (Time.deltaTime * FollowSpeed);
            }
        }
    }

    internal static class HudGridTicker
    {
        public static void OnHudUpdate(GameHudUpdateEvent ev)
        {
            HudGrid.Instance?.Tick();
        }
    }
}
