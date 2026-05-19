// Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs
// Application katmanı servis kayıtları — driving port implementasyonları.

using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Application.DependencyInjection;

/// <summary>
/// Application katmanı servislerini DI container'a kaydeder.
/// Driving port implementasyonları burada register edilir.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Application katmanı driving port implementasyonlarını ve use case servislerini kaydeder.
    /// </summary>
    public static IServiceCollection AddApplicationDrivingPorts(this IServiceCollection services)
    {
        // Driving port implementasyonları
        services.AddScoped<ISessionPort, SessionPortService>();
        services.AddScoped<IApprovalPort, ApprovalPortService>();
        services.AddScoped<IEscalationPort, EscalationPortService>();
        services.AddScoped<IAnalyticsPort, AnalyticsPortService>();

        // Use case servisleri (FakeDatabase'den port'lara geçen)
        services.AddSingleton<CustomerSupportToolsService>();

        return services;
    }
}
