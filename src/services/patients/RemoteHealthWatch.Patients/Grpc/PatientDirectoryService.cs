using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Contracts.Grpc;
using RemoteHealthWatch.Patients.Data;
using RemoteHealthWatch.Patients.Domain;
using RemoteHealthWatch.Patients.Services;
using RemoteHealthWatch.ServiceDefaults.Auth;

namespace RemoteHealthWatch.Patients.Grpc;

[Authorize(Policy = Policies.Internal)]
public sealed class PatientDirectoryService(PatientsDb db, PatientService patients, AuditLog audit) : PatientDirectory.PatientDirectoryBase
{
    public override async Task<ThresholdSet> GetThresholds(PatientRequest request, ServerCallContext context)
    {
        var id = ParseId(request.PatientId);
        var thresholds = await db.Thresholds.AsNoTracking().Where(t => t.PatientId == id).ToListAsync(context.CancellationToken);
        var set = new ThresholdSet { PatientId = request.PatientId };
        set.Thresholds.AddRange(thresholds.Select(t =>
        {
            var msg = new Threshold { Metric = t.Metric, Overridden = t.Source == ThresholdSource.Override };
            if (t.LowCritical is { } lc) msg.LowCritical = lc;
            if (t.LowWarning is { } lw) msg.LowWarning = lw;
            if (t.HighWarning is { } hw) msg.HighWarning = hw;
            if (t.HighCritical is { } hc) msg.HighCritical = hc;
            return msg;
        }));
        return set;
    }

    public override async Task<CareTeam> GetCareTeam(PatientRequest request, ServerCallContext context)
    {
        var id = ParseId(request.PatientId);
        var members = await db.CareTeamMembers.AsNoTracking().Where(m => m.PatientId == id).ToListAsync(context.CancellationToken);
        var team = new CareTeam { PatientId = request.PatientId };
        team.Members.AddRange(members.Select(m => new RemoteHealthWatch.Contracts.Grpc.CareTeamMember
        {
            UserId = m.UserId.ToString(),
            Role = m.Role,
            DisplayName = m.DisplayName,
            Email = m.Email,
            Phone = m.Phone,
            IsPrimary = m.IsPrimary
        }));
        return team;
    }

    public override async Task<MonitoringProfile> GetMonitoringProfile(PatientRequest request, ServerCallContext context)
    {
        var patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == ParseId(request.PatientId), context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "patient not found"));
        var profile = new MonitoringProfile
        {
            PatientId = request.PatientId,
            Status = patient.Status.ToString(),
            Spo2Scale = patient.SpO2Scale,
            OnSupplementalOxygen = patient.OnSupplementalOxygen,
            IntervalSeconds = patient.Plan.IntervalSeconds
        };
        profile.Conditions.AddRange(patient.Conditions);
        profile.Vitals.AddRange(patient.Plan.Vitals.Select(v => v.ToString()));
        return profile;
    }

    public override async Task<PatientIdList> GetPatientsForUser(UserRequest request, ServerCallContext context)
    {
        if (Roles.ProgrammeWide.Contains(request.Role)) return new PatientIdList { All = true };
        var userId = ParseId(request.UserId);
        var ids = await db.CareTeamMembers.AsNoTracking().Where(m => m.UserId == userId)
            .Select(m => m.PatientId).ToListAsync(context.CancellationToken);
        var list = new PatientIdList();
        list.PatientIds.AddRange(ids.Select(i => i.ToString()));
        return list;
    }

    public override async Task<AccessDecision> AuthorizeAccess(AccessRequest request, ServerCallContext context)
    {
        var patientId = ParseId(request.PatientId);
        var userId = Guid.TryParse(request.UserId, out var u) ? u : Guid.Empty;
        var patient = await patients.FindAsync(patientId, context.CancellationToken);
        var allowed = patient is not null && CareTeamAccessPolicy.CanAccess(userId, request.Role, patient.CareTeam);
        await audit.WriteAsync(userId, request.Role, patientId, request.Action, request.Resource, request.Service, allowed, context.CancellationToken);
        return new AccessDecision { Allowed = allowed, Reason = patient is null ? "unknown-patient" : allowed ? "" : "not-on-care-team" };
    }

    private static Guid ParseId(string value) => Guid.TryParse(value, out var id)
        ? id
        : throw new RpcException(new Status(StatusCode.InvalidArgument, "id must be a GUID"));
}
