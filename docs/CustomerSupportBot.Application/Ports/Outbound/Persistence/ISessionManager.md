# SessionInfo

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Persistence/ISessionManager.cs`
- **Tür:** `public  class`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Persistence`

## Ne işe yarar?

`SessionInfo`, <summary> Oturum bilgisi özeti (sidebar listesi için). </summary> <summary> Oturum ve konuşma geçmişi kalıcılığı için secondary (driven) port. Tamamen async — Postgres implementasyonu bu sayede sync-over-async (.GetAwaiter().GetResult()) blocking'e ihtiyaç duymaz. </summary> ─── Session yönetimi ─── <summary> Oturumu <b>kalıcı depodan</b> yeniden okur ve cache'i tazeleyip güncel nesneyi döner.  <para>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`SessionInfo`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Özellikler/Properties

- `SessionId` (`string`): İlgili veriyi temsil eden özellik.
- `Title` (`string`): İlgili veriyi temsil eden özellik.
- `LastActivity` (`DateTime`): İlgili veriyi temsil eden özellik.
- `MessageCount` (`int`): İlgili veriyi temsil eden özellik.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
