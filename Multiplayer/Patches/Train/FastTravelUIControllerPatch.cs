using DV.UI;
using HarmonyLib;
using Multiplayer.Components.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Multiplayer.Patches.Player;

[HarmonyPatch(typeof(FastTravelUIController))]
public static class FastTravelUIControllerPatch
{
    [HarmonyPatch(typeof(FastTravelUIController), nameof(FastTravelUIController.RefreshInterface))]
    private static void Postfix(FastTravelUIController __instance)
    {
        // If the host is playing alone, don't disable the fast travel with loco button
        if (NetworkLifecycle.Instance.IsHost() && NetworkLifecycle.Instance.Server.PlayerCount == 1)
            return;

        if (__instance?.fastTravelWithLocoButton != null)
        {
            __instance.fastTravelWithLocoButton.ToggleInteractable(false);
        }
    }
}
