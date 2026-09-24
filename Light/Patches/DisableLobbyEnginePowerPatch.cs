using LightInDark.Core;

namespace Light.Patches;

[HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Update))]
public static class DisableLobbyEnginePowerPatch
{
    public static void Postfix(LobbyBehaviour __instance)
    {
        try
        {
            if (Input.GetKeyDown(KeyCode.LeftControl))
            {
                var LeftEngine = __instance.transform.GetChild(2); // 左引擎火
                LeftEngine.gameObject.SetActive(!LeftEngine.gameObject.activeSelf);
            }
            if(Input.GetKeyDown(KeyCode.RightControl))
            {
                var RightEngine = __instance.transform.GetChild(1); // 右引擎火
                RightEngine.gameObject.SetActive(!RightEngine.gameObject.activeSelf);
            }
        }
        catch (System.Exception ex)
        {
            LightLogger.LogWarning("[Light] DisableLobbyEnginePowerPatch.Postfix NRE: " + ex.Message + "\n" + ex.StackTrace);
        }
    }
}