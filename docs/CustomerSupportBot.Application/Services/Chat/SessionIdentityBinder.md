# SessionIdentityBinder

- **Kaynak:** `CustomerSupportBot.Application/Services/Chat/SessionIdentityBinder.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Application.Services.Chat`

## Ne işe yarar?

`SessionIdentityBinder`, Application/Services/Chat/SessionIdentityBinder.cs Bir oturuma JWT-doğrulanmış müşteri kimliğini bağlar ve oturum sahipliğini doğrular. <summary> Oturum ↔ müşteri bağını kuran tek nokta.  <para> <b>Neden ayrı bir yardımcı:</b> aynı bağlama üç kanalda gerekiyor — yazılı chat, sesli köprü modu ve sesli native mod. Kural üç yere kopyalansaydı biri güncellenip diğerleri kalırdı; nitekim sesli kanallar uzun süre bu bağı hiç kurmuyordu ve sonucunda sipariş sorgulayan her tool <c>customerId=""</c> ile çalışıp "sipariş bulunamadı" dönüyordu. </para> </summary> <summary> Oturumu login'li müşteriye bağlar.  <para> Bağ <b>bir kez</b> kurulur; oturum zaten aynı müşteriye bağlıysa işlem yapılmaz. Oturum BAŞKA bir müşteriye bağlıysa <c>false</c> döner — çağıran taraf bağlantıyı reddetmelidir. Bu kontrol olmadan, bir kullanıcı başkasının <c>sessionId</c>'sini vererek o oturumun kimliğiyle çalışan tool'lara (sipariş geçmişi, iptal, iade) erişebilirdi: oturum zaten bağlı olduğu için bağlama sessizce atlanır ve tool'lar oturumdaki kimlikle koşardı.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`SessionIdentityBinder`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `TurnLockKey`
```csharp
public static string TurnLockKey(string sessionId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `BindAtomicallyAsync`
```csharp
public static async Task<AgentSession?> BindAtomicallyAsync(
        string sessionId,
        string? authenticatedCustomerId,
        ISessionManager sessions,
        IAppDistributedLock locks,
        CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `TryBindAsync`
```csharp
public static async Task<bool> TryBindAsync(
        AgentSession session,
        string? authenticatedCustomerId,
        ISessionManager sessions,
        CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `IsAccessibleAsync`
```csharp
public static async Task<bool> IsAccessibleAsync(
        string? sessionId,
        string? authenticatedCustomerId,
        ISessionManager sessions,
        CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
