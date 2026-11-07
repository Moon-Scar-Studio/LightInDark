using System;
using Il2CppInterop.Runtime.Injection;
using Innersloth.Assets;
using HarmonyLib;

namespace Light.Cosmic;

// In-memory addressable wrappers follow NOS MoreCosmic's WrappedHat/VisorAsset.
// Vanilla visibility/material/animation paths require a loaded viewAsset, even
// though these cosmetics don't have an on-disk Unity Addressables entry.
public sealed class CosmicHatAsset : AddressableAsset<HatViewData>
{
    static CosmicHatAsset() => ClassInjector.RegisterTypeInIl2Cpp<CosmicHatAsset>();
    public CosmicHatAsset(IntPtr pointer) : base(pointer) { }
    public CosmicHatAsset() : base(ClassInjector.DerivedConstructorPointer<CosmicHatAsset>())
        => ClassInjector.DerivedConstructorBody(this);
    public HatViewData View = null!;
    public override HatViewData GetAsset() => View;
    public override AssetLoadState GetState() => AssetLoadState.Success;
    public override void Unload() { }
    public override void Destroy() { }
    public override void LoadAsync(Il2CppSystem.Action? onSuccessCb = null,
        Il2CppSystem.Action? onErrorcb = null, Il2CppSystem.Action? onFinishedcb = null)
    {
        onSuccessCb?.Invoke();
        onFinishedcb?.Invoke();
    }
}

public sealed class CosmicVisorAsset : AddressableAsset<VisorViewData>
{
    static CosmicVisorAsset() => ClassInjector.RegisterTypeInIl2Cpp<CosmicVisorAsset>();
    public CosmicVisorAsset(IntPtr pointer) : base(pointer) { }
    public CosmicVisorAsset() : base(ClassInjector.DerivedConstructorPointer<CosmicVisorAsset>())
        => ClassInjector.DerivedConstructorBody(this);
    public VisorViewData View = null!;
    public override VisorViewData GetAsset() => View;
    public override AssetLoadState GetState() => AssetLoadState.Success;
    public override void Unload() { }
    public override void Destroy() { }
    public override void LoadAsync(Il2CppSystem.Action? onSuccessCb = null,
        Il2CppSystem.Action? onErrorcb = null, Il2CppSystem.Action? onFinishedcb = null)
    {
        onSuccessCb?.Invoke();
        onFinishedcb?.Invoke();
    }
}

[HarmonyPatch(typeof(HatData), nameof(HatData.CreateAddressableAsset))]
internal static class CosmicHatAddressablePatch
{
    public static bool Prefix(HatData __instance, ref AddressableAsset<HatViewData> __result)
    {
        var item = CosmicRegistry.HatItem(__instance.ProductId);
        if (item == null || item.NosImages.Count == 0) return true;
        __result = new CosmicHatAsset { View = CosmicRegistry.HatViewOf(item)! };
        return false;
    }
}

[HarmonyPatch(typeof(VisorData), nameof(VisorData.CreateAddressableAsset))]
internal static class CosmicVisorAddressablePatch
{
    public static bool Prefix(VisorData __instance, ref AddressableAsset<VisorViewData> __result)
    {
        var item = CosmicRegistry.VisorItem(__instance.ProductId);
        if (item == null || item.NosImages.Count == 0) return true;
        __result = new CosmicVisorAsset { View = CosmicRegistry.VisorViewOf(item)! };
        return false;
    }
}
