using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using RemoteHealthWatch.Contracts.Grpc;
using RemoteHealthWatch.ServiceDefaults.Auth;
using RemoteHealthWatch.ServiceDefaults.Messaging;

namespace RemoteHealthWatch.ServiceDefaults.Access;

public interface IPatientAccess
{
    /// <summary>Care-team check plus PHI audit entry, done by the patients service.</summary>
    Task<bool> AuthorizeAsync(ClaimsPrincipal user, Guid patientId, string action, string resource, CancellationToken ct);

    /// <summary>Patients the user may see; null means all (coordinators, admins).</summary>
    Task<IReadOnlySet<Guid>?> VisiblePatientsAsync(ClaimsPrincipal user, CancellationToken ct);
}

public sealed class GrpcPatientAccess(PatientDirectory.PatientDirectoryClient client, HybridCache cache, ServiceIdentity service) : IPatientAccess
{
    public async Task<bool> AuthorizeAsync(ClaimsPrincipal user, Guid patientId, string action, string resource, CancellationToken ct)
    {
        var decision = await client.AuthorizeAccessAsync(new AccessRequest
        {
            UserId = user.UserId().ToString(),
            Role = user.PrimaryRole(),
            PatientId = patientId.ToString(),
            Action = action,
            Resource = resource,
            Service = service.Name
        }, cancellationToken: ct);
        return decision.Allowed;
    }

    public async Task<IReadOnlySet<Guid>?> VisiblePatientsAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.SeesAllPatients()) return null;
        var userId = user.UserId();
        var ids = await cache.GetOrCreateAsync(CacheKeys.VisiblePatients(userId), async token =>
        {
            var list = await client.GetPatientsForUserAsync(new UserRequest { UserId = userId.ToString(), Role = user.PrimaryRole() },
                cancellationToken: token);
            return list.PatientIds.Select(Guid.Parse).ToArray();
        }, new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(5), LocalCacheExpiration = TimeSpan.FromMinutes(1) },
            [CacheKeys.CareTeamTag], ct);
        return ids.ToHashSet();
    }
}

public static class CacheKeys
{
    public const string CareTeamTag = "care-teams";
    public static string VisiblePatients(Guid userId) => $"visible:{userId}";
    public static string Thresholds(Guid patientId) => $"thresholds:{patientId}";
    public static string CareTeam(Guid patientId) => $"care-team:{patientId}";
    public static string Profile(Guid patientId) => $"profile:{patientId}";
}

/// <summary>Rejects the request with 403 unless the caller may see the patient named by the route value.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class PatientDataAttribute(string resource, string routeKey = "patientId") : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!Guid.TryParse(context.RouteData.Values[routeKey]?.ToString(), out var patientId))
        {
            context.Result = new BadRequestResult();
            return;
        }
        var access = context.HttpContext.RequestServices.GetRequiredService<IPatientAccess>();
        var action = HttpMethods.IsGet(context.HttpContext.Request.Method) ? "read" : "write";
        if (!await access.AuthorizeAsync(context.HttpContext.User, patientId, action, resource, context.HttpContext.RequestAborted))
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Patient is not on your care team"
            }) { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }
        await next();
    }
}
