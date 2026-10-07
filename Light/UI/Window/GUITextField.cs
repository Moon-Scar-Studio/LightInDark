using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Injection;
using LightInDark.UI.Window;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using LightInDark.Core;
using Color = LightInDark.Color;
using Object = UnityEngine.Object;

namespace Light.UI.Window;

/// <summary>
/// 输入框 Widget 包装：将 GUITextField 接入 GUI 布局体系
/// </summary>
public class TextFieldWidget : AbstractGUIWidget
{
    /// <summary>底层输入框（Instantiate 后可用）</summary>
    public GUITextField? Field { get; private set; }

    private readonly Vector2 _size;
    private readonly string _hint;
    private readonly Action<string>? _onEnter;

    public TextFieldWidget(GUIAlignment alignment, Vector2 size, string hint, Action<string>? onEnter) : base(alignment)
    {
        _size = size;
        _hint = hint;
        _onEnter = onEnter;
    }

    public override GameObject? Instantiate(Size size, out Size actualSize)
    {
        try
        {
            Field = GUITextField.Create(null, _size, _hint, _onEnter);
            actualSize = new Size(_size);
            return Field.GameObject;
        }
        catch (Exception ex)
        {
            actualSize = default; LightLogger.LogError("[GUITextField.Instantiate]", ex); return default;
        }
    }
}

/// <summary>
/// 最小文本输入框（无光标/多行）
/// </summary>
public class GUITextField
{
    private static readonly Dictionary<TextFieldBehaviour, GUITextField> _fields = new();

    /// <summary>当前输入文本</summary>
    public string Text => _behaviour.Value;

    /// <summary>回车确认回调</summary>
    public Action<string>? EnterAction { get; set; }

    /// <summary>根对象</summary>
    public GameObject GameObject { get; }

    /// <summary>
    /// 设置文本。
    /// ⚠️ 原来只有只读的 <see cref="Text"/>，外部想预填内容（比如"作者默认用玩家名"）没办法，
    ///    所以补这个方法 —— 直接写 <c>Value</c> 即可，显示会在 <c>Update</c> 里自动跟上。
    /// </summary>
    public void SetText(string text)
    {
        try { _behaviour.Value = text ?? ""; }
        catch (Exception ex) { LightLogger.LogWarning($"[GUITextField.SetText] {ex.Message}"); }
    }

    /// <summary>设置位置（手动排版时用）。</summary>
    public void SetPosition(Vector3 localPos)
    {
        try { if (GameObject != null) GameObject.transform.localPosition = localPos; }
        catch (Exception ex) { LightLogger.LogWarning($"[GUITextField.SetPosition] {ex.Message}"); }
    }

    /// <summary>主动聚焦 / 取消聚焦。</summary>
    public void SetFocused(bool focused)
    {
        try { _behaviour.Focused = focused; }
        catch (Exception ex) { LightLogger.LogWarning($"[GUITextField.SetFocused] {ex.Message}"); }
    }

    private readonly TextFieldBehaviour _behaviour;

    private GUITextField(GameObject obj, TextFieldBehaviour behaviour)
    {
        try
        {
            GameObject = obj;
            _behaviour = behaviour;
            _fields[behaviour] = this;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GUITextField.GUITextField]", ex);
        }
    }

    /// <summary>
    /// 创建输入框：背景 + 文本 + 点击聚焦
    /// </summary>
    public static GUITextField Create(Transform parent, Vector2 size, string hint = "", Action<string>? onEnter = null)
    {
        try
        {
            var obj = new GameObject("GUITextField");
            obj.layer = LayerExpansion.GetUILayer();
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = Vector3.zero;

            // 背景
            // ⚠️ 2026-10-06 换贴图（用户："这个输入框贴图哪来的，太丑了"）：
            //    原来用 `VanillaAsset.PopUpBackSprite` —— 那是**弹窗的大底板**，
            //    塞进这么小的输入框里又糊又花。
            //    Nebula 用的是 `MetaScreen.GetButtonBackSprite()`（**按钮底图**）+ Tiled，
            //    我们对应的就是 HudUIAssets.ButtonNormal。
            var renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = Light.UI.HudUI.HudUIAssets.ButtonNormal;
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.tileMode = SpriteTileMode.Continuous;
            renderer.size = size;

            // 碰撞体
            var collider = obj.AddComponent<BoxCollider2D>();
            collider.size = size;
            collider.isTrigger = true;

            // 行为组件（聚焦后每帧处理输入）
            var behaviour = obj.AddComponent<TextFieldBehaviour>();
            behaviour.Hint = hint;

            // 文本
            var tmp = Object.Instantiate(VanillaAsset.StandardTextPrefab, obj.transform);
            tmp.transform.localPosition = new Vector3(-size.x * 0.5f + 0.15f, 0f, -0.1f);
            tmp.rectTransform.pivot = new Vector2(0f, 0.5f);
            tmp.rectTransform.sizeDelta = new Vector2(size.x - 0.3f, size.y - 0.06f);
            tmp.fontSize = 1.35f;
            tmp.fontSizeMin = 1f;
            tmp.fontSizeMax = 1.6f;
            tmp.enableAutoSizing = true;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.raycastTarget = false;
            tmp.text = hint;
            tmp.color = UnityEngine.Color.gray;
            tmp.ForceMeshUpdate();
            behaviour.TMP = tmp;

            // 光标：**独立一个 TMP**（Nebula 同款）——
            //   把 "|" 拼进正文的话，光标永远停在最末尾、按方向键也不会动。
            try
            {
                var pipe = Object.Instantiate(VanillaAsset.StandardTextPrefab, obj.transform);
                pipe.transform.localPosition = new Vector3(0f, 0f, -1.5f);
                pipe.rectTransform.pivot = new Vector2(0f, 0.5f);
                pipe.rectTransform.sizeDelta = new Vector2(0.4f, size.y - 0.06f);
                pipe.fontSize = 1.35f;
                pipe.fontSizeMin = 1.35f;
                pipe.fontSizeMax = 1.35f;
                pipe.enableAutoSizing = false;
                pipe.alignment = TextAlignmentOptions.Left;
                pipe.raycastTarget = false;
                pipe.text = "";
                pipe.color = UnityEngine.Color.white;
                pipe.ForceMeshUpdate();
                behaviour.Pipe = pipe;
            }
            catch (Exception ex) { LightLogger.LogWarning($"[GUITextField] 光标 TMP 建立失败：{ex.Message}"); }

            // 点击聚焦
            // ⚠️ 必须走 GetFocus()（会**先把上一个踢掉**）——
            //   直接写 Focused = true 也行（那是个属性，内部就是 GetFocus），
            //   但这里写明确一点，免得以后有人改成别的写法又踩回"两个一起输入"。
            var backColor = new Color(0.16f, 0.16f, 0.16f, 0.85f);
            var hoverColor = new Color(0.3f, 0.3f, 0.3f, 0.9f);
            var button = obj.SetUpButton(true, renderer, backColor, hoverColor, playSound: false);
            button.OnClick.AddListener((UnityAction)(() => behaviour.GetFocus()));

            // 悬浮：不是当前焦点时给个绿边（Nebula 同款提示）
            button.OnMouseOver.AddListener((UnityAction)(() =>
            {
                if (TextFieldBehaviour.ValidField != behaviour) renderer.color = UnityEngine.Color.green;
            }));
            button.OnMouseOut.AddListener((UnityAction)(() => renderer.color = UnityEngine.Color.white));

            var field = new GUITextField(obj, behaviour);
            field.EnterAction = onEnter;
            return field;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GUITextField.Create]", ex); return default;
        }
    }

    internal static void NotifyEnter(TextFieldBehaviour behaviour)
    {
        try
        {
            if (_fields.TryGetValue(behaviour, out var field))
                field.EnterAction?.Invoke(field.Text);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GUITextField.NotifyEnter]", ex);
        }
    }

    internal static void RemoveField(TextFieldBehaviour behaviour)
    {
        try
        {
            _fields.Remove(behaviour);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GUITextField.RemoveField]", ex);
        }
    }
}

/// <summary>
/// 输入框行为组件：聚焦后每帧读取 Input.inputString 更新文本。
///
/// ═══════════════════════════════════════════════════════════════════════
///  【2026-10-06 照搬 Nebula 重写】
///  用户："这俩输入框点一个再点一个还能一起输入的。你照搬 Nebula 的。"
///
///  关键就是 **<see cref="ValidField"/> 这个静态字段** —— 全工程同一时刻
///  只允许一个输入框持有焦点：
/// <code>
///   GetFocus():   if (ValidField) ValidField.LoseFocus();   // 抢焦点前先踢掉上一个
///                 ValidField = this;
///   Update():     if (ValidField != this) { 只显示文本; return; }   // 每帧按它判定
/// </code>
///  原来我用的是实例字段 <c>Focused</c>，**每个输入框各管各的** →
///  点第二个时第一个的 <c>Focused</c> 还是 true → 两边同时吃键盘输入。
///
///  其余照抄 Nebula <c>Nebula\Components\TextInputField.cs</c> 的部分：
///    · 光标 <c>|</c> 用**独立的 TMP**，位置由 <c>textInfo.characterInfo[].bottomRight.x</c> 精确定位
///      （原来是把 "|" 拼进文本里，光标永远在最右边，跟着输入跑不了）；
///    · 左右方向键移动光标；
///    · **Ctrl+V 粘贴**（用 Unity 自带的 <c>GUIUtility.systemCopyBuffer</c>，不引 Nebula 的 ClipboardHelper）；
///    · 失焦时显示灰色提示文字。
/// ═══════════════════════════════════════════════════════════════════════
/// </summary>
public class TextFieldBehaviour : MonoBehaviour
{
    static TextFieldBehaviour()
    {
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<TextFieldBehaviour>();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GUITextField.TextFieldBehaviour]", ex);
        }
    }

    /// <summary>★ 全工程**当前持有焦点的那一个**输入框。null = 没有。</summary>
    public static TextFieldBehaviour? ValidField = null;

    public TextMeshPro? TMP;

    /// <summary>光标用的独立 TMP。</summary>
    public TextMeshPro? Pipe;

    /// <summary>已输入的文本。</summary>
    public string Value = "";

    /// <summary>空的时候显示的灰色提示。</summary>
    public string Hint = "";

    /// <summary>光标位置（在 Value 里的下标）。</summary>
    public int Cursor;

    /// <summary>是否允许输入法（中文）。</summary>
    public bool UseIME = true;

    /// <summary>
    /// ⚠️⚠️⚠️ **原版自己就是这个全局值的所有者**（2026-10-06 读原版源码才看清）：
    ///
    /// <code>
    /// TextBoxTMP.GiveFocus():  Input.imeCompositionMode = 1;   // On  —— 输入框拿到焦点 → 开启输入法
    /// TextBoxTMP.LoseFocus():  Input.imeCompositionMode = 2;   // Off —— 输入框失去焦点 → 关闭输入法
    /// </code>
    ///
    /// **所以 `Off` 是游戏的正常状态** ✓ —— 而本类原来那套
    /// "聚焦时记住旧值 → 失焦时写回旧值"是**错的** ✗✗：
    /// 它会把游戏刚设好的 `On` 覆盖成**上一次游戏设的 `Off`** ✗
    /// → 表现就是用户报的「**中文压根打不进去，跟没打似的**」
    ///   （聊天框聚焦中，全局输入法却是 Off ✗）
    /// 以及「挑字吞」（合成中途状态被改写的时机问题 ✗）。
    ///
    /// ⚠️ 还试过并**全部失败**的写法，别再试：
    ///   · 每帧拉回 `Auto`（"自愈"）        → 输入法压根调不出来 ✗
    ///   · 聊天框打开时归一 `Auto`          → 输入法又死了 ✗
    ///   · 设 `On` 后再"还原"              → 就是上面那个 Off 覆盖 bug ✗
    ///
    /// ✅ **正确做法（与游戏一致）**：聚焦时设 `On` ✓，**失去焦点时什么都不写** ✓
    ///    —— 之后谁再聚焦谁自己设，游戏在每次焦点切换时都会重设，不需要我们"还原" ✓
    /// </summary>
    private static void ApplyImeOnFocus()
    {
        try { Input.imeCompositionMode = IMECompositionMode.On; }   // 与 TextBoxTMP.GiveFocus 完全一致 ✓
        catch { }
    }

    private float _caretTimer;
    private bool _caretOn;
    private float _justFocused;      // 刚聚焦的宽限期，防止"点下去的那一下"立刻被判成失焦

    /// <summary>
    /// 是否持有焦点。
    /// ⚠️ 它**不是**一个独立字段，而是 <see cref="ValidField"/> 的投影 ——
    ///    这样才能保证"全工程只有一个 true"。
    /// </summary>
    public bool Focused
    {
        get => ValidField == this;
        set { if (value) GetFocus(); else LoseFocus(); }
    }

    /// <summary>抢焦点：**先把上一个踢掉**（这就是单一焦点的全部秘密）。</summary>
    public void GetFocus()
    {
        try
        {
            if (ValidField != null && ValidField != this) ValidField.LoseFocus();

            ValidField = this;
            if (UseIME) ApplyImeOnFocus();   // 与 TextBoxTMP.GiveFocus 一致：只设 On，**不再保存/还原** ✓

            Cursor = Value.Length;
            _justFocused = 0.4f;
            _caretTimer = 0.5f;
            _caretOn = true;

            LightLogger.LogDebug($"[TextFieldBehaviour] 聚焦 → {gameObject.name}");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[TextFieldBehaviour.GetFocus]", ex);
        }
    }

    public void LoseFocus()
    {
        try
        {
            // ★★ 2026-10-06 根因（读原版 TextBoxTMP 才看清）：
            //    原来这里（提前返回**之前**）会把全局输入法"还原"成进入编辑前的旧值 ✗
            //    而那个旧值往往是游戏 `TextBoxTMP.LoseFocus()` 设的 **`Off`**（=关闭输入法）✗✗
            //    → 玩家一打开聊天框（原版 GiveFocus 设了 `On` ✓），只要我们的文本框碰巧也走一次
            //      失焦，就把 `On` 覆盖成 `Off` → **中文压根打不进去、跟没打似的** ✓✓
            //    → 现在**什么都不写** ✓：游戏的每次焦点切换都会自己重设（GiveFocus/LoseFocus）✓
            if (ValidField != this) return;

            ValidField = null;
            if (Pipe != null) Pipe.text = "";

            LightLogger.LogDebug($"[TextFieldBehaviour] 失焦 ← {gameObject.name}");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[TextFieldBehaviour.LoseFocus]", ex);
        }
    }

    /// <summary>把一串字符吃进 Value（支持退格 / 回车 / 光标处插入）。</summary>
    public void AcceptText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var result = Value;
        foreach (char c in text)
        {
            if (c == '\b')
            {
                if (Cursor > 0 && result.Length > 0)
                {
                    result = result.Remove(Cursor - 1, 1);
                    Cursor--;
                }
            }
            else if (c == '\n' || c == '\r')
            {
                GUITextField.NotifyEnter(this);
                LoseFocus();
                break;
            }
            else if (c == '\u001b')      // Esc
            {
                LoseFocus();
                break;
            }
            else if (!char.IsControl(c))
            {
                if (Cursor < result.Length) result = result.Insert(Cursor, c.ToString());
                else result += c;
                Cursor++;
            }
        }

        Value = result;
        if (Cursor > Value.Length) Cursor = Value.Length;
        _caretTimer = 0.5f;
        _caretOn = true;
    }

    public void Update()
    {
        try
        {
            bool isFocused = ValidField == this;

            // ---- 没焦点：只负责显示（提示 / 已有文本）----
            if (!isFocused)
            {
                if (Pipe != null) Pipe.text = "";
                ShowText();
                return;
            }

            // ---- 有焦点 ----
            if (_justFocused > 0f) _justFocused -= Time.deltaTime;
            else if (Input.GetMouseButtonDown(0)) LoseFocus();   // 点别处就失焦

            // Ctrl+V 粘贴
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                && Input.GetKeyDown(KeyCode.V))
            {
                try
                {
                    var clip = GUIUtility.systemCopyBuffer;
                    if (!string.IsNullOrEmpty(clip)) AcceptText(clip);
                }
                catch { }
            }

            // 左右方向键
            if (Cursor > 0 && Input.GetKeyDown(KeyCode.LeftArrow))
            {
                Cursor--;
                _caretTimer = 0.5f; _caretOn = true;
            }
            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                Cursor++;
                _caretTimer = 0.5f; _caretOn = true;
            }
            if (Cursor > Value.Length) Cursor = Value.Length;

            // 键盘输入
            AcceptText(Input.inputString);

            // 候选框跟随（中文输入法的候选窗要贴着输入框）
            var camera = Camera.main;
            if (camera != null && TMP != null)
            {
                try
                {
                    var screen = camera.WorldToScreenPoint(TMP.transform.position);
                    Input.compositionCursorPos = new Vector2(screen.x, screen.y);
                }
                catch { }
            }

            // 光标闪烁
            _caretTimer -= Time.deltaTime;
            if (_caretTimer < 0f) { _caretTimer = 0.5f; _caretOn = !_caretOn; }

            ShowText();
            UpdateCaret();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GUITextField.Update]", ex);
        }
    }

    /// <summary>刷新正文（含输入法"组合中"的未提交文本）。</summary>
    private void ShowText()
    {
        if (TMP == null) return;

        string composition = Focused ? Input.compositionString : "";
        bool empty = Value.Length == 0 && composition.Length == 0;

        if (empty && !string.IsNullOrEmpty(Hint))
        {
            TMP.text = Hint;
            TMP.color = UnityEngine.Color.gray;
        }
        else
        {
            // 组合中的文本插在光标处显示（但**不写进 Value**，等输入法提交）
            string head = Cursor > 0 ? Value.Substring(0, Cursor) : "";
            string tail = Cursor < Value.Length ? Value.Substring(Cursor) : "";
            TMP.text = head + composition + tail;
            TMP.color = UnityEngine.Color.white;
        }
        TMP.ForceMeshUpdate();
    }

    /// <summary>
    /// 把光标 <c>|</c> 摆到真实字符位置上。
    ///
    /// ⚠️⚠️ 2026-10-06 修（用户："光标太歪了，我要实际跟着文本走"）：
    ///   <c>textInfo.characterInfo[]</c> 里的坐标是**以正文 TMP 自己的 pivot 为原点**的，
    ///   而正文 TMP 的 pivot 是 <c>(0, 0.5)</c> → **本地 x=0 就是它的左边缘**。
    ///   它自身又摆在 <c>-size.x/2 + 0.15</c> 处。
    ///
    ///   我原来直接把 characterInfo 的 x 当父空间坐标写进 <c>Pipe.localPosition</c> ——
    ///   于是差了"正文 TMP 左边缘"这一整段偏移，**光标永远飘在右边一大截**
    ///   （而且是固定偏移，看起来就像完全没跟着文本走）。
    ///
    ///   正确：父空间 x = **正文物体自身的 localPosition.x** + characterInfo 的 x。
    /// </summary>
    private void UpdateCaret()
    {
        if (Pipe == null || TMP == null) return;

        if (!_caretOn) { Pipe.text = ""; return; }
        Pipe.text = "|";

        try
        {
            var info = TMP.textInfo;
            float local;

            if (Cursor > 0 && info != null && info.characterCount >= Cursor)
                local = info.characterInfo[Cursor - 1].bottomRight.x;
            else if (info != null && info.characterCount > 0)
                local = info.characterInfo[0].bottomLeft.x;
            else
                local = 0f;

            float parentX = TMP.transform.localPosition.x + local;
            Pipe.transform.localPosition = new Vector3(parentX, 0f, -1.5f);
        }
        catch { }
    }

    public void OnDisable() => LoseFocus();

    public void OnDestroy()
    {
        try
        {
            LoseFocus();
            GUITextField.RemoveField(this);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GUITextField.OnDestroy]", ex);
        }
    }
}
