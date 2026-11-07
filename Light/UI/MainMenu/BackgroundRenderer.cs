using System;
using System.Collections.Generic;
using System.Linq;
using LightInDark.Core;
using LightInDark.UI.Window;          // LayerExpansion
using UnityEngine;
using UnityEngine.Video;

namespace Light.UI.MainMenu;

/// <summary>
/// 主界面自定义背景（图片 / 视频）。
///
/// =====================================================================
///  【为什么重写：原实现的两个毛病】
///
/// 旧实现（<c>ImageGalleryPanel.EnsureBgObject</c>）是：
///     var go = new GameObject("LightBackground");
///     go.transform.position = new Vector3(0, 0, 520f);   // 世界坐标写死
///     UnityEngine.Object.DontDestroyOnLoad(go);          // 跨场景不销毁
///
/// → **外泄**：DontDestroyOnLoad 让它活到所有场景，进游戏/进大厅都还在，
///   只能靠 MainMenuPatch.LateUpdate 里"进别的场景就 SetActive(false) + 缩到 0.0001"
///   这种补丁去藏，一旦某个场景没覆盖到就露出来。
/// → **莫名其妙消失**：位置是写死的世界坐标 z=520。主菜单相机一旦
///   换位置/换朝向（重进主菜单、分辨率变化、MatchMaking 场景），
///   它就跑出视锥了 → 什么都看不见，但对象还在、日志也不报错。
///
/// =====================================================================
///  【现在的做法（照抄参考实现 MainMenuBackground 的正交思路）】
///
///   · **不用 DontDestroyOnLoad**，对象挂在主菜单场景里 → 场景卸载自动销毁，
///     从根上不可能外泄；
///   · **每帧跟随相机**，放在远裁剪面前一点点 → 永远在视野里，
///     也永远在所有 UI 后面，不存在"跑出视锥"；
///   · 用**网格**而不是 SpriteRenderer，因为视频要靠
///     <c>VideoRenderMode.MaterialOverride</c> 往材质的 _MainTex 上写。
///
/// ⚠️ 关于 Unity 的"假 null"（AGENTS.md §4.6）：这里每一步都重新取组件、
///    每次都判空，场景切换把对象销毁后会自动走重建分支。
/// </summary>
public static class BackgroundRenderer
{
    private const string RootName = "LightMainMenuBackground";
    private const int MediaSortingOrder = -30000;   // 远低于任何 UI
    private const float VideoPrepareTimeout = 20f;

    // ---- 运行时对象 ----
    private static GameObject? _root;
    private static MeshRenderer? _mediaRenderer;
    private static MeshFilter? _mediaFilter;
    private static MeshRenderer? _backdropRenderer;
    private static MeshFilter? _backdropFilter;
    private static Material? _mediaMaterial;
    private static VideoPlayer? _video;

    private static bool _mediaReady;
    /// <summary>视频是否已经真正开播（Prepare 完成之后才置 true）。</summary>
    private static bool _videoStarted;

    /// <summary>
    /// 开播后音量渐入的帧数。
    /// ⚠️ 这是"开头炸音"的最终修复：Play() 之后头几帧解码器可能还在吐脏样本，
    ///    所以那几帧音量保持 0（听不见），再渐变推上去（避免增益突变爆音）。
    /// </summary>
    private const int AudioFadeFrames = 8;
    private static int _fadeLeft;
    private static float _fadeTarget = 1f;
    private static float _mediaW = 1f, _mediaH = 1f;
    private static float _createdAt;
    private static int _generation;

    private static int _lastLayer = -1;
    private static (float vw, float vh, float mw, float mh, int fit, float dim, bool ready)? _lastLayout;

    private static float _lastVolume = -1f;
    private static bool _vanillaMusicMuted;

    private static string _currentPath = "";
    private static string _lastFailReason = "";

    // 被我们藏起来的原版背景（销毁时恢复）
    private static readonly List<(SpriteRenderer sr, bool wasEnabled)> _hiddenRenderers = new();
    private static readonly List<(GameObject go, bool wasActive)> _hiddenObjects = new();

    // 贴图缓存
    private static Texture2D? _cachedTex;
    private static string? _cachedTexKey;

    /// <summary>上一次失败原因（界面用来提示）。空 = 没失败。</summary>
    public static string LastFailReason => _lastFailReason;

    /// <summary>当前生效的素材路径（诊断用）。</summary>
    public static string CurrentPath => _currentPath;

    /// <summary>视频是否已准备好（界面用来显示分辨率）。</summary>
    public static bool IsReady => _mediaReady;

    /// <summary>背景根对象是否活着（诊断用；注意必须用 <c>!= null</c> 走 Unity 的假 null 判定）。</summary>
    public static bool HasRoot
    {
        get { try { return _root != null; } catch { return false; } }
    }

    // =====================================================================
    //  一次性诊断（2026-10-05 用户报「本地 / 搜索游戏没有背景图」时加的）
    //
    //  【为什么必须先加这个】
    //  Tick() 里现在有 6 条 early-return，任何一条命中都表现为"什么都没发生"，
    //  而且**全都不打日志** —— 光看现象分不清是"没被调用 / 场景判断失败 /
    //  被失败原因卡住 / 找不到相机 / 建好了但被原版背景盖住"。
    //
    //  【节流规则】每个场景最多 8 条 + 相邻两条至少隔 60 帧（≈1 秒）
    //  → 跑一次最多十几行，不刷屏，但足够看出卡在哪一步。
    // =====================================================================

    private const int DiagMaxPerScene = 8;
    private const int DiagFrameGap = 60;

    private static string _diagScene = "";
    private static int _diagCount;
    private static int _diagLastFrame = -100000;
    /// <summary>本场景是否已经打过"相机全家桶"（每场景只打一次）。</summary>
    private static bool _diagCamerasLogged;

    /// <summary>当前场景名（读不到返回 "?"，绝不抛）。</summary>
    private static string SafeSceneName()
    {
        try { return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; }
        catch { return "?"; }
    }

    /// <summary>
    /// 在 Tick() 的**每条 early-return 之前**调一行：把"我为什么什么都没做"的原因打全。
    /// 本方法自己在最外层 try/catch 里 —— 它在每帧路径上，绝不能抛。
    /// </summary>
    private static void DiagReturn(string stage)
    {
        try
        {
            string scene = SafeSceneName();

            bool sceneChanged = scene != _diagScene;
            if (sceneChanged)
            {
                _diagScene = scene;
                _diagCount = 0;
                _diagLastFrame = -100000;
                _diagCamerasLogged = false;
            }

            // ok / 异常 这类"正常路径"每场景只留一条就够
            if (!sceneChanged && _diagCount >= DiagMaxPerScene) return;
            if (!sceneChanged && Time.frameCount - _diagLastFrame < DiagFrameGap) return;

            _diagLastFrame = Time.frameCount;
            _diagCount++;

            // ⚠️ 全部用 `!= null`（Unity 假 null）；**一个 `??` 都不许出现**（AGENTS.md §4.6.1）
            var root = _root;
            bool rootAlive = root != null;
            int layer = root != null
                ? root.layer
                : (_lastLayer >= 0 ? _lastLayer : LayerExpansion.GetUILayer());

            int scanCount = -1;
            try { scanCount = BackgroundStore.Scan().Count; } catch { }

            bool shouldHave = false;
            try { shouldHave = ShouldHaveBackground(); } catch { }

            var cam = FindCamera(layer);
            string camInfo = cam == null
                ? "null"
                : $"{cam.name}(depth={cam.depth},mask={cam.cullingMask},ortho={cam.orthographic}," +
                  $"orthoSize={cam.orthographicSize:F1},near={cam.nearClipPlane:F1},far={cam.farClipPlane:F1}," +
                  $"enabled={cam.enabled},pos={cam.transform.position})";

            LightLogger.Log($"[BgTick#{_diagCount}/{DiagMaxPerScene}] stage={stage} " +
                            $"scene='{scene}' inMenuScene={IsInMenuScene()} " +
                            $"rootAlive={rootAlive} rootLayer={layer} lastLayer={_lastLayer} " +
                            $"selected='{BackgroundStore.Selected}' shouldHaveBg={shouldHave} scan={scanCount} " +
                            $"fail='{_lastFailReason}' cam={camInfo} " +
                            $"allCameras={Camera.allCameras.Length} ready={_mediaReady} " +
                            $"media={_mediaW:F0}x{_mediaH:F0} path='{System.IO.Path.GetFileName(_currentPath)}'");

            // 每个场景打一次"相机全家桶" —— 回答"哪台相机画哪个 layer"（AGENTS.md §4.3）
            if (!_diagCamerasLogged)
            {
                _diagCamerasLogged = true;
                try
                {
                    foreach (var c in Camera.allCameras)
                    {
                        if (c == null) continue;
                        LightLogger.Log($"[BgTick.cam] scene='{scene}' name={c.name} depth={c.depth} " +
                                        $"mask={c.cullingMask} enabled={c.enabled} ortho={c.orthographic} " +
                                        $"orthoSize={c.orthographicSize:F1} near={c.nearClipPlane:F1} " +
                                        $"far={c.farClipPlane:F1} pos={c.transform.position} " +
                                        $"fwd={c.transform.forward} tag={c.tag} " +
                                        $"drawsLayer{layer}={(c.cullingMask & (1 << layer)) != 0}");
                    }

                    // 场景所有根物体：回答"这个场景里到底有什么"（MatchMaking / FindAGame 里
                    // 没有 MainMenuManager，得知道替代的主控是谁）
                    var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < roots.Length && i < 40; i++)
                    {
                        if (roots[i] == null) continue;
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(roots[i].name);
                    }
                    LightLogger.Log($"[BgTick.roots] scene='{scene}' 根物体({roots.Length})：{sb}");
                }
                catch (Exception ex)
                {
                    LightLogger.LogWarning($"[BgTick.cam] 相机/根物体枚举失败：{ex.Message}");
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// 场景变化时清掉跨场景的状态。
    ///
    /// ⚠️⚠️ 这是"永久不再重试"的修复：<see cref="Tick"/> 里那条
    /// <code>if (_root == null && _lastFailReason == "" &amp;&amp; ShouldHaveBackground()) Reapply();</code>
    /// 一旦 <c>_lastFailReason</c> 非空（例如视频 20 秒没准备好、图片解码失败、
    /// 找不到相机），**在同一个场景里就再也不会重试**，而且 `_lastFailReason` 是
    /// static、跨场景不清 → 换到 MatchMaking / FindAGame 也照样卡死。
    /// 现在按场景名变化清一次，让新场景有机会重新建。
    /// </summary>
    private static void ResetSceneStateIfChanged()
    {
        try
        {
            string scene = SafeSceneName();
            if (scene == _lastSceneName) return;

            LightLogger.Log($"[BackgroundRenderer] 场景切换 '{_lastSceneName}' → '{scene}'：" +
                            $"清掉跨场景状态（rootAlive={_root != null}，旧 fail='{_lastFailReason}'）");
            _lastSceneName = scene;
            _lastFailReason = "";
            _diagScene = "";            // 让新场景的节流计数从 0 开始
            _diagCount = 0;
            _diagLastFrame = -100000;
            _diagCamerasLogged = false;
        }
        catch { }
    }

    /// <summary>上一次 Tick 看到的场景名（<see cref="ResetSceneStateIfChanged"/> 用）。</summary>
    private static string _lastSceneName = "";

    /// <summary>原版背景"补做隐藏"的剩余次数（见 <see cref="Tick"/> 里那段注释；有界，防无限重试）。</summary>
    private static int _hideRetryLeft;
    private static int _lastHideRetryFrame = -100000;

    // =====================================================================
    //  对外入口
    // =====================================================================

    /// <summary>主菜单出现时调用：按设置重建背景。</summary>
    public static void OnMainMenuStart(int uiLayer)
    {
        _lastLayer = uiLayer;
        _lastFailReason = "";      // 显式重进主菜单 = 允许重试（见 ResetSceneStateIfChanged 的注释）
        Reapply();
    }

    /// <summary>
    /// 设置变化后重新应用。
    ///
    /// ⚠️⚠️ **同一个素材时走"原地刷新"，绝不拆建。**
    ///   用户反馈"应用东西会有 1 秒黑屏 + 音频卡顿" —— 那就是 Teardown+Build 的代价：
    ///   VideoPlayer 被销毁 → 重建 → 必须重新 prepare（约 1 秒），
    ///   这段期间只能看到黑底（Backdrop），音频也是断的。
    ///   上一版的"续播 seek"只能保住**进度**，保不住**不断**。
    ///
    ///   原地刷新只重跑"隐藏原版背景 / 船员显示 / 布局 / 音量"这几件事，
    ///   视频一帧都不断、声音一秒都不卡。
    /// </summary>
    /// <param name="rerollRandom">
    /// true = 强制重新随机（用户重新点了「随机」那一行时才需要）；
    /// false = 随机模式下沿用当前这张，避免"改个船员显示把背景也换了"。
    /// </param>
    public static void Reapply(bool rerollRandom = false)
    {
        var entry = IsInMenuScene() ? BackgroundStore.Resolve() : null;

        // 随机模式下，没要求重roll 就沿用当前这张（素材还在的话）
        if (!rerollRandom && entry != null && BackgroundStore.Selected == BackgroundStore.SelectionRandom
            && !string.IsNullOrEmpty(_currentPath) && _currentPath != entry.FullPath
            && System.IO.File.Exists(_currentPath))
        {
            entry = new BackgroundEntry
            {
                FullPath = _currentPath,
                FileName = System.IO.Path.GetFileName(_currentPath),
                IsVideo = BackgroundStore.IsVideoExt(System.IO.Path.GetExtension(_currentPath)),
            };
        }

        string newPath = entry?.FullPath ?? "";

        // ---- 同一个素材 → 原地刷新（视频不中断） ----
        if (_root != null && !string.IsNullOrEmpty(newPath) && newPath == _currentPath)
        {
            RefreshInPlace();
            _lastFailReason = "";
            return;
        }

        // ---- 换素材 / 之前没建过 → 老老实实重建 ----
        Teardown();

        if (!IsInMenuScene()) return;

        if (entry == null)
        {
            _lastFailReason = "";
            _currentPath = "";
            return;     // 用原版背景
        }

        try
        {
            // ⚠️ 只有**真的建成功**才清失败原因。
            //    以前这里是无条件 `_lastFailReason = "";`，于是 Build 里设的
            //    "找不到主界面相机。" / "图片解码失败" 立刻被自己抹掉 →
            //    Tick 下一帧又看到 fail=="" → 又 Reapply() → 每帧重试、
            //    而且界面上（LastFailReason）永远看不到真正的原因。
            if (Build(entry)) _lastFailReason = "";
        }
        catch (Exception ex)
        {
            _lastFailReason = ex.Message;
            LightLogger.LogError("[BackgroundRenderer.Build]", ex);
            Teardown();
        }
    }

    /// <summary>
    /// 原地刷新：不销毁网格/材质/VideoPlayer，只重算"原版背景与船员的隐藏"、"布局"、"音量"。
    /// 用于同一个素材下的设置变更 —— 视频不会黑屏、声音不会断。
    /// </summary>
    private static void RefreshInPlace()
    {
        try
        {
            // 船员显示 / 原版背景的隐藏集合可能变了 → 先还原再重算
            RestoreHidden();

            var cam = _root != null ? FindCamera(_root.layer) : null;
            if (cam != null) HideVanilla(cam);

            _lastLayout = null;      // 下一帧 Tick 会按新设置重排网格
            ApplyVideoAudio();       // 音量可能变了

            LightLogger.Log("[BackgroundRenderer] 原地刷新（未重建，视频不中断）");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[BackgroundRenderer.RefreshInPlace] {ex.Message}");
        }
    }

    /// <summary>适配模式 / 调暗变了：不用重建，下一帧 Tick 会自动重排。</summary>
    public static void RefreshLayout() => _lastLayout = null;

    /// <summary>主菜单每帧驱动（MainMenuPatch.TickAll 里调，由 LightTicker 跨场景驱动）。</summary>
    /// <summary>
    /// **原版样式下彻底关掉自定义背景**（用户 2026-11-03："背景图不要…背景图只在原版样式砍掉" ✓）。
    /// ⚠️ 只屏蔽"显示" ✗，不清用户选的那张图 ✓ —— 切回 MOD / 美化版时立刻恢复 ✓。
    /// </summary>
    public static bool Suppressed { get; set; }

    public static void Tick()
    {
        try
        {
            // ① 被屏蔽（原版样式）→ 拆掉已经建出来的背景对象，并直接返回 ✓
            if (Suppressed)
            {
                if (_root != null) Teardown();
                return;
            }

            // ① 先按场景名变化清一次跨场景状态（修"失败原因一旦非空就永久不重试"）
            ResetSceneStateIfChanged();

            // 场景不对 → 立刻收拾干净，杜绝外泄
            if (!IsInMenuScene())
            {
                DiagReturn("leave-menu-scene");
                if (_root != null) Teardown();
                return;
            }

            // 对象被场景切换销毁了（Unity 假 null）→ 重建
            if (_root == null && !string.IsNullOrEmpty(BackgroundStore.Selected))
            {
                if (_root == null && _lastFailReason == "" && ShouldHaveBackground())
                {
                    DiagReturn("rebuild-after-destroy");
                    Reapply();
                    DiagReturn("rebuild-done");
                }
                else
                {
                    // ⚠️ 卡在这里 = "永远不重建"。日志会打出到底是
                    //    _lastFailReason 非空（失败被记住）还是 ShouldHaveBackground() 为 false。
                    DiagReturn("rebuild-blocked");
                }
                return;
            }

            if (_root == null)
            {
                DiagReturn("no-root-no-selection");
                return;
            }

            // ⚠️ 2026-11-03 性能优化（用户："进入主界面非常卡"）：
            //   `FindCamera` 里有 `Camera.main`（tag 查找）+ `Camera.allCameras`（**每次都分配数组** ✗）
            //   → 每帧调一次 = 每帧一份 GC 垃圾 ✗ → 改成按 layer 缓存 + 每 60 帧复验一次 ✓
            var cam = GetCameraCached(_root.layer);
            if (cam == null)
            {
                DiagReturn("no-camera");
                return;
            }

            // 正常路径也留一条（每场景一次）：有它才说明"其实跑通了"，
            // 而不是"日志里没东西=没跑"。
            DiagReturn("ok");

            // 原版背景的隐藏只在 Build 那一刻做了一次。MatchMaking / FindAGame 是
            // "切场景 → 下一帧就 Tick"，原版背景有可能晚一点才出现/才 active →
            // 那一刻没藏到，我们（sortingOrder −30000，画在最后面）就永远被它盖住。
            // 这里做**有界**补做：最多 6 次、每次至少隔 30 帧，且只在"一个都没藏到"时试。
            // 绝不无限重试（那会变成每帧扫全场景）。
            if (_hiddenRenderers.Count == 0 && _hideRetryLeft > 0
                && Time.frameCount - _lastHideRetryFrame >= 30)
            {
                _hideRetryLeft--;
                _lastHideRetryFrame = Time.frameCount;
                LightLogger.Log($"[BackgroundRenderer] 原版背景一个都没藏到 → 补做第 {6 - _hideRetryLeft} 次" +
                                $"（场景='{SafeSceneName()}'）");
                HideVanilla(cam);
            }

            // ① 跟随相机：放在远裁剪面前一点点 → 永远在视野里、永远在所有 UI 后面
            var camT = cam.transform;
            float near = cam.nearClipPlane, far = cam.farClipPlane;
            float depth = far - Mathf.Max(2f, (far - Mathf.Max(near, 0f)) * 0.02f);
            _root.transform.position = camT.position + camT.forward * depth;
            _root.transform.rotation = camT.rotation;

            // ② 视频：等它**准备好**再开播。
            //
            // ⚠️⚠️ 用户反馈"进游戏的一帧会炸音频"。
            //    根因：以前 BuildVideo 里是**立刻 Play()**。
            //    那时解码器还没 ready，Direct 音频输出会吐出一段未初始化的缓冲
            //    → 就是那一声爆音（而且此时音量还没真正生效，可能是满音量）。
            //
            //    现在改成：BuildVideo 只调 Prepare()，**完全不播**；
            //    等到 isPrepared 之后，先把音轨/音量/静音都设好，再 Play()。
            //    准备期间视频和音频都是停的，所以不会漏出任何东西。
            if (_video != null && !_mediaReady)
            {
                if (_video.isPrepared && _video.width > 0 && _video.height > 0)
                {
                    _mediaW = _video.width;
                    _mediaH = _video.height;

                    // ① 先确立音频状态（音轨 / 音量 / 静音）—— 必须在 Play() 之前，
                    //    否则第一帧可能用默认音量出声（"炸音"）
                    _video.SetDirectAudioMute(0, true);      // 先彻底闭麦
                    ApplyVideoAudio();                       // 再按设置设好音量

                    // ② 续播：必须等 prepare 之后才能 seek（prepare 前设 time 无效/会抛）
                    if (_pendingResume > 0.05)
                    {
                        double target = _pendingResume;
                        _pendingResume = 0;
                        try
                        {
                            _video.time = target;
                            LightLogger.Log($"[BackgroundRenderer] 已续播到 {target:F1} 秒（不重头播）");
                        }
                        catch (Exception ex)
                        {
                            LightLogger.LogWarning($"[BackgroundRenderer] 续播 seek 失败：{ex.Message}");
                        }
                    }

                    // ③ 一切就绪，现在才开播
                    try { _video.Play(); }
                    catch (Exception ex) { LightLogger.LogWarning($"[BackgroundRenderer] Play 失败：{ex.Message}"); }

                    // ④⚠️⚠️ **不要在这一帧就解除静音！**
                    //   这是"开头炸音"反复出现的原因：`Play()` 之后的头几帧，
                    //   解码器吐出的仍可能是未初始化/未对齐的缓冲。
                    //   上一版虽然改成"先 Prepare 再 Play"，但解除静音和 Play 同一帧，
                    //   那段脏样本照样听得见。
                    //
                    //   现在改成：Play 之后**继续保持 0 音量**，由 Tick 在随后几帧里
                    //   把音量**渐变**推上去（见 AudioFadeFrames）。这样：
                    //     ① 脏样本那几帧音量正好是 0，听不见；
                    //     ② 音量是渐变的不是跳变的，也不会因为增益突变产生爆音。
                    _mediaReady = true;
                    _videoStarted = true;
                    if (_mediaRenderer != null) _mediaRenderer.enabled = true;

                    _fadeTarget = EffectiveVideoVolume();
                    _fadeLeft = AudioFadeFrames;
                    try
                    {
                        _video.SetDirectAudioMute(0, true);
                        _video.SetDirectAudioVolume(0, 0f);
                    }
                    catch { }

                    LightLogger.Log($"[BackgroundRenderer] 视频已就绪并开播（音量渐入到 {_fadeTarget * 100f:F0}%，{AudioFadeFrames} 帧）");
                }
                else if (Time.realtimeSinceStartup - _createdAt > VideoPrepareTimeout)
                {
                    _lastFailReason = "视频在 20 秒内没有准备好，可能是编码不支持（建议 H.264 的 mp4）。已退回原版背景。";
                    LightLogger.LogWarning("[BackgroundRenderer] " + _lastFailReason);
                    DiagReturn("video-prepare-timeout");
                    Teardown();
                    return;
                }
            }

            // ③ 布局：屏幕/素材尺寸变了才重建网格
            float viewH = cam.orthographic
                ? cam.orthographicSize * 2f
                : 2f * depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float viewW = viewH * cam.aspect;

            var layout = (viewW, viewH, _mediaW, _mediaH,
                          (int)BackgroundStore.Fit, BackgroundStore.Dim, _mediaReady);
            if (_lastLayout == null || !_lastLayout.Value.Equals(layout))
            {
                _lastLayout = layout;
                Layout(viewW, viewH);
            }

            // ③.5 开播后的音量渐入（修复"开头炸音"）
            //     Play() 之后的头几帧，解码器可能还在吐未初始化/未对齐的缓冲。
            //     这里让那几帧音量为 0，然后逐帧推上去 ——
            //     脏样本听不见，增益又是渐变的，不会爆音。
            if (_videoStarted && _fadeLeft > 0 && _video != null)
            {
                _fadeLeft--;
                float prog = 1f - _fadeLeft / (float)AudioFadeFrames;   // 0 → 1
                float v = _fadeTarget * prog;
                try
                {
                    _video.SetDirectAudioMute(0, false);   // 音量本身已经是 0 起步，不必再 mute
                    _video.SetDirectAudioVolume(0, ExtVol(v));   // ← 淡入也要过 ExtVol

                    if (_fadeLeft == 0)
                    {
                        // 收尾：写死成精确目标值，避免浮点误差
                        float ft = ExtVol(_fadeTarget); _video.SetDirectAudioVolume(0, ft);
                        _video.SetDirectAudioMute(0, ft <= 0.001f);
                        LightLogger.Log($"[BackgroundRenderer] 音量渐入完成：{_fadeTarget * 100f:F0}%");
                    }
                }
                catch { }
            }

            // ④ 视频音量（跟随设置；模式变了下一帧就会重新应用）
            if (_video != null)
            {
                float vol = EffectiveVideoVolume();
                // ⚠️ 只有**已经开播**才在这里实时改音量。
                //    准备期间是刻意闭麦的，这时候调音量会把它提前解除静音 → 又会炸音。
                if (_videoStarted && !Mathf.Approximately(vol, _lastVolume))
                {
                    _lastVolume = vol;
                    ApplyVideoAudio();
                }
                if (_mediaReady && _video.audioTrackCount > 0) MuteVanillaMusic();
            }
        }
        catch (Exception ex)
        {
            DiagReturn("exception");
            LightLogger.LogWarning($"[BackgroundRenderer.Tick] {ex.Message}");
            Teardown();
        }
    }

    private static bool ShouldHaveBackground()
    {
        var sel = BackgroundStore.Selected;
        return !string.IsNullOrEmpty(sel) && BackgroundStore.Scan().Count > 0;
    }

    private static bool IsInMenuScene()
    {
        var n = SafeSceneName();
        // ⚠️ FindAGame（"搜索游戏"）与 MatchMaking（"本地"/连线区）也纳入 —— 用户要求那几个
        //    界面里背景图继续可见、而且音频不能断。加了它们以后 Tick() 不会走
        //    "!IsInMenuScene() → Teardown()" 分支，VideoPlayer 不被销毁
        //    → **音频跨场景继续播**。
        //    ⚠️ 光改这里**不够**：Tick() 得真的被调用才行。以前只有
        //    MainMenuManager.LateUpdate 这一个驱动源，而 MainMenuManager 只存在于
        //    MainMenu 场景 → 这里写的 MatchMaking / FindAGame 判断全是死代码。
        //    现在由 LightTicker（DontDestroyOnLoad 常驻）驱动，见该类的注释。
        // ⚠️ 2026-10-05：**已撤回 FindAGame** —— 用户实测「本地/搜索游戏里背景图显示不出来」，
            //    试了两轮（加场景名 + 修跨场景驱动器）仍未达成，用户决定**放弃该功能**。
            //    保持和改动前一致：只有 MainMenu 和 MatchMaking
            //    （MatchMaking 本来就在集合里，是改动前就有的行为，未动）。
            return n == "MainMenu" || n == "MatchMaking";
    }

    // =====================================================================
    //  构建 / 拆除
    // =====================================================================

    /// <summary>
    /// 建出背景对象。**返回是否真的建成功**（调用方据此决定要不要清 <c>_lastFailReason</c>）。
    /// ⚠️ 以前是 void，于是"Build 里设了失败原因、Reapply 立刻把它清成空"
    ///    导致每帧重试且永远看不到失败原因。
    /// </summary>
    private static bool Build(BackgroundEntry entry)
    {
        int layer = _lastLayer >= 0 ? _lastLayer : LayerExpansion.GetUILayer();
        var cam = FindCamera(layer);
        if (cam == null)
        {
            _lastFailReason = "找不到主界面相机。";
            LightLogger.LogWarning($"[BackgroundRenderer.Build] {_lastFailReason}" +
                                   $"（场景='{SafeSceneName()}' layer={layer}，" +
                                   $"相机数={Camera.allCameras.Length}）");
            return false;
        }

        _generation++;
        _currentPath = entry.FullPath;
        _mediaReady = false;
        _mediaW = _mediaH = 1f;

        // ⚠️⚠️ **绝对不要调用 DontDestroyOnLoad** —— 旧实现就是在这一行上翻车的：
        //    它让背景对象活过所有场景，进游戏/进大厅都还在，只能靠一堆
        //    "进别的场景就 SetActive(false) + 缩到 0.0001" 的补丁去藏，
        //    漏掉任何一个场景就露出来（用户报的"背景图外泄"）。
        //    Unity 没有"取消 DontDestroyOnLoad"的 API，所以唯一的正确做法是根本别调。
        //    不调 → 对象属于当前场景 → 场景卸载时自动销毁 → 从根上不可能外泄。
        _root = new GameObject(RootName);
        _root.layer = layer;
        _root.transform.SetParent(null, false);

        // 黑底（Contain 模式的黑边 + 视频加载期用）
        var unlit = Shader.Find("Sprites/Default");
        _backdropFilter = CreateQuad("Backdrop", _root.transform, layer, unlit, out _backdropRenderer);
        if (_backdropRenderer != null)
        {
            _backdropRenderer.sortingOrder = MediaSortingOrder - 1;
            SetMaterialColor(_backdropRenderer, Color.black);
        }

        // 素材网格
        _mediaFilter = CreateQuad("Media", _root.transform, layer, unlit, out _mediaRenderer);
        if (_mediaRenderer != null)
        {
            _mediaRenderer.sortingOrder = MediaSortingOrder;
            _mediaMaterial = _mediaRenderer.material;
        }

        // 同一个素材重建 → 续播；换了素材 → 从头
        PrepareResume(entry.FullPath);

        bool ok = entry.IsVideo ? BuildVideo(entry) : BuildImage(entry);
        if (!ok) return false;      // BuildImage/BuildVideo 失败时已经自己 Teardown 并设好原因

        HideVanilla(cam);
        _createdAt = Time.realtimeSinceStartup;
        // 允许后面几帧补做"藏原版背景"（见 Tick 里那段；有界）
        _hideRetryLeft = 6;
        _lastHideRetryFrame = Time.frameCount;
        return true;
    }

    /// <summary>建图片背景。返回是否成功（失败时已 Teardown 并写好 <c>_lastFailReason</c>）。</summary>
    private static bool BuildImage(BackgroundEntry entry)
    {
        var tex = LoadTexture(entry.FullPath);
        if (tex == null)
        {
            _lastFailReason = $"图片解码失败（只支持 PNG/JPG）：{entry.FileName}";
            LightLogger.LogWarning("[BackgroundRenderer] " + _lastFailReason);
            Teardown();
            return false;
        }
        if (_mediaMaterial != null) _mediaMaterial.mainTexture = tex;
        _mediaW = tex.width;
        _mediaH = tex.height;
        _mediaReady = true;
        return true;
    }

    /// <summary>建视频背景。返回是否成功（失败时已 Teardown 并写好 <c>_lastFailReason</c>）。</summary>
    private static bool BuildVideo(BackgroundEntry entry)
    {
        if (_mediaRenderer == null || _root == null)
        {
            _lastFailReason = "视频初始化前网格没建出来。";
            Teardown();
            return false;
        }

        // 第一帧出来之前先藏起来，避免闪白
        _mediaRenderer.enabled = false;

        try
        {
            // ⚠️ 用户明确要求：**不要反射**，直接 using UnityEngine.Video 调用。
            //    我们是 BepInEx 插件，Libs 里有 UnityEngine.VideoModule.dll，
            //    编译期就能引用，不需要参考实现那套反射兜底。
            _video = _root.AddComponent<VideoPlayer>();
            _video.playOnAwake = false;
            _video.source = VideoSource.Url;
            _video.url = entry.FullPath;
            _video.isLooping = true;
            _video.renderMode = VideoRenderMode.MaterialOverride;
            _video.targetMaterialRenderer = _mediaRenderer;
            _video.targetMaterialProperty = "_MainTex";
            _video.audioOutputMode = VideoAudioOutputMode.Direct;
            _video.skipOnDrop = true;
            // 我们已经在 isPrepared 之前把渲染器 enabled=false 了，不会闪白；
            // 设 true 会让 Play() 一直等到首帧才真正开始，白白多等一截。
            _video.waitForFirstFrame = false;
            // Direct 输出模式下要告诉播放器"我要接管 1 条音轨"，否则声音出不来。
            // 必须在 Play() 之前设。
            try { _video.controlledAudioTrackCount = 1; } catch { }

            _lastVolume = EffectiveVideoVolume();

            // ⚠️⚠️ 关键：这里**只 Prepare，不 Play**。
            //   以前直接 Play()，解码器还没就绪就开播 →
            //   Direct 音频输出会吐一段未初始化缓冲 = 那一声"炸音"，而且此时
            //   音量还没真正生效，可能是满音量。
            //
            //   现在：Prepare() 期间视频/音频都是停的，什么都漏不出来；
            //   等 Tick 里 isPrepared 之后再设音量、再 Play()。
            _videoStarted = false;

            // 准备期间先彻底闭麦（双保险：万一播放器自己在准备阶段出声）
            try
            {
                _video.EnableAudioTrack(0, true);
                _video.SetDirectAudioVolume(0, 0f);
                _video.SetDirectAudioMute(0, true);
            }
            catch { }

            _video.Prepare();

            LightLogger.Log($"[BackgroundRenderer] 视频开始准备（未开播）：{entry.FileName}，" +
                            $"目标音量={_lastVolume:F2}（{BackgroundStore.VideoVolumeLabel}）");
            return true;
        }
        catch (Exception ex)
        {
            _lastFailReason = $"视频初始化失败：{ex.Message}";
            LightLogger.LogError("[BackgroundRenderer.BuildVideo]", ex);
            Teardown();
            return false;
        }
    }

    /// <summary>拆除：销毁网格/材质/组件，恢复原版背景与音乐。可重复调用。</summary>
    public static void Teardown()
    {
        _generation++;
        _lastLayout = null;
        _mediaReady = false;

        // ⚠️⚠️ 拆之前先把播放进度记下来。
        //    用户反馈"改音量 / 应用任何东西视频都会重新播放" ——
        //    因为 Reapply() = Teardown() + Build()，VideoPlayer 被销毁重建，自然从头播。
        //    这里存下 (路径, 秒数)，重建后如果还是同一个素材就 seek 回去（见 Tick 里那段）。
        SavePlaybackPosition();

        _currentPath = "";

        try
        {
            if (_video != null)
            {
                try { _video.Stop(); } catch { }
                try { UnityEngine.Object.Destroy(_video); } catch { }
            }
            _video = null;

            if (_mediaFilter != null && _mediaFilter.sharedMesh != null)
                UnityEngine.Object.Destroy(_mediaFilter.sharedMesh);
            if (_backdropFilter != null && _backdropFilter.sharedMesh != null)
                UnityEngine.Object.Destroy(_backdropFilter.sharedMesh);

            if (_backdropRenderer != null && _backdropRenderer.sharedMaterial != null)
                UnityEngine.Object.Destroy(_backdropRenderer.sharedMaterial);
            if (_mediaMaterial != null)
                UnityEngine.Object.Destroy(_mediaMaterial);

            if (_root != null) UnityEngine.Object.Destroy(_root);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[BackgroundRenderer.Teardown] {ex.Message}");
        }

        _root = null;
        _mediaRenderer = null;
        _mediaFilter = null;
        _backdropRenderer = null;
        _backdropFilter = null;
        _mediaMaterial = null;
        _lastLayout = null;
        _lastVolume = -1f;
        _mediaW = _mediaH = 1f;
        _loggedAudioDiag = false;
        _hideRetryLeft = 0;      // 拆掉了就不用再补做隐藏

        RestoreHidden();
        RestoreVanillaMusic();
    }

    public static void Shutdown()
    {
        // 真正离开主菜单 → 不该续播，下次进来从头开始
        _resumeTime = 0;
        _resumePath = "";
        _pendingResume = 0;
        Teardown();
    }

    // =====================================================================
    //  播放进度保持（"应用设置不该让视频重头播"）
    // =====================================================================

    /// <summary>上次拆掉时的播放位置（秒）。</summary>
    private static double _resumeTime;
    /// <summary>上次拆掉时播的是哪个素材 —— 换了素材就不该续播。</summary>
    private static string _resumePath = "";
    /// <summary>这次重建要 seek 到的位置（0 = 从头播）。</summary>
    private static double _pendingResume;

    /// <summary>拆之前记下播放进度。</summary>
    private static void SavePlaybackPosition()
    {
        try
        {
            if (_video == null || !_video.isPrepared) return;
            if (string.IsNullOrEmpty(_currentPath)) return;

            _resumeTime = _video.time;
            _resumePath = _currentPath;
        }
        catch { }
    }

    /// <summary>这次要建的是不是"同一个素材"—— 是就续播，否则从头。</summary>
    private static void PrepareResume(string newPath)
    {
        if (!string.IsNullOrEmpty(newPath) && newPath == _resumePath && _resumeTime > 0.05)
            _pendingResume = _resumeTime;
        else
            _pendingResume = 0;
    }

    /// <summary>
    /// 只改音量：**不重建**，直接把新音量喂给正在播的视频。
    /// （重建会黑一帧 + 重新解码，改个音量完全没必要。）
    /// </summary>
    public static void ApplyAudioOnly() => ApplyVideoAudio();

    /// <summary>
    /// 只改裁剪模式 / 调暗：**不重建**，下一帧 Tick 会自动重排网格。
    /// </summary>
    public static void ApplyLayoutOnly() => RefreshLayout();

    // =====================================================================
    //  网格 / 材质
    // =====================================================================

    /// <summary>造一个 1×1 居中的四边形网格（靠 localScale 拉成任意尺寸）。</summary>
    private static Mesh CreateUnitQuad()
    {
        var mesh = new Mesh { name = "BackgroundQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f),
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 1f),
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static MeshFilter CreateQuad(string name, Transform parent, int layer,
        Shader? shader, out MeshRenderer renderer)
    {
        var go = new GameObject(name);
        go.layer = layer;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;

        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = CreateUnitQuad();

        renderer = go.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        var mat = shader != null ? new Material(shader) : null;
        if (mat != null) renderer.sharedMaterial = mat;

        return filter;
    }

    private static void SetMaterialColor(Renderer r, Color c)
    {
        try
        {
            var m = r.sharedMaterial;
            if (m != null && m.HasProperty("_Color")) m.color = c;
        }
        catch { }
    }

    /// <summary>按屏幕尺寸 + 适配模式摆放媒体网格。</summary>
    private static void Layout(float viewW, float viewH)
    {
        if (_backdropFilter != null && _backdropFilter.sharedMesh != null)
        {
            // 稍微放大一点，避免边缘露缝
            var t = _backdropFilter.transform;
            t.localScale = new Vector3(viewW * 1.05f, viewH * 1.05f, 1f);
        }

        if (!_mediaReady || _mediaFilter == null) return;

        float screenAspect = viewW / viewH;
        float mediaAspect = _mediaW / Mathf.Max(1f, _mediaH);

        float w = viewW, h = viewH;
        switch (BackgroundStore.Fit)
        {
            case BackgroundFit.Cover:
                // 等比放大到铺满，多出来的溢出屏幕外（不需要裁 UV，屏幕边缘天然裁掉）
                if (mediaAspect > screenAspect) w = viewH * mediaAspect;
                else h = viewW / mediaAspect;
                break;

            case BackgroundFit.Contain:
                // 等比缩小到完整可见，四周留黑（黑底在 Backdrop 那层）
                if (mediaAspect > screenAspect) h = viewW / mediaAspect;
                else w = viewH * mediaAspect;
                break;

            case BackgroundFit.Stretch:
                // 不动，直接铺满
                break;
        }

        _mediaFilter.transform.localScale = new Vector3(w, h, 1f);

        if (_mediaRenderer != null)
        {
            float k = 1f - BackgroundStore.Dim;
            SetMaterialColor(_mediaRenderer, new Color(k, k, k, 1f));
        }
    }

    private static Texture2D? LoadTexture(string path)
    {
        try
        {
            string key = path + "|" + System.IO.File.GetLastWriteTimeUtc(path).Ticks;
            if (_cachedTex != null && _cachedTexKey == key) return _cachedTex;

            if (_cachedTex != null) UnityEngine.Object.Destroy(_cachedTex);
            _cachedTex = null;
            _cachedTexKey = null;

            byte[] bytes;
            try { bytes = System.IO.File.ReadAllBytes(path); }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[BackgroundRenderer] 读不到图片 {path}：{ex.Message}");
                return null;
            }

            var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!ImageConversion.LoadImage(tex, bytes, false))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            // 场景切换时别被 UnloadUnusedAssets 回收（AGENTS.md §4.6 的"假 null"）
            tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            _cachedTex = tex;
            _cachedTexKey = key;
            return tex;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[BackgroundRenderer.LoadTexture]", ex);
            return null;
        }
    }

    // =====================================================================
    //  原版背景 / 音乐
    // =====================================================================

    private static readonly string[] VanillaBackgroundNames = { "BackgroundTexture", "WindowShine" };

    /// <summary>
    /// 藏掉原版背景。
    ///
    /// ⚠️⚠️ **这是"MatchMaking / FindAGame 里看不到我们的背景"的第二大嫌疑**：
    ///   原来的实现是 <c>FindObjectOfType&lt;MainMenuManager&gt;()</c>，拿不到就直接 return。
    ///   而 <c>MainMenuManager</c> **只存在于 MainMenu 场景** → 在那两个场景里
    ///   一个原版背景都没藏 → 我们的网格 sortingOrder = −30000（画在最后面）
    ///   **被原版那张铺满屏幕的背景完全盖住**。
    ///   （主菜单之所以正常，正是因为这里把它的 <c>BackgroundTexture</c> 关掉了。）
    ///
    ///   现在：拿不到 MainMenuManager 时，改从**当前场景的所有根物体**里找。
    ///   匹配规则（**故意分两套**）：
    ///     · MainMenu（有 MainMenuManager）→ 与改造前完全一致：先按名字
    ///       <c>BackgroundTexture</c> / <c>WindowShine</c>，没命中就退回"铺满屏幕的大贴图"；
    ///     · MatchMaking / FindAGame（没有 MainMenuManager）→ 只按名字 + "名字里带 background
    ///       的铺满屏幕大贴图"（**不用**通用大贴图启发式，免得藏错黑幕/遮罩）。
    ///   无论藏没藏到，候选都会打进日志（<see cref="LogBackgroundCandidatesOnce"/>）。
    /// </summary>
    /// </summary>
    private static void HideVanilla(Camera cam)
    {
        try
        {
            var menu = UnityEngine.Object.FindObjectOfType<MainMenuManager>();

            var all = new List<SpriteRenderer>();
            void Collect(Transform? t)
            {
                if (t == null) return;
                foreach (var sr in t.GetComponentsInChildren<SpriteRenderer>(true))
                    if (sr != null && !all.Contains(sr)) all.Add(sr);
            }

            if (menu != null)
            {
                Collect(menu.transform);
                if (menu.mainMenuUI != null) Collect(menu.mainMenuUI.transform);
            }
            else
            {
                // MatchMaking / FindAGame：没有 MainMenuManager → 全场景收集
                var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
                foreach (var go in roots)
                {
                    if (go == null) continue;
                    Collect(go.transform);
                }
                LightLogger.Log($"[BackgroundRenderer.HideVanilla] 场景 '{SafeSceneName()}' 里没有 MainMenuManager，" +
                                $"改从 {roots.Length} 个场景根物体收集原版背景候选（共 {all.Count} 个 SpriteRenderer）");
            }

            float viewH = cam.orthographicSize * 2f;
            float viewW = viewH * cam.aspect;

            // ① 先按名字（原版背景就叫 BackgroundTexture，辉光装饰叫 WindowShine）
            var targets = all.Where(r => Array.IndexOf(VanillaBackgroundNames, r.name) >= 0).ToList();

            if (menu == null)
            {
                // ② 未知场景（MatchMaking / FindAGame / 以后再加的场景）：
                //    **只再补"名字里带 background 的铺满屏幕大贴图"**这一种。
                //
                //    ⚠️ 刻意**不**在这里用下面那条"铺满屏幕的大贴图"通用启发式：
                //    那两个场景里有 ScreenMask / 黑幕 / 列表底板 / 面板底图之类
                //    同样铺满屏幕的东西，误藏会直接把原版界面搞坏
                //    （而且用户看到的是"东西不见了"，很难联想到是我们藏的）。
                //    名字对不上怎么办？→ LogBackgroundCandidatesOnce 已经把候选全打进日志了，
                //    看日志补名字即可。**宁可不藏，也不能藏错。**
                var extra = all.Where(r =>
                    r != null
                    && r.name.IndexOf("background", StringComparison.OrdinalIgnoreCase) >= 0
                    && IsFullScreenCandidate(r, viewW, viewH)).ToList();
                foreach (var r in extra)
                    if (!targets.Contains(r)) targets.Add(r);
            }
            else if (targets.Count == 0)
            {
                // MainMenu：行为与改造前**完全一致** —— 名字没命中就退回"铺满屏幕的大贴图"
                targets = all.Where(r => IsFullScreenCandidate(r, viewW, viewH)).ToList();
            }

            // 诊断：把"名字里带 background 的活跃渲染器"全打出来（每场景一次）。
            // 目的：万一原版背景名字不叫 BackgroundTexture，日志里能直接看到它叫什么。
            LogBackgroundCandidatesOnce(all, cam);

            foreach (var r in targets)
            {
                if (r == null) continue;
                _hiddenRenderers.Add((r, r.enabled));
                r.enabled = false;
            }

            if (BackgroundStore.HideCrewmates)
            {
                foreach (var p in UnityEngine.Object.FindObjectsOfType<PlayerParticles>())
                {
                    if (p == null || !p.gameObject.activeSelf) continue;
                    _hiddenObjects.Add((p.gameObject, true));
                    p.gameObject.SetActive(false);
                }
            }

            HideAmbienceDecor();

            LightLogger.Log($"[BackgroundRenderer] 已隐藏 {_hiddenRenderers.Count} 个原版背景渲染器、" +
                            $"{_hiddenObjects.Count} 个装饰物体（场景='{SafeSceneName()}'，" +
                            $"本次新建 {targets.Count} 个：{string.Join(",", targets.Where(r => r != null).Select(r => r.name))}）");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[BackgroundRenderer.HideVanilla] {ex.Message}");
        }
    }

    /// <summary>"铺满整个屏幕的大贴图"= 原版背景的候选（沿用旧实现的判定，一个字没改）。</summary>
    private static bool IsFullScreenCandidate(SpriteRenderer r, float viewW, float viewH)
    {
        try
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.sprite == null) return false;
            var n = r.name.ToLowerInvariant();
            // 名字像遮罩/渐隐/按钮的一律不算（这些也常常铺满屏幕，但藏了会出事）
            if (n.Contains("tint") || n.Contains("fade") || n.Contains("mask") || n.Contains("button")) return false;
            var sz = r.bounds.size;
            return sz.x >= viewW * 0.9f && sz.y >= viewH * 0.9f;
        }
        catch { return false; }
    }

    /// <summary>每场景一次：把"可能是原版背景"的渲染器打出来（名字/尺寸/sortingOrder/layer）。</summary>
    private static void LogBackgroundCandidatesOnce(List<SpriteRenderer> all, Camera cam)
    {
        try
        {
            string scene = SafeSceneName();
            if (_loggedCandScene == scene) return;
            _loggedCandScene = scene;

            float viewH = cam.orthographicSize * 2f;
            float viewW = viewH * cam.aspect;

            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var r in all)
            {
                if (r == null) continue;
                // 只看"名字带 background/star/bg"或者"够大"的，否则日志太长
                var nm = r.name.ToLowerInvariant();
                bool byName = nm.Contains("background") || nm.Contains("windowshine") || nm.Contains("star");
                bool big = r.gameObject.activeInHierarchy && r.enabled && r.sprite != null
                           && r.bounds.size.x >= viewW * 0.9f && r.bounds.size.y >= viewH * 0.9f;
                if (!byName && !big) continue;

                var sz = r.bounds.size;
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append($"{r.name}(size={sz.x:F1}x{sz.y:F1},order={r.sortingOrder}," +
                          $"layer={r.gameObject.layer},active={r.gameObject.activeInHierarchy},enabled={r.enabled}," +
                          $"big={big})");
                n++;
                if (n >= 20) break;
            }

            LightLogger.Log($"[BackgroundRenderer.bg候选] 场景='{scene}' 视口={viewW:F1}x{viewH:F1} " +
                            $"(MainMenuManager={(UnityEngine.Object.FindObjectOfType<MainMenuManager>() != null ? "有" : "无")})：" +
                            (n == 0 ? "（一个都没有）" : sb.ToString()));
        }
        catch { }
    }

    private static string _loggedCandScene = "";

    /// <summary>
    /// 隐藏原版主界面背景上的**星星装饰**（用户说的 <c>Ambience.starfield</c>）。
    ///
    /// 旧的 <c>ImageGalleryPanel</c> 路径里本来有这么一段：
    /// <code>
    /// var ambience = FindGO("Ambience");
    /// ambience.transform.FindChild("PlayerParticles")?.gameObject.SetActive(false);
    /// if (ambience.transform.childCount &gt; 0) ambience.transform.GetChild(0).gameObject.SetActive(false);
    /// </code>
    /// 我重写成 BackgroundRenderer 时把它漏掉了 → 换上自定义背景后星星还浮在上面。
    ///
    /// 这里按**名字**找（"star"），不依赖子物体序号；顺便把 Ambience 的子物体名字
    /// 打一行日志，万一名字不叫 star* 也能从日志看出来。
    /// </summary>
    private static void HideAmbienceDecor()
    {
        try
        {
            var ambience = GameObject.Find("Ambience");
            if (ambience == null)
            {
                LightLogger.LogWarning("[BackgroundRenderer] 找不到 Ambience，跳过星星装饰清理");
                return;
            }

            var names = new System.Text.StringBuilder();
            int hidden = 0;
            for (int i = 0; i < ambience.transform.childCount; i++)
            {
                var child = ambience.transform.GetChild(i);
                if (child == null) continue;
                if (names.Length > 0) names.Append(", ");
                names.Append(child.name);

                bool isStar = child.name.IndexOf("star", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isStar) continue;
                if (!child.gameObject.activeSelf) continue;

                _hiddenObjects.Add((child.gameObject, true));
                child.gameObject.SetActive(false);
                hidden++;
            }

            LightLogger.Log($"[BackgroundRenderer] Ambience 子物体：{names}（已隐藏 {hidden} 个星名匹配项）");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[BackgroundRenderer.HideAmbienceDecor] {ex.Message}");
        }
    }

    private static void RestoreHidden()    {
        foreach (var (sr, was) in _hiddenRenderers)
        {
            try { if (sr != null) sr.enabled = was; } catch { }
        }
        _hiddenRenderers.Clear();

        foreach (var (go, was) in _hiddenObjects)
        {
            try { if (go != null) go.SetActive(was); } catch { }
        }
        _hiddenObjects.Clear();
    }

    private static float ReadMusicVolume()
    {
        try
        {
            return Mathf.Clamp01(AmongUs.Data.DataManager.Settings.Audio.MusicVolume);
        }
        catch (Exception ex)
        {
            // ⚠️⚠️ 读不到就按**满音量**处理，绝不能按 0 —— 0 就是静音。
            //   参考实现写的是 `catch { return 0f; }`，一旦这次读取失败，
            //   视频就**永远是哑的**，而且看不出任何报错（"视频能放但没声音"就是这么来的）。
            if (!_loggedVolumeFallback)
            {
                _loggedVolumeFallback = true;
                LightLogger.LogWarning(
                    $"[BackgroundRenderer] 读不到游戏音乐音量（{ex.Message}）→ 视频按 100% 音量播放");
            }
            return 1f;
        }
    }

    private static bool _loggedVolumeFallback;
    private static bool _loggedAudioDiag;

    /// <summary>
    /// 视频**实际**该用的音量。
    ///
    /// ⚠️ 参考实现是"永远跟随游戏「音乐」音量"，实测会踩坑：
    ///    这台机器 <c>musicVolume = 0.1378</c>（14%）→ 视频被压到几乎听不见，
    ///    用户看到的就是"视频在放，但没声音"。
    ///    所以改成 0~100 的整数百分比（<see cref="BackgroundStore.VideoVolumePercent"/>），**默认 100%**。
    /// </summary>
    private static float EffectiveVideoVolume()
    {
        return Mathf.Clamp01(BackgroundStore.VideoVolumePercent / 100f);
    }

    /// <summary>
    /// 重新确认视频音轨与音量。
    ///
    /// ⚠️ 必须在**视频 prepare 完成之后**再调一次：prepare 之前
    ///   <c>audioTrackCount</c> 还是 0，那时调 <c>EnableAudioTrack</c> 不一定会生效。
    /// </summary>
    // =====================================================================
    //  外部静音（音乐播放器要求"不共存"时用）
    // =====================================================================

    private static bool _externalMute;

    /// <summary>
    /// 外部（音乐播放器）要求把**背景视频的音轨**静音。
    ///
    /// ⚠️ 为什么不用 SoundManager.SetChannelVolume(…, "MusicVolume") 那套（MuteVanillaMusic 的做法）：
    ///    那是 **AudioMixer 全局通道**，会影响所有走该通道的声音，且还原只能"按设置重算"，
    ///    还原不回运行期的实际状态。这里只需要压**我们自己的视频音轨**，对象级更精确。
    /// ⚠️ 只改音量/静音，**不 Stop / 不 Pause** —— 画面必须继续走。
    /// </summary>
    public static bool ExternalMute
    {
        get => _externalMute;
        set
        {
            try
            {
                if (_externalMute == value) return;
                _externalMute = value;
                ApplyVideoAudio();
                LightLogger.Log($"[BackgroundRenderer] 背景视频音轨={(value ? "已静音（音乐播放器要求不共存）" : "已恢复")}");
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[BackgroundRenderer.ExternalMute] {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 所有"写视频音量"的地方都必须过这个函数。
    /// ⚠️ 只在 ApplyVideoAudio 里判 _externalMute 是**不够的** —— 开播淡入那段（Tick 里）
    ///    是**直接**写 SetDirectAudioVolume 的，绕过了 ApplyVideoAudio。
    /// </summary>
    private static float ExtVol(float v) => _externalMute ? 0f : v;

    private static void ApplyVideoAudio()
    {
        var v = _video;
        if (v == null) return;
        try
        {
            v.EnableAudioTrack(0, true);
            float vol = EffectiveVideoVolume();
            _lastVolume = vol;

            // ⚠️⚠️ **还没开播（正在 Prepare）就只记下目标音量，保持闭麦。**
            //    ApplyAudioOnly / ReapplyAudio 都会走到这里；这时若解除静音，
            //    未初始化的解码缓冲会被直接放出来 —— 就是用户听到的那声"炸音"。
            if (!_videoStarted)
            {
                try
                {
                    v.SetDirectAudioVolume(0, 0f);
                    v.SetDirectAudioMute(0, true);
                }
                catch { }
                return;
            }

            vol = ExtVol(vol);                       // ← 外部静音时压成 0
            v.SetDirectAudioVolume(0, vol);
            v.SetDirectAudioMute(0, vol <= 0.001f);

            if (!_loggedAudioDiag)
            {
                _loggedAudioDiag = true;
                LightLogger.Log($"[BackgroundRenderer] 视频音频已就绪：音轨数={v.audioTrackCount} " +
                                $"输出模式={v.audioOutputMode} 实际音量={vol:F2} " +
                                $"模式={BackgroundStore.VideoVolumeLabel} " +
                                $"(音轨数 0 = 这个文件本来就没声音；" +
                                $"游戏音乐音量设置={SafeMusicSetting():F2})");
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[BackgroundRenderer.ApplyVideoAudio] {ex.Message}");
        }
    }

    private static float SafeMusicSetting()
    {
        try { return Mathf.Clamp01(AmongUs.Data.DataManager.Settings.Audio.MusicVolume); }
        catch { return -1f; }
    }

    /// <summary>设置里的音量模式改了 → 立刻重新应用到正在播的视频（不用重建）。</summary>
    public static void ReapplyAudio() => ApplyVideoAudio();

    /// <summary>视频音频是否可用（界面提示用）。</summary>
    public static int VideoAudioTrackCount
    {
        get { try { return _video?.audioTrackCount ?? 0; } catch { return 0; } }
    }

    /// <summary>当前视频实际音量（界面提示用）。</summary>
    public static float VideoVolume => _lastVolume < 0f ? 0f : _lastVolume;

    private static void MuteVanillaMusic()
    {
        try
        {
            var sm = SoundManager.Instance;
            if (sm == null) return;
            float unused = 0f;
            sm.SetChannelVolume(0f, ref unused, "MusicVolume");
            _vanillaMusicMuted = true;
        }
        catch { }
    }

    private static void RestoreVanillaMusic()
    {
        if (!_vanillaMusicMuted) return;
        _vanillaMusicMuted = false;
        try { SoundManager.Instance?.UpdateChannelVolumes(); } catch { }
    }

    // =====================================================================

    private static Camera? FindCamera(int layer)
    {
        int bit = 1 << layer;
        var main = Camera.main;
        if (main != null && (main.cullingMask & bit) != 0) return main;
        foreach (var c in Camera.allCameras)
            if (c != null && c.enabled && (c.cullingMask & bit) != 0) return c;
        return main;
    }

    // ── 相机缓存（每帧路径用这个 ✓，别再每帧 FindCamera ✗）──
    private static Camera? _cachedCam;
    private static int _cachedCamLayer = int.MinValue;
    private static int _cachedCamFrame = -100000;
    private const int CameraRecheckFrames = 60;      // 每 60 帧（≈1 秒）复验一次 ✓

    /// <summary>
    /// 带缓存的相机查找：同 layer 且缓存还活着就复用 ✓，否则每 <see cref="CameraRecheckFrames"/> 帧
    /// 或换了 layer 时重新查一次 ✓（避免每帧 `Camera.allCameras` 的数组分配 ✗）。
    /// </summary>
    private static Camera? GetCameraCached(int layer)
    {
        try
        {
            bool layerChanged = layer != _cachedCamLayer;
            bool stale = _cachedCam == null;            // == 走 Unity 假 null 判断 ✓
            bool due = Time.frameCount - _cachedCamFrame >= CameraRecheckFrames;

            if (!layerChanged && !stale && !due) return _cachedCam;

            _cachedCam = FindCamera(layer);
            _cachedCamLayer = layer;
            _cachedCamFrame = Time.frameCount;
            return _cachedCam;
        }
        catch
        {
            return FindCamera(layer);
        }
    }

    /// <summary>诊断：把关键状态打一行（排查"看不见"用，AGENTS.md §4.8）。</summary>
    public static void LogDiagnostics()
    {
        try
        {
            var cam = _root != null ? FindCamera(_root.layer) : null;
            LightLogger.Log($"[BackgroundRenderer.Diag] root={(_root == null ? "null" : _root.name)} " +
                            $"layer={(_root == null ? -1 : _root.layer)} " +
                            $"lastLayer={_lastLayer} scene='{SafeSceneName()}' " +
                            $"ticker={Light.Utilities.LightTicker.IsRunning} " +
                            $"cam={(cam == null ? "null" : cam.name)} " +
                            $"camOrtho={(cam == null ? false : cam.orthographic)} " +
                            $"ready={_mediaReady} media={_mediaW}x{_mediaH} fit={BackgroundStore.Fit} " +
                            $"dim={BackgroundStore.Dim} path='{System.IO.Path.GetFileName(_currentPath)}' " +
                            $"fail='{_lastFailReason}' hidden={_hiddenRenderers.Count}");
        }
        catch { }
    }
}
