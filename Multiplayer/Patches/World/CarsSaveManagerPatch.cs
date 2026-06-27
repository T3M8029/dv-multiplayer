using DV.JObjectExtstensions;
using DV.Utils;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data.Train;
using Newtonsoft.Json.Linq;
using System.Collections;
using UnityEngine;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(CarsSaveManager))]
public static class CarsSaveManager_Patch
{
    [HarmonyPatch(nameof(CarsSaveManager.Load))]
    [HarmonyPrefix]
    private static bool Load_Prefix()
    {
        if (!NetworkLifecycle.Instance.IsClientRunning || NetworkLifecycle.Instance.IsHost())
            return true;
        CarsSaveManager.DeleteAllExistingCars();
        return false;
    }

    [HarmonyPatch(nameof(CarsSaveManager.RestoreCarConnections))]
    [HarmonyPostfix]
    private static void RestoreCarConnections_Postfix(JObject carData)
    {
        TrainCar trainCarByCarGuid = SingletonBehaviour<TrainCarRegistry>.Instance.GetTrainCarByCarGuid(carData.GetString("carGuid"));
        if (WorldStreamingInit.IsLoaded && trainCarByCarGuid != null && NetworkedTrainCar.TryGetFromTrainCar(trainCarByCarGuid, out var networkedTrainCar))
        {
            NetworkLifecycle.Instance.Server.SendAbsoluteCouplingStatus(trainCarByCarGuid);
        }
            //SingletonBehaviour<CoroutineManager>.Instance.Run(SendConnectionsForRestoredCarDelayed(carData));
        }

    private static IEnumerator SendConnectionsForRestoredCarDelayed(JObject carData)
    {
        yield return WaitFor.SecondsRealtime(0.25f);

        TrainCar trainCarByCarGuid = SingletonBehaviour<TrainCarRegistry>.Instance.GetTrainCarByCarGuid(carData.GetString("carGuid"));
        if (trainCarByCarGuid != null && NetworkedTrainCar.TryGetFromTrainCar(trainCarByCarGuid, out var networkedTrainCar))
        {
            NetworkLifecycle.Instance.Server.SendAbsoluteCouplingStatus(trainCarByCarGuid);

            /*var frontOtherCoupler = trainCarByCarGuid.frontCoupler.GetAirHoseConnectedTo();
            if (frontOtherCoupler != null && (!(carData.GetBool("airHoseF") == true) && !(carData.GetBool("airCockF") == true)))
            {
                frontOtherCoupler.IsCockOpen = false;
                frontOtherCoupler.DisconnectAirHose(false);
            }

            var rearOtherCoupler = trainCarByCarGuid.frontCoupler.GetAirHoseConnectedTo();
            if (rearOtherCoupler != null && (!(carData.GetBool("airHoseR") == true) && !(carData.GetBool("airCockR") == true)))
            {
                rearOtherCoupler.IsCockOpen = false;
                rearOtherCoupler.DisconnectAirHose(false);
            }

            yield return null;

            networkedTrainCar.sendCouplers = true;
            networkedTrainCar.sendCables = true;
            networkedTrainCar.Server_SendCouplers();
            networkedTrainCar.Server_SendCables();*/
        }
    }
}
