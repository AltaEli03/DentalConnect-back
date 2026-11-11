using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DentalConnect.Tests;

public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string AuthenticationScheme = "Test";
    public const string SubjectHeader = "X-Test-Sub";
    public const string RoleHeader = "X-Test-Role";
    public const string ClinicHeader = "X-Test-Clinic";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var subject = Request.Headers[SubjectHeader].ToString();
        if (string.IsNullOrWhiteSpace(subject))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim>
        {
            new("sub", subject),
            new("clinic_id", Request.Headers[ClinicHeader].ToString())
        };
        var role = Request.Headers[RoleHeader].ToString();
        if (!string.IsNullOrWhiteSpace(role))
            claims.Add(new Claim("cognito:groups", role));

        var identity = new ClaimsIdentity(claims, AuthenticationScheme, "sub", "cognito:groups");
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), AuthenticationScheme)));
    }
}
