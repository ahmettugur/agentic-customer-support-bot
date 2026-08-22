# RealtimeNativeService

- **Kaynak:** `CustomerSupportBot.Application/Services/Realtime/RealtimeNativeService.cs`
- **Tür:** `public sealed class : IRealtimeNativeBridge`
- **Namespace:** `CustomerSupportBot.Application.Services.Realtime`

## Ne işe yarar?

`RealtimeNativeService`, Application/Services/RealtimeNativeService.cs IRealtimeNativeBridge driving port'unun Application katmanı implementasyonu. Native modda model kendi karar verir ve okuma-only tool'ları çağırır. Tool dispatch iş mantığı (hangi araçlar sesli modda kullanılabilir) burada kapsüllenir. Tarayıcı kanalı IBrowserChannel'da, realtime voice transport IRealtimeVoiceTransport'ta gizlenir. <summary> Native mod realtime oturumu — model konuşur, okuma-only tool'ları doğrudan çağırır. Sipariş oluşturma / şikayet kaydı HITL gerektirdiği için bu kanalda bilinçli olarak yoktur. </summary> end_conversation aracı bu sabit üzerinden tanımlanır; adapter'daki tool adıyla tutarlı olmalı.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`RealtimeNativeService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public RealtimeNativeService(IRealtimeVoiceTransport client,
        ISessionManager sessionManager,
        CustomerSupportToolsService tools,
        IInputGuard inputGuard,
        IChatBridge chatBridge,
        CustomerIdentityHintBuilder identityHint,
        IAppDistributedLock sessionLock,
        ILogger<RealtimeNativeService> logger)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `RunAsync`
```csharp
public async Task RunAsync(
        IBrowserChannel channel, string sessionId, string? authenticatedCustomerId, CancellationToken ct)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IRealtimeNativeBridge`
