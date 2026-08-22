# KnowledgeBaseStartupService

- **Dosya:** `Workers/KnowledgeBaseIngestor.cs`
- **Sınıf adı:** `KnowledgeBaseStartupService` *(dosya adıyla birebir örtüşmüyor — bkz. not aşağıda)*
- **Namespace:** `CustomerSupportBot.Api.Workers`
- **Arayüz:** `IHostedService`

## 1. Ne İşe Yarar

Uygulama açılışında bilgi tabanının (Markdown makaleler) vektör ambarına (Qdrant) taranıp
indekslenmesini tetikleyen **ince bir hosting adaptörüdür**. Gerçek chunking/değişiklik-tespiti/
orkestrasyon mantığını içermez — bunlar Application katmanındaki
`KnowledgeBaseIngestionService`'te yaşar; bu sınıf yalnızca "uygulama başladığında bunu çağır"
sorumluluğunu taşır.

## 2. Hangi Amaçla Kullanılır

ASP.NET Core `IHostedService` altyapısına kayıtlanır (`Program.cs`), uygulama `StartAsync`
aşamasında çalışır. `SemanticMemoryOptions.KnowledgeBase.AutoIngestOnStartup` `true` ise
`IMemoryPort.IngestAsync`'i çağırır.

## 3. Sorumlulukları

- `AutoIngestOnStartup` bayrağını kontrol eder; kapalıysa hiçbir şey yapmadan döner ve bunu
  loglar.
- Açıksa ingest use-case'ini tetikler ve tamamlanmasını (`await`) bekler — yani ingest
  tamamlanana kadar `StartAsync` dönmez, uygulama bu süre boyunca "başlıyor" durumundadır.
- **Üstlenmediği:** hangi dosyaların değiştiğinin tespiti, chunking, embedding üretimi,
  Qdrant'a yazma — bunların hepsi `IMemoryPort.IngestAsync` implementasyonunun (Application
  katmanı) işidir; bu sınıf `Enabled`/değişiklik kontrolü de yapmaz, "kendisi karar versin" diye
  doğrudan çağırır.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IMemoryPort` (Application, Inbound) — `IngestAsync`'in çağrıldığı port.
- `SemanticMemoryOptions` (`IOptions<T>`) — `KnowledgeBase.AutoIngestOnStartup` yapılandırması.
- `Program.cs` — `builder.Services.AddHostedService<KnowledgeBaseStartupService>()` ile kaydedilir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

- **`IHostedService` (tek seferlik) tercih edildi, `BackgroundService` (sürekli döngü) değil** —
  bu iş yalnızca açılışta bir kez yapılması gereken bir işlemdir, periyodik tarama değildir (KB
  içeriği admin panelinden CRUD ile değiştiğinde zaten anlık olarak yeniden indekslenir — bkz.
  [Intelligence.md](../Endpoints/Intelligence.md) `MemoryEndpoints` makale uçları).
- **İnce adaptör deseni:** karar mantığının (ne zaman/nasıl ingest edilir) Application katmanında
  kalması, bu servisi test edilebilir/değiştirilebilir tutar — API katmanı yalnızca "ne zaman
  tetiklenir" (startup) sorusuna karşılık gelir.

> 🐞 **Dosya adı ile sınıf adı uyuşmuyor:** dosya `KnowledgeBaseIngestor.cs` olsa da içindeki
> sınıf `KnowledgeBaseStartupService` adını taşıyor. Bu muhtemelen bir yeniden adlandırma
> sırasında dosya adının güncellenmemesinden kaynaklanıyor — davranışsal bir sorun değil, ama
> dosyayı ararken kafa karıştırabilir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Task StartAsync(CancellationToken ct)` | `AutoIngestOnStartup` kapalıysa loglayıp döner; açıksa `_memory.IngestAsync(ct)`'i awaitler. |
| `Task StopAsync(CancellationToken ct)` | No-op (`Task.CompletedTask`) — durdurulacak bir arka plan işi yok. |

## 7. Bağımlılıklar

| Bağımlılık | Neden |
|---|---|
| `IMemoryPort` | Ingest use-case'ini çağırmak için. |
| `IOptions<SemanticMemoryOptions>` | `AutoIngestOnStartup` bayrağını okumak için. |
| `ILogger<KnowledgeBaseStartupService>` | Açılış davranışını loglamak için. |

## Bağlantılar

- [../README.md](../README.md) — Katman indeksi
- [SlaGuardianService](SlaGuardianService.md)
