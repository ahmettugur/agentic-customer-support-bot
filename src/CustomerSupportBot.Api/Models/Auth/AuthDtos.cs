// Models/Auth/AuthDtos.cs
// HTTP katmanına özgü login/refresh/logout DTO'ları.

namespace CustomerSupportBot.Api.Models.Auth;

public sealed record LoginRequest(string Username, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record CustomerRegisterRequest(string Email, string Password, string CustomerId);

public sealed record CustomerLoginRequest(string Email, string Password);

