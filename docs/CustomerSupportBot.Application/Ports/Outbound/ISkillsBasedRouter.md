# ISkillsBasedRouter

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/ISkillsBasedRouter.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`ISkillsBasedRouter`, <summary> Eskalasyon için en uygun insan müşteri temsilcisini öneren router. Reasoning trace + opsiyonel müşteri profili üzerinden skill gereksinimlerini çıkarır, registry'deki adaylar arasında skor hesaplar ve en iyi match'i döner. </summary> <summary> Hiç temsilci yoksa veya skor eşik altında kalırsa SuggestedAgentId null döner (eskalasyon yine kaydedilir, admin manuel atayabilir). </summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ISkillsBasedRouter`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
