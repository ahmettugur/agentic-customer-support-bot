// Adapters.Persistence/EfCore/PersistenceHydrator.cs
// Startup recovery IHostedService — PostgreSQL provider aktifken uygulama açılışında
// bir restart'ın arkada bıraktığı "hiç bitmeyen" kayıtları temizler:
//   1. ApprovalQueue: Pending kayıtlardan eski olanları (TCS kayboldu) Expired yap.
//   2. ReasoningTraceStore: CompletedAt=null olan in-flight trace'leri
//      Error="terminated_by_restart" olarak kapat.
// İşlemler idempotent. Hata olursa uygulama durmaz, sadece loglanır.
//
// Demo/seed verisi (default admin, agent, Northwind ürün/müşteri verisi vb.) burada
// DEĞİL — DemoDataSeeder'da. Bu ikisi farklı sorumluluklar: biri restart sonrası veri
// bütünlüğü için gerekli, diğeri sadece boş bir ortamda hızlı başlamak için kullanışlı.

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Application.Ports.Outbound;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.EfCore;

public sealed class PersistenceHydrator : IHostedService
{
    private static readonly TimeSpan StalePendingThreshold = TimeSpan.FromSeconds(10);

    private readonly IServiceProvider _services;
    private readonly ILogger<PersistenceHydrator> _logger;

    public PersistenceHydrator(
        IServiceProvider services,
        ILogger<PersistenceHydrator> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Hydrator] Startup recovery başlıyor.");

        // Approval queue — yetim Pending'leri Expired'a çek.
        try
        {
            if (GetService<IApprovalQueue>() is PostgresApprovalQueue pgApproval)
            {
                await pgApproval.ExpirePendingOnStartupAsync(StalePendingThreshold, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Hydrator] Approval expire başarısız.");
        }

        // Reasoning trace — yarım kalmış trace'leri kapat.
        try
        {
            if (GetService<IReasoningTraceStore>() is PostgresReasoningTraceStore pgTrace)
            {
                await pgTrace.MarkInflightAsErrorOnStartupAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Hydrator] Trace recovery başarısız.");
        }

        _logger.LogInformation("[Hydrator] Startup recovery tamam.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private T? GetService<T>() =>
        (T?)_services.GetService(typeof(T));
}
