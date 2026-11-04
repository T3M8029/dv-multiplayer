using DV.Logic.Job;
using HarmonyLib;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Packets.Clientbound.Jobs;
using Multiplayer.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.Jobs;
using UnityEngine;

namespace Multiplayer.Components.Networking.Jobs;

public class NetworkedTask : IdMonoBehaviour<ushort, NetworkedTask>
{
    #region Lookup Cache
    private static readonly Dictionary<Task, NetworkedTask> taskToNetworkedTask = [];

    public static bool TryGet(ushort netId, out NetworkedTask networkedTask)
    {
        bool b = Get(netId, out IdMonoBehaviour<ushort, NetworkedTask> rawObj);
        networkedTask = (NetworkedTask)rawObj;
        return b;
    }

    public static bool TryGet(Task task, out NetworkedTask networkedTask)
    {
        return taskToNetworkedTask.TryGetValue(task, out networkedTask);
    }

    public static bool TryGet(ushort netId, out Task task)
    {
        task = null;

        if (!Get(netId, out IdMonoBehaviour<ushort, NetworkedTask> rawObj) || rawObj == null)
            return false;

        task = ((NetworkedTask)rawObj).Task;

        return task != null;
    }

    public static bool TryGetNetId(Task task, out ushort netId)
    {
        if (taskToNetworkedTask.TryGetValue(task, out var networkedTask) && networkedTask != null)
        {
            netId = networkedTask.NetId;
            return true;
        }

        netId = 0;
        return false;
    }
    #endregion

    public static readonly Queue<ClientboundTaskUpdatePacket> ClientboundTaskUpdateQueue = new();
    public static bool taskUpdateProcessing = false;

    protected override bool IsIdServerAuthoritative => true;

    public Task Task { get; private set; }

    private float lastStartTime;
    private float lastFinishTime;
    private TaskState lastState;

    public void Initialize(Task task, ushort netId = 0)
    {
        if (task == null)
        {
            Multiplayer.LogError($"NetworkedTask.Initialize(): Task is null\r\n{Environment.StackTrace}");
            return;
        }

        if (taskToNetworkedTask.ContainsKey(task))
        {
            Multiplayer.LogError($"NetworkedTask.Initialize(): Task {task.InstanceTaskType} for jobId {task.Job.ID} is already registered");
            Destroy(this);
            return;
        }

        Task = task;
        taskToNetworkedTask[Task] = this;
        if (netId != 0)
            NetId = netId;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        if (Task != null)
            taskToNetworkedTask.Remove(Task);
    }

    public static void DoOnActualTask(Task task, Action<Task> action)
    {
        if (task is ParallelTasks || task is SequentialTasks)
        {
            Traverse.Create(task)
                .Field("tasks")
                .GetValue<IEnumerable<Task>>()
                .Do(t => DoOnActualTask(t, action));
        }
        else
        {
            action(task);
        }
    }

    public void SetState(TaskState newState)
    {
        if ((lastState == newState && lastStartTime == Task.taskStartTime && lastFinishTime == Task.taskFinishTime) || !NetworkedJob.TryGetNetId(Task.Job, out var jobNetId))
            return;

        lastState = newState;
        lastStartTime = Task.taskStartTime;
        lastFinishTime = Task.taskFinishTime;

        NetworkLifecycle.Instance.Server.SendTaskUpdate(jobNetId, NetId, newState, Task.taskStartTime, Task.taskFinishTime);
    }

    public void UpdateDestinationTrack()
    {
        var destTrackOrNull = Traverse.Create(Task).Field("destinationTrack").GetValue<Track>();
        if (destTrackOrNull != null && NetworkedJob.TryGetNetId(Task.Job, out var jobNetId)) NetworkLifecycle.Instance.Server.SendTaskDestTrackUpdate(jobNetId, NetId, destTrackOrNull);
    }

    public void UpdateCar(Car car)
    {
        if (NetworkedTrainCar.TryGetNetId(car, out var carNetId) && NetworkedJob.TryGetNetId(Task.Job, out var jobNetId)) NetworkLifecycle.Instance.Server.SendTaskCarUpdate(jobNetId, NetId, carNetId);
    }

    public static IEnumerator ProcessClientboundTaskUpdatePackets()
    {
        try
        {
            while (ClientboundTaskUpdateQueue.Count > 0)
            {
                ClientboundTaskUpdatePacket packet = ClientboundTaskUpdateQueue.Dequeue();
                bool jobExists = NetworkedJob.Get(packet.JobNetId, out NetworkedJob netJob);
                Multiplayer.Log(() => $"Processing ClientboundTaskUpdatePacket for task with id {packet.TaskNetId} of job [{netJob?.Job.ID ?? "!unknown ID!"}, {packet.JobNetId}], {ClientboundTaskUpdateQueue.Count} other updates still in queue");

                if (!jobExists)
                {
                    NetworkedStationController.RequestSpecificJobs([packet.JobNetId], null);
                    yield return AwaitConditionOrTimeout((() => !NetworkedStationController.PendingJobs.Contains(packet.JobNetId)), 10f, 0.5f);
                }

                if ((NetworkedStationController.WaitingNSCs.Any()) || (NetworkedStationController.DelayedJobs.Contains(packet.JobNetId)))
                {
                    if (NetworkedStationController.WaitingNSCs.Any())
                    {
                        Multiplayer.LogDebug(() => $"NetworkClient.ProcessClientboundTaskUpdatePackets: Stations are waiting for new jobs, task updates will be processed later");
                        yield return NetworkedTask.AwaitConditionOrTimeout(() => !NetworkedStationController.WaitingNSCs.Any(), 10f, 0.5f, () => Multiplayer.LogWarning($"NetworkClient.ProcessClientboundTaskUpdatePackets for job with netId {packet.JobNetId} timed out waiting for waitingNSCs"));
                    }

                    if (NetworkedStationController.DelayedJobs.Contains(packet.JobNetId))
                    {
                        Multiplayer.LogDebug(() => $"NetworkClient.ProcessClientboundTaskUpdatePackets: Job with netId {packet.JobNetId} is delayed in its creation, will wait with task updating");
                        yield return NetworkedTask.AwaitConditionOrTimeout(() => !NetworkedStationController.DelayedJobs.Contains(packet.JobNetId), 10f, 0.5f, () => Multiplayer.LogWarning($"NetworkClient.ProcessClientboundTaskUpdatePackets timed out waiting for delayed job with netId {packet.JobNetId}"));
                    }
                }

                if (NetworkedJob.Get(packet.JobNetId, out netJob) && NetworkedTask.TryGet(packet.TaskNetId, out NetworkedTask netTask) && netTask != null)
                {
                    netTask.ApplyUpdate(packet);
                }
                else
                {
                    Multiplayer.LogError($"Received task update for taskNetId {packet.TaskNetId} of job [{netJob?.Job.ID ?? "!unknown ID!"}, {packet.JobNetId}] but netTask was not found!", true);
                    NetworkedStationController.PendingJobs.Remove(packet.JobNetId);
                    continue;
                }

                yield return WaitFor.EndOfFrame;
            }
        }
        finally
        {
            taskUpdateProcessing = false;
        }
    }

    public static IEnumerator AwaitConditionOrTimeout(Func<bool> condition, float timeout, float checkInterval = 0.1f, Action onTimeout = null)
    {
        var startTime = Time.unscaledTime;
        while (!condition.Invoke() && Time.unscaledTime - startTime < timeout)
        {
            yield return WaitFor.SecondsRealtime(checkInterval);
        }
        if ((onTimeout != null) && (Time.unscaledTime - startTime >= timeout)) onTimeout.Invoke();
    }

    private void ApplyUpdate(ClientboundTaskUpdatePacket packet)
    {

        if (packet.TaskStateUpdate == true)
        {
            Task.SetState(packet.NewState);
            Task.taskStartTime = packet.TaskStartTime;
            Task.taskFinishTime = packet.TaskFinishTime;
            Multiplayer.LogDebug(() => $"NetworkClient.ProcessClientboundTaskUpdatePackets: Successful task state update for task with id {packet.TaskNetId}");
        }

        if (packet.ReplaceDestTrack == true)
        {
            var track = (NetworkedRailTrack.TryGet(packet.DestTrackId, out RailTrack railTrack) ? railTrack.LogicTrack() : null);
            if (track != null) Traverse.Create(Task).Field("destinationTrack").SetValue(track);
            Multiplayer.LogDebug(() => $"NetworkClient.ProcessClientboundTaskUpdatePackets: Successful destination track update for task with id {packet.TaskNetId}");
        }

        if (packet.ReplaceCar == true)
        {
            Multiplayer.LogDebug(() => $"NetworkClient.ProcessClientboundTaskUpdatePackets: Car update for task with id {packet.TaskNetId} started");

            if (!NetworkedTrainCar.TryGet(packet.CarNetID, out Car car))
            {
                Multiplayer.LogError($"No car for netId {packet.CarNetID} found, skipping replacement!");
            }
            else
            {
                var cars = Traverse.Create(Task).Field("cars").GetValue<IList<Car>>();
                Car carToReplace = cars.FirstOrDefault(c => c.ID == car.ID);
                if (cars != null && cars.Contains(carToReplace))
                {
                    var job = Task.Job;
                    var jobToCarsDict = JobsManager.Instance.jobToJobCars;
                    if (jobToCarsDict.TryGetValue(job, out var dictCars))
                        jobToCarsDict[job] = (dictCars?.Replace(carToReplace, car).ToHashSet());

                    cars.Replace(carToReplace, car);
                    NetworkedStationController.UpdateCarPlates([packet.CarNetID], job.ID);
                    Multiplayer.LogDebug(() => $"ProcessClientboundTaskUpdatePackets() successfully updated car reference {car.ID} in {Task.GetType().Name} with netId {packet.TaskNetId} for job {Task.Job.ID}");
                }
                else
                    Multiplayer.LogWarning($"{Task.GetType().Name} with netId {packet.TaskNetId} for job {Task.Job.ID} doesn't have relevant cars!");
            }
        }
    }
}
