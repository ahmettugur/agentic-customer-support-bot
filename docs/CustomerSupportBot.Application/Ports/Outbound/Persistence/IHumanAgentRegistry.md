# IHumanAgentRegistry

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Persistence/IHumanAgentRegistry.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Persistence`

## Ne işe yarar?

`IHumanAgentRegistry`, <summary> Müşteri temsilcisi kaydı ve yük yönetimi için secondary port. </summary> <summary>Tüm kayıtlı temsilciler (admin UI için).</summary> <summary>Sadece aktif temsilciler (router için).</summary> <summary>Var olanı günceller (kısmi update — null olmayan alanlar uygulanır).</summary> <summary>Temsilciyi siler — bağlı eskalasyonlar dokunulmaz.</summary> <summary>Atamadan sonra current load'u +1 yapar ve LastAssignedAt'i günceller.</summary> <summary>Eskalasyon kapanınca current load'u -1 yapar (min 0).</summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IHumanAgentRegistry`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
