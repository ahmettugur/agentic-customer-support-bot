# IAppDistributedLock

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Locking/IAppDistributedLock.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Locking`

## Ne işe yarar?

`IAppDistributedLock`, <summary> Dağıtık kilit için secondary port. Core concurrent state mutasyonlarını bu port üzerinden serialize eder. </summary> <summary> Verilen resource key için kilit almayı dener. Kilit alınırsa IAsyncDisposable handle döner; dispose edildiğinde release olur. Timeout içinde alınamazsa null döner. </summary> <summary>Kilit alana kadar bekler. Timeout aşılırsa TimeoutException fırlatır.</summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IAppDistributedLock`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
