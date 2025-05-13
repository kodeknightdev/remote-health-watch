using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RemoteHealthWatch.ServiceDefaults.Access;
using RemoteHealthWatch.ServiceDefaults.Auth;

namespace RemoteHealthWatch.Realtime.Hubs;

public static class LiveGroups
{
    public const string All = "all";
    public static string Patient(Guid id) => $"patient:{id}";
}

/// <summary>Clients only ever receive pushes for patients they may see: care-team groups, or "all" for coordinators.</summary>
[Authorize(Policy = Policies.Staff)]
public sealed class LiveHub(IPatientAccess access, ILogger<LiveHub> logger) : Hub
{
    public const string Path = "/hubs/live";

    public override async Task OnConnectedAsync()
    {
        var ct = Context.ConnectionAborted;
        var visible = await access.VisiblePatientsAsync(Context.User!, ct);
        if (visible is null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, LiveGroups.All, ct);
        }
        else
        {
            foreach (var patientId in visible)
                await Groups.AddToGroupAsync(Context.ConnectionId, LiveGroups.Patient(patientId), ct);
        }
        HubMetrics.Connections.Add(1);
        logger.LogDebug("Connection {ConnectionId} joined {Count} patient groups", Context.ConnectionId, visible?.Count ?? -1);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        HubMetrics.Connections.Add(-1);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Explicit subscribe for a patient opened from search; goes through the audited access check.</summary>
    public async Task<bool> Watch(Guid patientId)
    {
        if (Context.User!.SeesAllPatients()) return true;
        if (!await access.AuthorizeAsync(Context.User!, patientId, "read", "live", Context.ConnectionAborted)) return false;
        await Groups.AddToGroupAsync(Context.ConnectionId, LiveGroups.Patient(patientId));
        return true;
    }

    public Task Unwatch(Guid patientId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, LiveGroups.Patient(patientId));
}
