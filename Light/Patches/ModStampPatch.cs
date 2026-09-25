using HarmonyLib;
using LightInDark.Core;
using Light.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Light.Patches;

[HarmonyPatch(typeof(ModManager), nameof(ModManager.ShowModStamp))]
public static class ModStampPatch
{
    public static void Postfix()
    {
        try
        {
            Dispatcher.Instance.Enqueue(() =>
            {
                var modStamp = GameObject.Find("ModStamp");
                if (modStamp == null)
                {
                    LightLogger.LogWarning("[ModStampPatch] ModStamp not found!");
                    return;
                }
                modStamp.transform.localScale = Vector3.one * 0.06f;
                var sr = modStamp.GetComponent<SpriteRenderer>();
                if (sr != null) sr.sprite = ResourceHelper.LoadSpriteFromResource("Light.Resources.ModStamp.png");
            });
        }
        catch (System.Exception)
        {
        }
    }
}