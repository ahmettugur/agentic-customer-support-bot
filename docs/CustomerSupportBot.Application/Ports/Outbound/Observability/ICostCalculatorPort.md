# ICostCalculatorPort

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Observability/ICostCalculatorPort.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Observability`

## Ne işe yarar?

`ICostCalculatorPort`, <summary> LLM kullanım maliyeti hesaplama için secondary port. TelemetryChatClient bu port üzerinden token başına maliyet hesaplar. </summary> <summary> Verilen model + token sayısı için USD maliyeti hesaplar. </summary> <summary>Bilinen model adlarını döner (UI için).</summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ICostCalculatorPort`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
