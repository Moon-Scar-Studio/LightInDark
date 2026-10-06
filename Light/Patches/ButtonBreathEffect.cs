using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LightInDark.Core;
using System;

namespace Light.Patches;

/// <summary>
/// FS-style static button effect manager (no injected MonoBehaviour).
/// Driven by MainMenuManager.LateUpdate postfix; self-stops when MainUI is gone.
/// </summary>
public static class ButtonBreathEffect
{
    private class ButtonState
    {
        public Vector3 BasePos;
        public Vector3 BaseScale;
        public float Phase;
        public int Id;
        public float HoverLerp;
        public float ClickLerp;
        public bool IsHovering;
        public SpriteRenderer? InactiveSr;
        public Color BaseColor;
        public PassiveButton? Button;
        public bool IsMainButton;
    }

    private static readonly Dictionary<GameObject, ButtonState> _states = new();
    private static int _nextId;
    private static readonly List<GameObject> _deadKeys = new();

    private const float FollowRadius = 0.15f;
    private const float FloatAmount = 0.006f;
    private const float BreathAmount = 0.04f;
    private const float ButtonScale = 0.88f;

    private static readonly Dictionary<GameObject, Vector3> _originalScales = new();

    public static void Init()
    {
        try
        {
            _states.Clear();
            _nextId = 0;
            // LeftPanel 解构后按钮被 reparent，LeftPanel 本身 SetActive(false)
            // 所以从 mainMenuUI 下搜索所有 PassiveButton
            var mainMenuUI = GameObject.Find("MainUI");
            if (mainMenuUI == null)
            {
                // fallback：尝试 LeftPanel（首次布局前）
                var leftPanel = GameObject.Find("LeftPanel");
                if (leftPanel == null) return;
                foreach (var btn in leftPanel.GetComponentsInChildren<PassiveButton>(true))
                {
                    if (btn == null) continue;
                    Register(btn);
                }
                return;
            }

            foreach (var btn in mainMenuUI.GetComponentsInChildren<PassiveButton>(true))
            {
                if (btn == null) continue;
                var name = btn.gameObject.name;
                if (name == "LightLogo") continue;
                Register(btn);
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ButtonBreathEffect.Init]", ex);
        }
    }

    public static void Reload()
    {
        _states.Clear();
        Init();
    }

    /// <summary>
    /// 把某个按钮的基准位置刷新为它【当前】的位置。
    /// 移动过按钮之后必须调用 —— 否则 Update() 每帧都会用旧的 BasePos 把 localPosition 写回去,
    /// 表现就是"移了等于没移"(改完下一帧又被摆回原地)。
    /// </summary>
    public static void RebasePosition(GameObject go)
    {
        try
        {
            if (go != null && _states.TryGetValue(go, out var state) && state != null)
                state.BasePos = go.transform.localPosition;
        }
        catch { }
    }

    private static void Register(PassiveButton pb)
    {
        try
        {
            var go = pb.gameObject;
            if (_states.ContainsKey(go)) return;

            if (!_originalScales.ContainsKey(go))
            {
                // ⚠️ 这个静态字典**只增不减**（2026-10-06 审查 #20）：按钮 GameObject 被销毁后
                //    条目仍然留着（键是假 null），跨局/跨场景一直累积。
                //    这里在新增时顺手清一遍已销毁的键（Unity 假 null 用 `==` 判，AGENTS §4.6.1）。
                if (_originalScales.Count > 64) PruneDeadEntries();
                _originalScales[go] = go.transform.localScale;
            }

            var state = new ButtonState
            {
                BasePos = go.transform.localPosition,
                BaseScale = _originalScales[go] * ButtonScale,
                Phase = UnityEngine.Random.value * Mathf.PI * 2f,
                Id = _nextId++,
                Button = pb,
                IsMainButton = !go.name.StartsWith("CustomButton")
            };
            go.transform.localScale = state.BaseScale;

            if (pb.inactiveSprites != null)
            {
                state.InactiveSr = pb.inactiveSprites.GetComponent<SpriteRenderer>();
                if (state.InactiveSr != null)
                    state.BaseColor = state.InactiveSr.color;

                // 高光（Shine）整体关掉：原本"悬浮时淡入、并跟着鼠标跑"，观感不好，已按要求移除
                var shine = pb.inactiveSprites.transform.FindChild("Shine");
                if (shine != null) shine.gameObject.SetActive(false);
            }

            // 兜底：确保悬停/点击事件非空，避免原版 PassiveButtonManager 触发 NRE
            if (pb.OnMouseOver == null) pb.OnMouseOver = new UnityEngine.Events.UnityEvent();
            if (pb.OnMouseOut == null) pb.OnMouseOut = new UnityEngine.Events.UnityEvent();
            pb.OnMouseOver?.AddListener((UnityEngine.Events.UnityAction)(() => state.IsHovering = true));
            pb.OnMouseOut?.AddListener((UnityEngine.Events.UnityAction)(() => state.IsHovering = false));
            pb.OnClick?.AddListener((UnityEngine.Events.UnityAction)(() => state.ClickLerp = 1f));

            _states[go] = state;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ButtonBreathEffect.Register]", ex);
        }
    }

    /// <summary>清掉已销毁对象的缓存条目（`== null` 走 Unity 的假 null 判定，见 AGENTS §4.6.1）。</summary>
    private static void PruneDeadEntries()
    {
        try
        {
            var dead = new List<GameObject>();
            foreach (var k in _originalScales.Keys)
                if (k == null) dead.Add(k);
            foreach (var k in dead) _originalScales.Remove(k);

            if (dead.Count > 0)
                LightLogger.Log($"[ButtonBreathEffect] 清理 {dead.Count} 个已销毁按钮的缩放缓存（剩 {_originalScales.Count}）");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[ButtonBreathEffect.PruneDeadEntries] {ex.Message}");
        }
    }

    /// <summary>缓存"创建房间界面"（CreateGameOptions）。⚠️ 别每帧全场景扫描，很贵。</summary>
    private static CreateGameOptions? _cachedCreateGameScreen;

    /// <summary>创建房间界面（CreateGameOptions，主菜单 MainUI 下的 CreateGameScreen）是否正在显示。</summary>
    private static bool IsCreateGameScreenActive()
    {
        try
        {
            // ⚠️ 原来每帧 `FindObjectOfType<CreateGameOptions>()`（遍历整个场景）—— 主菜单里每秒几十次。
            //    缓存它，只在缓存被销毁（Unity 假 null，见 AGENTS §4.6.1）时才重查一次。
            if (_cachedCreateGameScreen != null) return _cachedCreateGameScreen.gameObject.activeInHierarchy;

            _cachedCreateGameScreen = UnityEngine.Object.FindObjectOfType<CreateGameOptions>();
            return _cachedCreateGameScreen != null && _cachedCreateGameScreen.gameObject.activeInHierarchy;
        }
        catch { return false; }
    }

    public static void Update()
    {
        try
        {
            if (GameObject.Find("MainUI") == null) return;

            // 创建房间界面（MainUI-CreateGameScreen）显示时，去掉呼吸灯/点击按压/悬停反馈，避免与房间创建面板错位
            if (IsCreateGameScreenActive()) return;

            _deadKeys.Clear();
            foreach (var kvp in _states)
            {
                var obj = kvp.Key;
                var s = kvp.Value;

                try
                {
                    if (obj == null || !obj.activeSelf) continue;
                    if (s.Button == null || s.Button.gameObject == null)
                    {
                        _deadKeys.Add(obj);
                        continue;
                    }

                    s.Phase += Time.deltaTime;

                    float breath = BreathAmount + BreathAmount * 0.5f * Mathf.Sin(s.Phase * 0.8f + s.Id);
                    float floatY = FloatAmount * Mathf.Sin(s.Phase * 1.2f + s.Id * 0.7f);

                    float targetHover = s.IsHovering ? 1f : 0f;
                    s.HoverLerp = Mathf.Lerp(s.HoverLerp, targetHover, Time.deltaTime * 10f);
                    s.ClickLerp = Mathf.Lerp(s.ClickLerp, 0f, Time.deltaTime * 6f);

                    float hoverScale = 1f + (s.IsMainButton ? 0.08f : 0.04f) * s.HoverLerp;
                    float clickScale = 1f - (s.IsMainButton ? 0.06f : 0.03f) * s.ClickLerp;
                    float totalScale = hoverScale * clickScale;

                    Vector3 pos = s.BasePos + new Vector3(0f, floatY, 0f);

                    if (s.IsMainButton && s.IsHovering && Camera.main != null)
                    {
                        Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                        Vector3 local = obj.transform.parent.InverseTransformPoint(mouseWorld);
                        Vector3 delta = local - s.BasePos;
                        float dist = delta.magnitude;
                        if (dist > FollowRadius)
                            delta = delta.normalized * FollowRadius;
                        pos += delta * 0.3f;
                    }

                    float breathScale = s.IsMainButton ? breath : breath * 0.5f;
                    obj.transform.localScale = s.BaseScale * (totalScale + breathScale);
                    obj.transform.localPosition = pos;

                    if (s.InactiveSr != null)
                    {
                        float pulse = Mathf.Sin(s.Phase * 1.5f + s.Id);
                        float brightness = 1f + 0.2f * pulse;
                        float hoverBoost = 0.35f * s.HoverLerp;
                        float goldR = Mathf.Clamp01(s.BaseColor.r * brightness + hoverBoost + 0.15f * s.HoverLerp);
                        float goldG = Mathf.Clamp01(s.BaseColor.g * brightness + hoverBoost + 0.12f * s.HoverLerp);
                        float goldB = Mathf.Clamp01(s.BaseColor.b * brightness + hoverBoost);
                        s.InactiveSr.color = new Color(goldR, goldG, goldB, s.BaseColor.a);
                    }
                }
                catch
                {
                    _deadKeys.Add(obj);
                }
            }

            foreach (var dead in _deadKeys)
                _states.Remove(dead);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ButtonBreathEffect.Update]", ex);
        }
    }
}
