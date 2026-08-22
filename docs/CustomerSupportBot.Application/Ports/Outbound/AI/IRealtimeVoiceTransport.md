# IRealtimeVoiceTransport

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/AI/IRealtimeVoiceTransport.cs`
- **Tür:** `public  interface : IAsyncDisposable`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.AI`

## Ne işe yarar?

`IRealtimeVoiceTransport`, <summary> Realtime sesli API ile WebSocket transport için secondary (driven) port. Application katmanı bu port'u çağırır; adaptör provider-specific protokolü bilir. Vendor-agnostik: OpenAI, Google veya başka bir sağlayıcı ile değiştirilebilir. </summary> <summary>Native modda kullanılan tool adları — frontend'e bilgi vermek için.</summary> <summary>Realtime WS'e bağlanır. Başarısız olursa false döner.</summary> <summary>Bridge modu session konfigürasyonunu gönderir (model otomatik yanıt vermez).</summary> <summary> Native modu session konfigürasyonunu gönderir (model kendi cevaplar + tool'lar açık). </summary> <param name="sessionContext"> Oturuma özel ek system talimatı — login'li müşterinin adı ve bugünün tarihi (bkz. <c>CustomerIdentityHintBuilder</c>). Sabit talimatların SONUNA eklenir; null/boşsa yalnızca sabit talimatlar gönderilir. Yazılı kanal bu bilgiyi mesaj listesine system mesajı olarak koyuyor — sesli kanalda mesaj listesi olmadığı için buradan geçirilir.

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IRealtimeVoiceTransport`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IAsyncDisposable`
