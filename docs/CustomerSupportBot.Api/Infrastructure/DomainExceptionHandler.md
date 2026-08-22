# DomainExceptionHandler

- **Kaynak:** `CustomerSupportBot.Api/Infrastructure/DomainExceptionHandler.cs`
- **Tür:** `internal sealed class : IExceptionHandler`
- **Namespace:** `CustomerSupportBot.Api.Infrastructure`

> 🐞 Bu doküman güncellendi — önceki sürümdeki HTTP kod eşlemeleri (`401`, `502`,
> `ValidationException`, `UnauthorizedDomainException`) gerçek koddaki tiplerle/kodlarla
> uyuşmuyordu. Aşağıdaki tablo `Map(DomainException)` switch ifadesinden birebir alındı.

## 1. Ne İşe Yarar

ASP.NET Core'un `IExceptionHandler` arayüzünü uygulayan, **inbound boundary**'de (istek→yanıt
yönünde) domain exception hiyerarşisini HTTP durum kodu + RFC 7807 `ProblemDetails` gövdesine
çeviren global hata yakalayıcı. Dosya başı yorumunda belirtildiği gibi, driven tarafta
[ExceptionTranslator](../../CustomerSupportBot.Adapters.Redis/ExceptionTranslator.md) gibi
adaptörlerin altyapı hatalarını domain exception'a çevirmesinin **simetriğidir** — bu handler
döngünün diğer ucunu (domain exception → HTTP) kapatır.

## 2. Hangi Amaçla Kullanılır

`Program.cs`'te `AddExceptionHandler<DomainExceptionHandler>()` ile kaydedilir,
`app.UseExceptionHandler()` middleware zincirine girer. Herhangi bir endpoint lambda'sında
fırlatılan bir `DomainException` alt tipi, istemciye ham C# istisnası ya da düz `500` olarak değil,
anlamlı bir durum koduyla ulaşır.

## 3. Sorumlulukları

- Yakaladığı istisna `DomainException` DEĞİLSE `false` döner — varsayılan (framework) 500
  handler'ı devreye girer.
- Yanıt zaten başlamışsa (`httpContext.Response.HasStarted` — ör. SSE/WebSocket akışı ortasında)
  yine `false` döner, çünkü status kodu artık değiştirilemez.
- İstisna tipine göre (durum kodu, log seviyesi, detayın istemciye açılıp açılmayacağı) üçlüsünü
  belirler (`Map`).
- `EntityNotFoundException` için `entityType`/`entityId`, `ExternalServiceException` için
  `service` gibi ek alanları `ProblemDetails.Extensions`'a ekler.
- **Üstlenmediği:** hangi durumlarda domain exception fırlatılacağı — bu, Application/Domain
  katmanlarının işi; bu sınıf yalnızca zaten fırlatılmış bir istisnayı HTTP diline çevirir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `DomainException` ve alt tipleri (`EntityNotFoundException`, `UnauthorizedSessionAccessException`,
  `ConcurrencyConflictException`, `ExternalServiceException`, `PersistenceException`) —
  `CustomerSupportBot.Domain.Exceptions`.
- `IProblemDetailsService` — ASP.NET Core'un yerleşik `ProblemDetails` yazım servisi
  (`AddProblemDetails()` ile `Program.cs`'te kaydedilir).
- `Program.cs` — `AddExceptionHandler<DomainExceptionHandler>()` + `UseExceptionHandler()`.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

- **4xx'te mesaj istemciye gösterilir, 5xx'te gösterilmez (`exposeDetail`):** 4xx hatalar
  (bulunamadı, yetkisiz, çakışma) istemcinin zaten bildiği bilgiyi (ör. "bu oturum senin değil")
  yeniden ifade eder — sızan yeni bir bilgi yoktur. 5xx hatalar ise iç altyapı bağlamı
  (`ExternalServiceException`, `PersistenceException` — hangi dış servisin/DB'nin nasıl
  başarısız olduğu) taşıyabilir; bu detay istemciye SIZDIRILMAZ, yerine sabit
  `GenericServerDetail` metni döner.
- **`UnauthorizedSessionAccessException` → `403`, `401` değil:** yorumda açıkça gerekçelendirilmiş
  — kimlik (JWT) zaten geçerlidir, sorun "bu oturum başkasına ait" olmasıdır; `401` (kimlik
  doğrulama eksik/geçersiz) yanlış semantik olurdu.
- **`ExternalServiceException`/`PersistenceException` → `503`, `502` değil:** `503 Service
  Unavailable`, "şu an isteği işleyemiyorum, daha sonra tekrar dene" anlamına gelir ve istemcinin
  yeniden deneme (retry) mantığı için daha doğru bir sinyaldir; bu iki tip aynı durum koduna ve
  aynı gizli detay davranışına eşlenir.
- **`Response.HasStarted` kontrolü:** SSE/WebSocket gibi akışlarda header'lar ve gövdenin bir
  kısmı çoktan yazılmış olabilir; bu noktadan sonra status kodu değiştirmeye çalışmak
  `InvalidOperationException` fırlatır — handler bunun yerine `false` dönüp istisnayı
  yayılmaya bırakır (çağıran taraf, ör. `ChatEndpoints`, akış içinde kendi hata event'ini yazar).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `DomainExceptionHandler(IProblemDetailsService, ILogger<DomainExceptionHandler>)` | Constructor. |
| `ValueTask<bool> TryHandleAsync(HttpContext, Exception, CancellationToken)` | `IExceptionHandler`'ın tek metodu; `DomainException` değilse veya yanıt başlamışsa `false`, aksi halde `ProblemDetails` yazıp `true` döner. |
| `Map(DomainException)` *(private static)* | İstisna tipini `(Status, LogLevel, ExposeDetail)` üçlüsüne eşler — bkz. tablo. |
| `GenericServerDetail` *(private const)* | 5xx'te istemciye gösterilen sabit, bağlam sızdırmayan mesaj. |

**`Map` eşleme tablosu:**

| İstisna Tipi | HTTP Durumu | Log Seviyesi | Detay İstemciye Açılır mı |
|---|---|---|---|
| `EntityNotFoundException` | 404 | Information | Evet |
| `UnauthorizedSessionAccessException` | 403 | Warning | Evet |
| `ConcurrencyConflictException` | 409 | Warning | Evet |
| `ExternalServiceException` | 503 | Error | Hayır |
| `PersistenceException` | 503 | Error | Hayır |
| *(diğer tüm `DomainException` alt tipleri)* | 400 | Warning | Evet |

## 7. Bağımlılıklar

| Bağımlılık | Neden |
|---|---|
| `IProblemDetailsService` | RFC 7807 uyumlu gövdeyi yazmak için. |
| `ILogger<DomainExceptionHandler>` | Her domain hatasını kod/status/path ile loglamak için. |

## Bağlantılar

- [../README.md](../README.md) — Katman indeksi
- [../../CustomerSupportBot.Domain/Exceptions](../../CustomerSupportBot.Domain/Exceptions) — istisna hiyerarşisi
