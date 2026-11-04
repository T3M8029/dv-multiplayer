using DV.Logic.Job;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Jobs;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using Unity.Jobs;


namespace Multiplayer.Patches.Jobs;

[HarmonyPatch(typeof(JobBooklet))]
public static class JobBooklet_Patch
{
    //[HarmonyPatch(nameof(JobBooklet.AssignJob))]
    //[HarmonyPostfix]
    //private static void AssignJob(JobBooklet __instance, Job jobToAssign)
    //{
    //    if (!NetworkedJob.TryGetFromJob(__instance.job, out NetworkedJob networkedJob))
    //    {
    //        Multiplayer.LogError($"JobBooklet.AssignJob() NetworkedJob not found for Job ID: {__instance.job?.ID}");
    //        return;
    //    }

    //    networkedJob.JobBooklet = __instance;
    //    if(networkedJob.TryGetComponent(out NetworkedItem netItem))
    //        networkedJob.ValidationItem = netItem;
    //}

    [HarmonyPatch(nameof(JobBooklet.AssignJob))]
    [HarmonyPostfix]
    private static void AssignJob_Postfix(JobBooklet __instance, Job jobToAssign)
    {
        if (!NetworkLifecycle.Instance.IsHost())
            return;

        if (!NetworkedJob.TryGetFromJob(jobToAssign, out NetworkedJob networkedJob))
        {
            Multiplayer.LogError($"JobBooklet.AssignJob_Postfix() NetworkedJob not found for Job ID: {jobToAssign.ID}");
        }
        else
        {
            NetworkedItem netItem = __instance.GetOrAddComponent<NetworkedItem>();
            netItem.Initialize(__instance, 0, false);
            networkedJob.JobBooklet = netItem;
        }
    }

    [HarmonyPatch(nameof(JobBooklet.DestroyJobBooklet))]
    [HarmonyPrefix]
    private static void DestroyJobBooklet(JobBooklet __instance)
    {
        if (__instance == null || __instance.job == null)
            return;

        if (!NetworkedJob.TryGetFromJob(__instance?.job, out NetworkedJob networkedJob))
            Multiplayer.LogError($"JobBooklet.DestroyJobBooklet() NetworkedJob not found for Job ID: {__instance?.job?.ID}");
        else
            networkedJob.JobBooklet = null;
    }
}
