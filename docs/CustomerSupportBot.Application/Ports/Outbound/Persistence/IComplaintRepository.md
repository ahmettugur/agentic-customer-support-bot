# IComplaintRepository

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Persistence/IComplaintRepository.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Persistence`

## Ne işe yarar?

`IComplaintRepository`, <summary> Şikayet yönetimi için secondary port. </summary> <summary>Yeni şikayet oluşturur ve şikayet ID'sini döner.</summary> <summary>Şikayet ID ile sorgular. Bulunamazsa null döner.</summary> <summary>Sipariş ID'ye göre şikayetler.</summary> <summary>Müşteri ID'ye göre tüm şikayetler.</summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IComplaintRepository`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
