// Adapters.Persistence/EfCore/PersistenceHydrator.cs
// Startup recovery IHostedService — PostgreSQL provider aktifken uygulama açılışında
// bir restart'ın arkada bıraktığı "hiç bitmeyen" kayıtları temizler:
//   1. ReasoningTraceStore: CompletedAt=null olan in-flight trace'leri
//      Error="terminated_by_restart" olarak kapat.
//   2. Hibrit cache'leri (ICacheWarmup) ASENKRON ısıt. Isıtılmamış bir cache'in ilk senkron
//      okuması hydrate'i GetAwaiter().GetResult() ile bekler ve o isteğin thread'ini DB okuması
//      boyunca bloklar. Isıtma 1. adımdan SONRA yapılır ki trace cache'i kapatılmış hâli görsün.
// İşlemler idempotent. Hata olursa uygulama durmaz, sadece loglanır — ısıtılamayan cache ilk
// okumada eskisi gibi senkron hydrate eder.
//
// BURADA ARTIK APPROVAL TEMİZLİĞİ YOK. Eskiden 10 saniyeden eski Pending onaylar
// startup'ta Expired'a çekiliyordu; bu, onayın tool çağrısını BLOKLADIĞI modelde
// doğruydu — restart bekleyen TaskCompletionSource'u öldürdüğü için kaydın sahibi
// kalmıyordu. Bloklamayan modelde (bkz. ApprovalGateService) bekleyen yok: kayıt
// admin karar verene kadar günlerce Pending durabilir ve durmalıdır
// (ApprovalOptions.StalePendingHours, varsayılan 72 saat). O kod kalsaydı her deploy
// bekleyen tüm onayları sessizce reddederdi — 10 saniye ile 72 saat aynı kayıt için
// iki çelişen ömür tanımlıyordu. Süresi geçen kayıtları artık
// StaleApprovalSweepService periyodik olarak reddediyor.
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

        await WarmUpCachesAsync(cancellationToken);

        _logger.LogInformation("[Hydrator] Startup recovery tamam.");
    }

    /// <summary>Isıtılacak port'lar — implementasyon <see cref="ICacheWarmup"/> ise ısıtılır.</summary>
    private static readonly Type[] WarmablePorts =
    [
        typeof(IApprovalQueue),
        typeof(IEscalationSink),
        typeof(IChatModeRegistry),
        typeof(IRatingStore),
        typeof(ICustomerProfileStore),
        typeof(ILessonStore),
        typeof(ISlaEventSink),
        typeof(IReasoningTraceStore),
    ];

    private async Task WarmUpCachesAsync(CancellationToken ct)
    {
        foreach (var port in WarmablePorts)
        {
            if (_services.GetService(port) is not ICacheWarmup warmup) continue;
            try
            {
                await warmup.WarmUpAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Hydrator] Cache ısıtma başarısız: {Port}. İlk okuma senkron hydrate edecek.", port.Name);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private T? GetService<T>() =>
        (T?)_services.GetService(typeof(T));
}
