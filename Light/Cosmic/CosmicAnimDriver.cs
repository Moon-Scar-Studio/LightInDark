using System;
using Il2CppInterop.Runtime.Injection;
using LightInDark.Core;
using UnityEngine;

namespace Light.Cosmic;

/// <summary>
/// **多帧装扮的逐帧动画驱动**（JSON 的 `frames` / `frameDelay` ✓）。
///
/// ⚠️ 为什么自己写：原版动画帽子用的是 `PowerTools.SpriteAnim` + **`AnimationClip`** ✗
///    （`CosmeticsLayer.cs` 里引用了它 ✓）—— 那是美术在 Unity 里烘好的资源 ✓，
///    而我们手里只有一张横向 sprite sheet ✓ → 逐帧换 `sprite` 就够了 ✓。
///
/// ⚠️⚠️ AGENTS §11.2：**自建 MonoBehaviour 必须在静态构造函数里注册到 Il2Cpp** ✓
///    否则 `AddComponent&lt;T&gt;()` 抛 `TypeInitializationException` ✗
///    （本工程在 `LightTicker` / `BassMusicPlayer` 上各踩过一次 ✓）
/// </summary>
public class CosmicAnimDriver : MonoBehaviour
{
    static CosmicAnimDriver()
    {
        ClassInjector.RegisterTypeInIl2Cpp<CosmicAnimDriver>();
    }

    private SpriteRenderer? _target;
    private Sprite[]? _frames;
    private int _index;
    private float _timer;
    private float _delay = 0.12f;

    public CosmicAnimDriver(IntPtr ptr) : base(ptr) { }

    /// <summary>挂上（没有就加 ✓）并设置帧序列 ✓</summary>
    public static void Attach(SpriteRenderer? target, Sprite[]? frames, int frameDelayMs)
    {
        try
        {
            if (target == null || frames == null || frames.Length <= 1) return;

            var go = target.gameObject;
            if (go == null) return;

            var drv = go.GetComponent<CosmicAnimDriver>();
            if (drv == null) drv = go.AddComponent<CosmicAnimDriver>();
            if (drv == null) return;

            drv._target = target;
            drv._frames = frames;
            drv._index = 0;
            drv._timer = 0f;
            // `frameDelay` 单位按毫秒理解 ✓（JSON 里写 100 = 每帧 0.1 秒 ✓）；没写就用 0.12 秒 ✓
            drv._delay = frameDelayMs > 0 ? frameDelayMs / 1000f : 0.12f;
            target.sprite = frames[0];
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[CosmicAnim] 挂载失败：{ex.Message}");
        }
    }

    private void Update()
    {
        try
        {
            if (_target == null || _frames == null || _frames.Length <= 1) return;

            _timer += Time.deltaTime * 1000f;
            if (_timer < _delay * 1000f) return;
            _timer = 0f;

            _index = (_index + 1) % _frames.Length;
            _target.sprite = _frames[_index];
        }
        catch { }
    }
}
