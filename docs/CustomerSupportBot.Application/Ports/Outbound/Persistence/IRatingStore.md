# IRatingStore

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Persistence/IRatingStore.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Persistence`

## Ne işe yarar?

`IRatingStore`, <summary> Müşteri geri bildirimi (1-5 yıldız + yorum) için secondary port. </summary> <summary>Yeni bir değerlendirme kaydeder. Session başına tek rating.</summary> <summary>Belirtilen oturumun rating'ini döndürür. Yoksa null.</summary> <summary>Tüm rating'leri döndürür (analytics için).</summary> <summary>Son N rating'i döndürür.</summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IRatingStore`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
