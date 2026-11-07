namespace BepInEx
{
    internal static class Paths { public static string GameRootPath = ""; }
}
namespace LightInDark.Core
{
    internal static class LightLogger
    {
        public static readonly List<string> Warnings = new();
        public static void Log(string message) { }
        public static void LogWarning(string message) => Warnings.Add(message);
        public static void LogError(string message, Exception exception) => throw new Exception(message, exception);
    }
}
