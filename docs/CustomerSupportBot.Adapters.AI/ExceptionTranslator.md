# ExceptionTranslator

> 💡 **Analiz notu:** OpenAI/Qdrant SDK'ları HTTP 429 (rate limit), 503 (service unavailable) gibi hatalar fırlatır. Bu sınıf o teknik hataları domain'un anlayacağı `ExternalServiceException'a çevirir — Application katmanı SDK detaylarından habersiz kalır.

**Dosya:** `ExceptionTranslator.cs`  
**Tür:** `internal static class`

AI sağlayıcılarından gelen altyapı exception'larını **domain exception**'larına çevirir. Tüm adapter'lar (`GeneralChatClientAdapter`, `ReasoningChatClient`, `OpenAiEmbeddingAdapter`, `QdrantVectorMemoryAdapter`) bu translator'ı kullanır.

---

## Neden translator?

Provider SDK'ları kendi exception tipleri fırlatır:

- OpenAI: `ClientResultException` (HTTP detayları)
- Qdrant: `Grpc.Core.RpcException`
- Genel: `HttpRequestException`, `TaskCanceledException`

Application katmanı bu tiplerin hiçbirini bilmemeli. `ExceptionTranslator` arada çevirici:

```
ClientResultException (HTTP 429)
   ↓ ExceptionTranslator.Translate
ExternalServiceException("AI", "rate limit aşıldı, sonra dene")
   ↓
API katmanı → HTTP 502 + Türkçe mesaj
```

---

## `Translate`

```csharp
internal static DomainException Translate(Exception ex, string? context = null)
{
    return ex switch
    {
        ClientResultException { Status: 429 } =>
            new ExternalServiceException("AI", context ?? "AI servisine çok fazla istek gönderildi (rate limit).", ex),
        ClientResultException { Status: 401 or 403 } =>
            new ExternalServiceException("AI", context ?? "AI servisine yetkilendirme başarısız.", ex),
        ClientResultException { Status: >= 500 } =>
            new ExternalServiceException("AI", context ?? "AI servisi geçici olarak kullanılamıyor.", ex),
        ClientResultException { Status: 408 } =>
            new ExternalServiceException("AI", context ?? "AI servis isteği zaman aşımına uğradı.", ex),
        HttpRequestException =>
            new ExternalServiceException("AI", context ?? "AI servisine bağlantı kurulamadı.", ex),
        TaskCanceledException { InnerException: TimeoutException } =>
            new ExternalServiceException("AI", context ?? "AI servis isteği zaman aşımına uğradı.", ex),
        OperationCanceledException =>
            new ExternalServiceException("AI", context ?? "AI servis isteği iptal edildi.", ex),
        Grpc.Core.RpcException rpc => TranslateGrpc(rpc, context),
        _ => new ExternalServiceException("AI", context ?? "AI servisi hatası oluştu.", ex)
    };
}
```

HTTP status pattern matching `ClientResultException` üzerinde doğrudan yapılır — ayrı `TranslateClientResult` metodu yoktur.

---

## HTTP eşleme (`ClientResultException`)

OpenAI/Azure SDK her HTTP hatasında `ClientResultException` fırlatır. Status kodu doğrudan pattern matching ile eşlenir:

| HTTP status | Domain exception | Türkçe mesaj |
| --- | --- | --- |
| `429` | `ExternalServiceException("AI", ...)` | "AI servisine çok fazla istek gönderildi (rate limit)." |
| `401`, `403` | `ExternalServiceException("AI", ...)` | "AI servisine yetkilendirme başarısız." |
| `408` | `ExternalServiceException("AI", ...)` | "AI servis isteği zaman aşımına uğradı." |
| `≥500` | `ExternalServiceException("AI", ...)` | "AI servisi geçici olarak kullanılamıyor." |
| Diğer | `ExternalServiceException("AI", ...)` | "AI servisi hatası oluştu." |

**Önemli not:** Bu translator **retry yapmaz** — sadece çevirir. Retry için circuit breaker veya `Polly` middleware başka katmanda olmalı (örn. `HttpClient.AddPolicyHandler`).

### 429 (Rate limit)

OpenAI rate limit aşılınca 429 döner — `Retry-After` header'ı içerir. Bu translator header'ı kullanmaz; sadece exception fırlatır. Caller (Application veya API) ne yapacağına karar verir:

- Specialist agent: Fallback mesaj (`WellKnown.FallbackMessages.ReasoningUnavailable`)
- API: 502 döner, client retry yapabilir

### 401/403 (Auth)

Yapılandırma hatası — production'da log alarm tetiklemeli:

```
[Error] AI auth failed — API key revoked or invalid
```

Geçici hata değil; uygulama düzelmeden çalışmaz.

### 5xx (Service unavailable)

Provider tarafında problem — beklemek + retry stratejisi gerekir. Bu translator sadece exception'ı sınıflandırır.

---

## Network exception'lar

### `HttpRequestException`

TCP bağlantısı kurulamadı, DNS hatası, vb.

```csharp
HttpRequestException => new ExternalServiceException("AI",
    "Yapay zeka servisine ulaşılamıyor.", ex)
```

### `TaskCanceledException` with `TimeoutException` inner

.NET HttpClient timeout pattern'i:

```csharp
TaskCanceledException { InnerException: TimeoutException } => new ExternalServiceException("AI",
    "Yapay zeka servisi zaman aşımına uğradı.", ex)
```

**Önemli:** `OperationCanceledException` ile karıştırılmamalı:

- `TaskCanceledException` + inner `TimeoutException` → **timeout** (server cevap vermedi)
- `OperationCanceledException` cancellation token tetiklendi → **kullanıcı iptal etti**

### `OperationCanceledException`

```csharp
OperationCanceledException => new ExternalServiceException("AI",
    "İşlem iptal edildi.", ex)
```

> ⚠️ Adapter kodu genellikle bunu **translator'a göndermeden** yeniden fırlatır:
>
> ```csharp
> catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
> catch (Exception ex) { throw ExceptionTranslator.Translate(ex, "..."); }
> ```
>
> Cancellation kullanıcı iptal eyleminin doğal sonucu — exception olarak loglanmamalı.

---

## gRPC eşleme (`Grpc.Core.RpcException`)

Qdrant gRPC çağrılarında kullanılır.

```csharp
private static DomainException TranslateGrpc(Grpc.Core.RpcException rpc, string? context)
{
    return rpc.StatusCode switch
    {
        StatusCode.Unavailable      => new ExternalServiceException("Qdrant", "Vector database şu an kullanılamıyor.", rpc),
        StatusCode.DeadlineExceeded => new ExternalServiceException("Qdrant", "Vector store sorgusu zaman aşımı.", rpc),
        StatusCode.NotFound         => new EntityNotFoundException("VectorCollection", rpc.Status.Detail),
        StatusCode.AlreadyExists    => new ConcurrencyConflictException(rpc.Status.Detail, rpc),
        _                           => new ExternalServiceException("Qdrant", rpc.Status.Detail, rpc)
    };
}
```

| gRPC StatusCode | Domain Exception |
| --- | --- |
| `Unavailable` | `ExternalServiceException("Qdrant", "database unavailable")` |
| `DeadlineExceeded` | `ExternalServiceException("Qdrant", "operation timeout")` |
| `NotFound` | `EntityNotFoundException("VectorCollection")` |
| `AlreadyExists` | `ConcurrencyConflictException` |
| Diğer | `ExternalServiceException("Qdrant", detail)` |

**Service identifier "Qdrant"** kullanılır (AI değil) — log'larda kaynak ayrımı için.

---

## Service identifier konvansiyonu

`ExternalServiceException`'ın ilk parametresi servis adı:

| Servis adı | Kullanım |
| --- | --- |
| `"AI"` | Chat, embedding (OpenAI/Azure) |
| `"Qdrant"` | Vector store |
| `"Redis"` | Lock, message bus (Adapters.Redis) |
| `"Postgres"` | Persistence (Adapters.Persistence) |

API katmanı bu identifier'ı response body'ye koyar → client log/UI'da hangi servisin hata verdiğini görebilir.

---

## `internal` görünürlük

```csharp
internal static class ExceptionTranslator
```

Sadece `Adapters.AI` projesi içinden erişilebilir. Diğer adapter'lar kendi `ExceptionTranslator`'larına sahip:

- `Adapters.Persistence/ExceptionTranslator.cs` — PostgreSQL exception'ları
- `Adapters.Redis/ExceptionTranslator.cs` — StackExchange.Redis exception'ları
- `Adapters.AI/ExceptionTranslator.cs` — bu

**Neden ayrı ayrı?**

Her adapter kendi provider'ının exception ekosistemini bilir. Tek paylaşılan translator olsa:

- Tüm adapter projeleri birbirinin SDK paketine bağımlı olurdu
- "Hangi SDK exception nereye gider" kararı tek dosyaya sıkışırdı

Encapsulation prensibi: SDK detayları adapter dışına sızmaz.

---

## Kullanım deseni

```csharp
try
{
    var response = await _client.GetResponseAsync(messages, options, ct);
    return response.Text ?? string.Empty;
}
catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
catch (Exception ex)
{
    throw ExceptionTranslator.Translate(ex, "Reasoning completion failed");
}
```

`context` parametresi log'larda hangi işlem fail oldu görmek için. Caller'a yansıyan mesaj `context` veya default Türkçe mesaj.

---

## Test edilebilirlik

Translator saf static — birim test kolay:

```csharp
var ex = new ClientResultException("rate limit", new MockResponse(429));
var domainEx = ExceptionTranslator.Translate(ex, "Chat call");

Assert.IsType<ExternalServiceException>(domainEx);
Assert.Equal("AI", ((ExternalServiceException)domainEx).ServiceName);
Assert.Contains("rate limit", domainEx.Message);
```

---

## Bağlantılar

- [Domain Exceptions.md](../domain/Exceptions.md) — ExternalServiceException, EntityNotFoundException, ConcurrencyConflictException
- [Adapters.Persistence ExceptionTranslator](../adapters-persistence/ExceptionTranslator.md) — PostgreSQL paralel pattern
- [Adapters.Redis HealthCheck.md](../adapters-redis/HealthCheck.md) — Redis ExceptionTranslator
