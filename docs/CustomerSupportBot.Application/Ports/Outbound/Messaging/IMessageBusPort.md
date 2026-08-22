# IMessageBusPort

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Messaging/IMessageBusPort.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Messaging`

## Ne işe yarar?

`IMessageBusPort`, <summary> Yatay ölçeklendirme için pod'lar arası pub/sub mesaj yolu. Persistence adaptörleri bu port üzerinden diğer pod'lara bildirim yapar. </summary> <summary>Belirtilen kanala JSON payload yayınlar.</summary> <summary>Belirtilen kanalı dinler; mesaj geldiğinde handler çağrılır.</summary> <summary>Bu node'un benzersiz kimliği. Kendi mesajlarını filtrelemek için kullanılır.</summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IMessageBusPort`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
