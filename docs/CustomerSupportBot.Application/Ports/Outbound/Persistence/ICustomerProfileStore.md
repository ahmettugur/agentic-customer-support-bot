# ICustomerProfileStore

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Persistence/ICustomerProfileStore.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Persistence`

## Ne işe yarar?

`ICustomerProfileStore`, <summary> Per-customer profil verisi için secondary port. </summary> <summary>Var olan profili döndürür, yoksa null.</summary> <summary>Var olan profili döner, yoksa boş bir tane oluşturup ekler.</summary> <summary>Profili upsert eder (tüm alanlar replace).</summary> <summary>Profili tamamen siler.</summary> <summary>Tüm profilleri lastInteraction azalan sırada listeler (admin UI).</summary> <summary>Toplam profil sayısı.</summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ICustomerProfileStore`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
