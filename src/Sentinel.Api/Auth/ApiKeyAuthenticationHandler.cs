using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Sentinel.Api.Auth;

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    private readonly ApiKeyOptions _options;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<ApiKeyOptions> apiKeyOptions)
        : base(options, logger, encoder)
    {
        _options = apiKeyOptions.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing API key"));
        }

        if (string.IsNullOrWhiteSpace(_options.Key) || apiKeyHeader != _options.Key)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "api-client"),
            new Claim(ClaimTypes.Role, "Ingestion")
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
