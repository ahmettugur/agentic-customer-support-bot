# IReasoningChatClient

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/AI/IReasoningChatClient.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.AI`

## Ne işe yarar?

`IReasoningChatClient`, <summary> Reasoning model için secondary (driven) port. Framework'e özgü IChatClient ve ChatMessage tiplerini core'dan gizler. </summary> <summary>Kullanılan modelin adı.</summary> <summary>Reasoning effort seviyesi (low / medium / high).</summary> <summary>Tek seferlik reasoning tamamlama — tam metni döner.</summary> <summary>Streaming reasoning — token parçaları döner.</summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IReasoningChatClient`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
