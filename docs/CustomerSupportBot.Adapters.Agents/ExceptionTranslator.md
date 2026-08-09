# ExceptionTranslator

**Dosya:** `CustomerSupportBot.Adapters.Agents/ExceptionTranslator.cs`  
**Tür:** `internal static class`

## Ne yapar?

MAF (Microsoft Agents Framework) veya .NET runtime'dan fırlayan ham exception'ları, uygulamanın domain katmanının anladığı `DomainException` türlerine dönüştürür.

**Neden gerekli?** Hexagonal mimaride Application ve Domain katmanları framework bağımlılıklarına sahip olmamalıdır. `OperationCanceledException` veya `HttpRequestException` gibi altyapı exception'ları domain sınırını geçmemelidir.

## `Translate` metodu

```csharp
internal static DomainException Translate(Exception ex, string? context = null)
```

| Gelen exception | Dönen DomainException |
|----------------|----------------------|
| `InvalidOperationException` (mesajında "Workflow" geçiyor) | `ExternalServiceException("AgentWorkflow", ...)` |
| `TaskCanceledException` (inner: `TimeoutException`) | `ExternalServiceException("AgentWorkflow", "...zaman aşımına uğradı")` |
| `OperationCanceledException` | `ExternalServiceException("AgentWorkflow", "...isteği iptal edildi")` |
| `HttpRequestException` | `ExternalServiceException("AgentWorkflow", "...bağlantı hatası")` |
| Diğer tüm | `ExternalServiceException("AgentWorkflow", "...hatası: {message}")` |

`context` parametresi loglama bağlamı için isteğe bağlı bir ek mesaj sağlar. Null gelirse exception mesajından otomatik oluşturulur.

## Kullanım yeri

`CustomerSupportTeam.RunAsync` içinde `WorkflowErrorEvent` yakalandığında:

```csharp
case WorkflowErrorEvent errorEvt:
    throw ExceptionTranslator.Translate(
        errorEvt.Exception ?? new InvalidOperationException("Workflow hatası"),
        "RunAsync workflow hatası.");
```

Streaming modda (`RunStreamingAsync`) ise exception fırlatılmaz; hata `StreamEvent(StreamEventTypes.Error, ...)` olarak yield edilir ve Translate çağrılmaz. `ExceptionTranslator` yalnızca non-streaming path'te kullanılır.

## Yeni exception eşlemesi eklemek

Pattern matching switch expression'ına yeni bir `case` ekleyin:

```csharp
public static DomainException Translate(Exception ex, string? context = null)
{
    return ex switch
    {
        // ... mevcut case'ler ...

        AuthenticationException =>                           // ← yeni
            new ExternalServiceException("AgentWorkflow",
                context ?? "AI servisine kimlik doğrulama başarısız.", ex),

        _ => new ExternalServiceException("AgentWorkflow",
                context ?? $"Ajan workflow hatası: {ex.Message}", ex)
    };
}
```
