# ILlmCallPersistencePort

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Observability/ILlmCallPersistencePort.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Observability`

## Ne işe yarar?

`ILlmCallPersistencePort`, <summary> LLM çağrı kayıtlarını kalıcı depolamaya yazan secondary port. Implementasyon: PostgresLlmCallUsageSink (prod) veya no-op (InMemory/test). </summary> <summary> Tek bir LLM çağrısını persist eder. Fire-and-forget çağrılabilir. Hata durumunda caller'a exception sızdırmaz. </summary> <summary>Persist edilen LLM çağrı kaydı.</summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ILlmCallPersistencePort`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `LlmCallRecord`
```csharp
public sealed record LlmCallRecord(
    string Model,
    string Provider,
    long InputTokens,
    long OutputTokens,
    decimal CostUsd,
    double DurationMs,
    DateTime CalledAt)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
