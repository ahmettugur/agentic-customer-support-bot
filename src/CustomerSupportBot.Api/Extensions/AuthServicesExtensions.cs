using System.Text;
using CustomerSupportBot.Adapters.Persistence.Auth;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Ports.Inbound.Auth;
using CustomerSupportBot.Application.Services.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using CustomerSupportBot.Application.Services.A2A;

namespace CustomerSupportBot.Api.Extensions;

public static class AuthServicesExtensions
{
    public static IServiceCollection AddAuthenticationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        services.Configure<JwtOptions>(jwtSection);
        var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();

        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<IJwtAccessTokenProvider, JwtAccessTokenProvider>();
        services.AddScoped<ITokenService, TokenPortService>();
        services.AddScoped<IUserService, Application.Services.Auth.UserService>();
        services.AddScoped<ICustomerAuthService, Application.Services.Auth.CustomerAuthService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false; // Development için
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                        string.IsNullOrWhiteSpace(jwtOptions.SigningKey)
                            ? new string('x', 32) // boşsa boot fail edecek (JwtAccessTokenProvider throw eder)
                            : jwtOptions.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                // SSE / EventSource için query string desteği
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"].ToString();
                        if (!string.IsNullOrEmpty(token)) ctx.Token = token;
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("Admin", p => p.RequireRole("Admin"));
            options.AddPolicy("Agent", p => p.RequireRole("Agent"));
            options.AddPolicy("AdminOrAgent", p => p.RequireRole("Admin", "Agent"));
            options.AddPolicy("Customer", p => p.RequireRole("Customer"));

            // ─── A2A (dış sistemlere açılan kanal) ───
            // İki ayrı rol bilinçli: partner token'ı YALNIZCA token değişimi yapabilir,
            // ajanları doğrudan çağıramaz. Ajanları çağıran özne token'ı ise tek bir müşteriye
            // kilitlidir. Böylece "hangi sistem" ile "hangi müşteri" soruları ayrı token'larda
            // taşınır ve müşteri kimliği hiçbir zaman istemcinin değiştirebildiği bir parametre olmaz.
            options.AddPolicy("Partner", p => p.RequireRole(A2ARoles.Partner));
            options.AddPolicy("A2ASubject", p => p.RequireRole(A2ARoles.Subject));
        });

        return services;
    }
}

