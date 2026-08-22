# ISlaEventSink

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Persistence/ISlaEventSink.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Persistence`

## Ne işe yarar?

`ISlaEventSink`, <summary> SLA Guardian'ın ürettiği warn/breach olayları için secondary port. </summary> <summary>Yeni bir SLA olayı kaydeder ve event yayar.</summary> <summary>En son N olay (default 100).</summary> <summary> Belirli bir target+severity için en son ne zaman event yayınlandığını döner. Bu sayede her tarama döngüsünde tekrar tekrar event üretilmez. </summary> <summary>Yeni event eklendiğinde fire eder (UI canlı bildirim için).</summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ISlaEventSink`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
