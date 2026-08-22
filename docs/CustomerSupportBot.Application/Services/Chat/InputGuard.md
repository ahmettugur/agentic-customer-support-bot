# InputGuard

- **Kaynak:** `CustomerSupportBot.Application/Services/Chat/InputGuard.cs`
- **Tür:** `public  class : IInputGuard`
- **Namespace:** `CustomerSupportBot.Application.Services.Chat`

## Ne işe yarar?

`InputGuard`, Application/Services/InputGuard.cs Deterministic input gate — runs BEFORE the user message reaches any LLM. Catches obvious abuse vectors that prompt-level guardrails alone cannot reliably stop: - Length DoS / token bomb - Suspicious prompt-injection / jailbreak patterns - Zero-width / RTL Unicode tricks - Excessive token-like ID enumeration (cost bomb) Returns an InputGuardResult with a verdict (Allow / Sanitize / Reject) plus reasoning. <summary>Maksimum kullanıcı mesaj uzunluğu (karakter).</summary> <summary>Maksimum tek mesajda görünebilecek ID sayısı (4+ haneli rakamsal ID).</summary> <summary>Tehlike sinyali — eşleşince mesaj reddedilir (LLM'e gitmez).</summary> <summary>Yumuşak sinyal — flag'lenir ama mesaj geçer (sanitize edilebilir).</summary> <summary>HTML/script/img injection — admin paneline veya trace store'a sızma riski.</summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`InputGuard`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `Inspect`
```csharp
public InputGuardResult Inspect(string? input)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IInputGuard`
