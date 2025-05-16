using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RemoteHealthWatch.ServiceDefaults.Auth;

namespace RemoteHealthWatch.Gateway.Composition;

public sealed record PatientOverview(
    Guid PatientId,
    JsonElement? Patient,
    JsonElement? LatestVitals,
    JsonElement? Summary24h,
    JsonElement? OpenAlerts,
    JsonElement? Risk,
    JsonElement? Devices,
    IReadOnlyList<string> Degraded);

/// <summary>API composition: one call for the patient detail page, fanned out to four services in parallel.</summary>
[ApiController]
[Route("api/patients")]
[Authorize(Policy = Policies.Staff)]
public sealed class OverviewController(DownstreamClient downstream) : ControllerBase
{
    [HttpGet("{patientId:guid}/overview")]
    public async Task<ActionResult<PatientOverview>> Overview(Guid patientId, CancellationToken ct)
    {
        // the patient record decides access; if it says no, nothing else is returned
        var patientTask = downstream.GetAsync("patients", "patient", $"api/patients/{patientId}", ct);
        var latestTask = downstream.GetAsync("vitals", "latest", $"api/vitals/{patientId}/latest", ct);
        var summaryTask = downstream.GetAsync("vitals", "summary", $"api/vitals/{patientId}/summary?hours=24", ct);
        var alertsTask = downstream.GetAsync("alerts", "alerts", $"api/alerts/patient/{patientId}?openOnly=true", ct);
        var riskTask = downstream.GetAsync("risk", "risk", $"api/risk/{patientId}/current", ct);
        var devicesTask = downstream.GetAsync("devices", "devices", $"api/devices/patient/{patientId}", ct);
        await Task.WhenAll(patientTask, latestTask, summaryTask, alertsTask, riskTask, devicesTask);

        var patient = patientTask.Result;
        if (patient.Status is 403 or 404 or 401)
            return StatusCode(patient.Status, new ProblemDetails { Status = patient.Status, Title = patient.Status == 404 ? "Patient not found" : "Access denied" });

        var legs = new[] { patient, latestTask.Result, summaryTask.Result, alertsTask.Result, riskTask.Result, devicesTask.Result };
        // a missing risk score (404) just means the engine hasn't scored yet
        var degraded = legs.Where(l => !l.Ok && l.Status != 404).Select(l => l.Name).ToList();
        if (degraded.Count > 0) Response.Headers["X-Composition-Degraded"] = string.Join(',', degraded);

        return new PatientOverview(patientId, patient.Data, latestTask.Result.Data, summaryTask.Result.Data, alertsTask.Result.Data,
            riskTask.Result.Data, devicesTask.Result.Data, degraded);
    }
}
