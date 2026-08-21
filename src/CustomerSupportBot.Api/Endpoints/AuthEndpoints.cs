// Endpoints/AuthEndpoints.cs
// /auth/login + /auth/refresh + /auth/logout endpoint'leri.

using CustomerSupportBot.Api.Models.Auth;
using CustomerSupportBot.Application.Ports.Inbound.Auth;

namespace CustomerSupportBot.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth").RequireRateLimiting("auth");

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

        group.MapPost("/customer/register",
            async (CustomerRegisterRequest req, ICustomerAuthService customerAuth, ITokenService tokens) =>
        {
            var (user, error) = await customerAuth.RegisterAsync(req.Email, req.Password, req.CustomerId);
            if (user is null) return Results.BadRequest(new { error });

            var auth = await tokens.IssueAsync(user);
            return Results.Ok(auth);
        })
        .AllowAnonymous();

        group.MapPost("/customer/login",
            async (CustomerLoginRequest req, ICustomerAuthService customerAuth, ITokenService tokens) =>
        {
            var user = await customerAuth.AuthenticateAsync(req.Email, req.Password);
            if (user is null) return Results.Unauthorized();

            var auth = await tokens.IssueAsync(user);
            return Results.Ok(auth);
        })
        .AllowAnonymous();

        return app;
    }
}

