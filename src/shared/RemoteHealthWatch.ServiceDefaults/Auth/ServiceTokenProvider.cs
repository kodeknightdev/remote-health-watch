using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RemoteHealthWatch.Contracts;

namespace RemoteHealthWatch.ServiceDefaults.Auth;

/// <summary>Mints short-lived tokens with the Service role for background calls that have no user behind them.</summary>
public sealed class ServiceTokenProvider(IOptions<JwtOptions> options, ServiceName service, TimeProvider clock)
{
    private readonly Lock _gate = new();
    private string? _token;
    private DateTimeOffset _expiresAt;

    public string GetToken()
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            if (_token is not null && _expiresAt - now > TimeSpan.FromMinutes(1)) return _token;

            var jwt = options.Value;
            _expiresAt = now.AddMinutes(5);
            var token = new JwtSecurityToken(jwt.Issuer, jwt.Audience,
                [
                    new Claim(JwtRegisteredClaimNames.Sub, Guid.Empty.ToString()),
                    new Claim("role", Roles.Service),
                    new Claim("name", service.Value)
                ],
                now.UtcDateTime, _expiresAt.UtcDateTime,
                new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)), SecurityAlgorithms.HmacSha256));
            _token = new JwtSecurityTokenHandler().WriteToken(token);
            return _token;
        }
    }
}

public sealed record ServiceName(string Value);

/// <summary>Forwards the caller's bearer token, or falls back to a service token for background work.</summary>
public sealed class BearerForwardingHandler(IHttpContextAccessor accessor, ServiceTokenProvider tokens) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var incoming = accessor.HttpContext?.Request.Headers.Authorization.ToString();
        request.Headers.Authorization = !string.IsNullOrEmpty(incoming) && incoming.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? AuthenticationHeaderValue.Parse(incoming)
            : new AuthenticationHeaderValue("Bearer", tokens.GetToken());
        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class ServiceTokenHandler(ServiceTokenProvider tokens) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetToken());
        return base.SendAsync(request, cancellationToken);
    }
}
