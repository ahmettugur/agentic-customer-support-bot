// Endpoints/AuthEndpoints.cs
// /auth/login + /auth/refresh + /auth/logout endpoint'leri.

using CustomerSupportBot.Api.Models.Auth;
using CustomerSupportBot.Application.Ports.Driving.Auth;

namespace CustomerSupportBot.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest req, IUserService users, ITokenService tokens) =>
        {
            var user = await users.AuthenticateAsync(req.Username, req.Password);
            if (user is null) return Results.Unauthorized();
            var auth = await tokens.IssueAsync(user);
            return Results.Ok(auth);
        })
        .AllowAnonymous();

        group.MapPost("/refresh", async (RefreshRequest req, ITokenService tokens) =>
        {
            var auth = await tokens.RefreshAsync(req.RefreshToken);
            return auth is null ? Results.Unauthorized() : Results.Ok(auth);
        })
        .AllowAnonymous();

        group.MapPost("/logout", async (LogoutRequest req, ITokenService tokens) =>
        {
            var ok = await tokens.RevokeAsync(req.RefreshToken);
            return ok ? Results.NoContent() : Results.NotFound();
        })
        .RequireAuthorization();

        return app;
    }
}

