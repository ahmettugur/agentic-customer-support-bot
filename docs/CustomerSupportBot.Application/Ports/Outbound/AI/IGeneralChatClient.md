# IGeneralChatClient

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/AI/IGeneralChatClient.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.AI`

## Ne işe yarar?

`IGeneralChatClient`, <summary> Genel LLM metin tamamlama için secondary (driven) port. Framework'e özgü IChatClient'ı core'dan gizler. </summary> <summary>Verilen mesaj listesi ile LLM'den metin yanıtı alır.</summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IGeneralChatClient`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
