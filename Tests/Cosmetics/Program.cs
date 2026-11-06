using System.IO.Compression;
using System.Text;
using Light.Cosmic;
using LightInDark.Core;
using Mono.Cecil;
using Mono.Cecil.Cil;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks++;
    Console.WriteLine("PASS " + name);
}
var temporary = Path.Combine(Path.GetTempPath(), "lid-cosmetics-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
BepInEx.Paths.GameRootPath = temporary;
try
{
    var directory = CosmicAddonLoader.AddonsDirectory;
    Directory.CreateDirectory(directory);
    using (var input = typeof(CosmicAddonLoader).Assembly.GetManifestResourceStream("Light.Resources.CosmicAddons.halfsugarscloset.zip")!)
    using (var output = File.Create(Path.Combine(directory, "duplicate.zip"))) input.CopyTo(output);
    File.WriteAllText(Path.Combine(directory, "broken.zip"), "not a zip");
    CosmicAddonLoader.Load();
    Check(CosmicAddonLoader.Addons.Count == 1 && CosmicAddonLoader.ItemCount == 93, "built-in pack survives broken and duplicate external packs");
    var addon = CosmicAddonLoader.Addons.Single();
    Check(addon.Id == "halfsugarscloset" && addon.Name == "半糖的衣柜" && addon.Author == "曦曦", "pack attribution preserved");
    Check(addon.ItemsByCategory["Hats"].Count == 59 && addon.ItemsByCategory["Visors"].Count == 34, "all 59 hats and 34 visors load");
    var items = addon.ItemsByCategory.Values.SelectMany(x => x).ToArray();
    Check(items.Count(i => i.ColorMode == CosmicColorMode.Adaptive) == 17 && items.Count(i => i.ColorMode == CosmicColorMode.Fixed) == 76, "17 adaptive and 76 fixed-color cosmetics preserved");
    Check(items.All(i => i.PngBytes.Length > 8 && i.NosImages.Values.All(v => v.PngBytes.AsSpan(0, 8).SequenceEqual(new byte[] {137, 80, 78, 71, 13, 10, 26, 10}))), "all image bytes remain usable after archive disposal");
    Check(items.All(i => i.PixelsPerUnit == 112.875f && i.PivotX == 0.53f && i.PivotY == 0.575f), "NOS sprite size and pivot preserved");
    Check(items.All(i => i.LocalId.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')) && items.Select(i => i.FullId).Distinct().Count() == 93, "stable unique native IDs");
    Check(items.Single(i => i.DisplayName == "Glitch.Y").Author == "", "blank original author is not replaced with pack author");
    Check(LightLogger.Warnings.Count == 2, "duplicate and corrupt archives each produce a warning");
    CosmicAddonLoader.Load();
    Check(CosmicAddonLoader.ItemCount == 93 && LightLogger.Warnings.Count == 2, "load is idempotent");
    var backOnly = items.Where(i => !i.NosImages.ContainsKey("Main")).ToArray();
    Check(backOnly.Length == 6 && backOnly.All(i => NosCosmeticState.Select(i, NosAnimation.Idle, false) is { Front: null, Back: not null }), "six back-only cosmetics stay behind body");
    var asymmetric = items.First(i => i.NosImages.ContainsKey("Flip"));
    Check(NosCosmeticState.Select(asymmetric, NosAnimation.Idle, true).Front == asymmetric.NosImages["Flip"], "left-facing alternate selected");
    var climber = items.First(i => i.NosImages.ContainsKey("ClimbDown"));
    var down = NosCosmeticState.Select(climber, NosAnimation.ClimbDown, false);
    Check(down.Front == climber.NosImages["ClimbDown"] && down.Back == null, "descending ladder selects image and hides back");
    var fallback = items.First(i => i.NosImages.ContainsKey("Climb") && !i.NosImages.ContainsKey("ClimbDown"));
    Check(NosCosmeticState.Select(fallback, NosAnimation.ClimbDown, false).Front == fallback.NosImages["Climb"], "descending ladder falls back to climb image");
    var moving = items.First(i => i.NosImages.ContainsKey("MoveBack"));
    Check(NosCosmeticState.Select(moving, NosAnimation.Run, false).Back == moving.NosImages["MoveBack"], "moving back image selected");
    var vent = items.First(i => i.NosImages.ContainsKey("EnterVentBack"));
    Check(NosCosmeticState.Select(vent, NosAnimation.EnterVent, false).Back == vent.NosImages["EnterVentBack"], "vent back image selected");

    CosmicAddon? Fixture(string name, string itemJson)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            void Write(string path, byte[] data) { using var entry = zip.CreateEntry(path).Open(); entry.Write(data); }
            Write("wrapper/AddonInfo.json", Encoding.UTF8.GetBytes("{\"Id\":\"fixture_" + name + "\",\"Name\":\"Fixture\"}"));
            Write("wrapper/CosmicInfo.json", Encoding.UTF8.GetBytes("{\"Hats\":{\"hat\":" + itemJson + "}}"));
            Write("wrapper/Resources\\hats\\sample.png", items[0].PngBytes);
        }
        stream.Position = 0;
        using var reader = new ZipArchive(stream, ZipArchiveMode.Read);
        return CosmicAddonLoader.LoadArchive(reader, "fixture.zip");
    }
    var native = Fixture("native", "{\"path\":\"hats/sample.png\",\"colorMode\":\"player\",\"frames\":2,\"offset\":{\"x\":0.2,\"y\":0.3}}");
    var nativeItem = native!.ItemsByCategory["Hats"].Single();
    Check(nativeItem.ColorMode == CosmicColorMode.Player && nativeItem.NosImages.Count == 0 && nativeItem.Frames == 2 && nativeItem.PixelsPerUnit == 100f && nativeItem.OffsetX == 0.2f, "legacy native packs retain mask, frames, offset and geometry");
    Check(Fixture("missing", "{\"path\":\"hats/sample.png\",\"nosImages\":{\"Main\":\"missing.png\"}}")!.TotalItems == 0
          && CosmicAddonLoader.Get("fixture_missing/hat") == null, "missing layer rejects entire item without partial registration");

    if (args.Length > 0)
    {
        using var module = ModuleDefinition.ReadModule(args[0]);
        Check(module.Resources.Any(r => r.Name == "Light.Resources.CosmicAddons.halfsugarscloset.zip"), "release DLL embeds wardrobe ZIP");
        foreach (var name in new[] { "NosHatLateUpdatePatch", "CosmicHatParentSetHatPatch", "CosmicVisorLayerPatch", "CosmicSetPreviewPatch", "CosmicHatAddressablePatch", "CosmicVisorAddressablePatch" })
            Check(module.Types.Single(t => t.Name == name).CustomAttributes.Any(a => a.AttributeType.Name == "HarmonyPatch"), name + " has class-level HarmonyPatch");
        foreach (var name in new[] { "CosmicHatAsset", "CosmicVisorAsset" })
        {
            var type = module.Types.Single(t => t.Name == name);
            Check(type.Methods.Single(m => m.Name == "GetState").IsVirtual && type.Methods.Single(m => m.Name == "GetAsset").IsVirtual,
                  name + " provides loaded vanilla asset view");
        }
        Check(module.Types.Single(t => t.Name == "NosCosmeticDriver").Methods.Any(m => m.Name == "LateUpdate" && m.HasBody), "frame rendering hook exists in release DLL");
    }
    Console.WriteLine($"{checks} cosmetic checks passed.");
}
finally { Directory.Delete(temporary, true); }
