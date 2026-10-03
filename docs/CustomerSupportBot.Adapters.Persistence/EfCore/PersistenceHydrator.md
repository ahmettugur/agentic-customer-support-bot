# PersistenceHydrator

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/EfCore/PersistenceHydrator.cs`
- **Tür:** `public sealed class : IHostedService`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.EfCore`

## Ne işe yarar?

`PersistenceHydrator`, ASP.NET Core `IHostedService` arayüzünü uygulayan; uygulama başlangıcında PostgreSQL sağlayıcısı devredeyken bir önceki çalışma döneminden kalan veya sunucu çökmesi/yeniden başlatılması (restart) sebebiyle yarım kalmış ("in-flight") trace kayıtlarını toparlayan kurtarma servisidir.

## Hangi amaçla kullanılır`?

- `CompletedAt == null` olan açık ReasoningTrace kayıtlarını `Error = "terminated_by_restart"` olarak işaretleyip kapatmak (`MarkInflightAsErrorOnStartupAsync`).
- **Tasarım Kararı (Approval Kayıtları Neden Burada Silinmez?):** Bloklamayan HITL onay modelinde onay kayıtları bir arka plan işlemini bloklamaz; admin karar verene kadar günlerce `Pending` kalabilir (`StalePendingHours`). Bu yüzden startup'ta pending onaylar silinmez; süresi geçenleri periyodik olarak [StaleApprovalSweepService](StaleApprovalSweepService.md) reddeder.

## Sorumlulukları

- **Üstlendiği:**
  - `StartAsync` döngüsünde `IReasoningTraceStore` üzerindeki yarım kalmış trace'leri kapatmak.
  - Hata oluşursa ana uygulamanın ayağa kalkmasını engellememek (idempotent ve güvenli try/catch).

## Constructor ve Başlatma Mantığı

```csharp
public PersistenceHydrator(
    IServiceProvider services,
    ILogger<PersistenceHydrator> logger)
```

### Constructor İçerisinde Yapılan İşler:
- `_services` (`IServiceProvider`): Gerekli depoları çözmek için servis sağlayıcı saklanır.
- `_logger`: Günlükleme motoru atanır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `StartAsync`
```csharp
public async Task StartAsync(CancellationToken cancellationToken)
```
- **Ne işe yarar?:** Uygulama başlarken kurtarma işlemlerini yürütür.
- **İç Mantığı:** `_services.GetService<IReasoningTraceStore>()` çözülür; eğer `PostgresReasoningTraceStore` ise `await pgTrace.MarkInflightAsErrorOnStartupAsync(cancellationToken)` çağrılır. Ardından hibrit cache'ler **asenkron ısıtılır** (`WarmUpCachesAsync`): `IApprovalQueue`, `IEscalationSink`, `IChatModeRegistry`, `IRatingStore`, `ICustomerProfileStore`, `ILessonStore`, `ISlaEventSink`, `IReasoningTraceStore` — implementasyonu `ICacheWarmup` ise `WarmUpAsync` beklenir.
- **🐞 Neden ısıtma:** Bu adaptörlerin okuma uçları senkron port arayüzlerinin arkasındadır; ısıtılmamış bir cache'in ilk okuması hydrate'i `GetAwaiter().GetResult()` ile bekler ve o isteğin thread'ini DB okuması boyunca bloklar. Isıtma trace kurtarmasından SONRA yapılır ki trace cache'i kapatılmış hâli görsün. Isıtma başarısız olursa loglanır; o cache ilk okumada eskisi gibi senkron hydrate eder (bkz. `PersistenceHydratorWarmupTests`).

## Bağımlılıklar

- `Microsoft.Extensions.Hosting.IHostedService`
- [PostgresReasoningTraceStore](../Postgres/PostgresReasoningTraceStore.md)
