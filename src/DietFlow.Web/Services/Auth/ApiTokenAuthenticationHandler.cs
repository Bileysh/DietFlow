using System.Text.Encodings.Web;
using DietFlow.Web.Models.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace DietFlow.Web.Services.Auth;

public class ApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    ApiTokenService tokens, UserManager<AppUser> users, IUserClaimsPrincipalFactory<AppUser> principals)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiToken";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();

        var token = await tokens.ValidateAsync(header["Bearer ".Length..].Trim());
        if (token is null) return AuthenticateResult.Fail("Invalid or revoked API token.");

        var user = await users.FindByIdAsync(token.UserId);
        if (user is null) return AuthenticateResult.Fail("Token owner not found.");

        var principal = await principals.CreateAsync(user);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}
