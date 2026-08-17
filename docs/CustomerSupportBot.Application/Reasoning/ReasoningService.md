# ReasoningService

**Dosya:** `Services/Reasoning/ReasoningService.cs`  
**Implements:** `IReasoningPort`  
**Yaşam döngüsü:** Scoped (DI)

## 1. Ne İşe Yarar

Kullanıcı sorgusu için **yapılandırılmış reasoning** üretir. Reasoning pipeline'ın **L2 katmanı** ve orkestratörüdür — EntityVerifier, MessageBuilder, LLM çağrısı ve SanityChecker'ı sırayla çalıştırır.

## 2. Hangi Amaçla Kullanılır

`ChatPortService` her mesajda reasoning'i ilk adım olarak çağırır. Çıktı `ReasoningResult` PlanningAgent'a hint, trace'e kayıt ve session state'e sinyal olarak kullanılır.

> 💡 **Analiz notu:** Bir hakim gibi düşün — delilleri topla (EntityVerifier), dosyayı hazırla (MessageBuilder), düşün ve karar ver (LLM), kararı kontrol et (SanityChecker).

## 3. Sorumlulukları

- ✅ Reasoning pipeline'ı orkestre etmek (L0→L1→L2→L3)
- ✅ LLM çağrısı yapıp sonucu parse etmek
- ✅ Streaming desteği sağlamak (IAsyncEnumerable)
- ❌ Agent routing yapmak — bu PlanningAgent'ın işi

## 4. Pipeline Akışı

```
[L0] EntityVerifier.Verify()     → VerifiedEntities
[L1] MessageBuilder.Build()      → List<ConversationMessage>
[L2] LLM.CompleteAsync()         → raw text → ReasoningResultParser.Parse()
[L3] SanityChecker.Check()       → List<ReasoningIssue>
```

## 5. Metotlar

| Metot | Açıklama |
|-------|----------|
| `ReasonAsync(query, session, history, ct)` | Tam reasoning pipeline çalıştırır |
| `ReasonStreamAsync(query, session, history, ct)` | Streaming reasoning (token token) |

## 7. Constructor Bağımlılıkları

```csharp
public ReasoningService(
    IReasoningChatClient reasoningClient,  // o-series LLM client
    ILogger<ReasoningService> logger,
    IPromptRepository prompts,             // Prompt şablonları
    EntityVerifier entityVerifier,          // L0 katmanı
    ReasoningSanityChecker sanityChecker    // L3 katmanı
)
```

## Bağlantılar

- [EntityVerifier.md](EntityVerifier.md) — L0 entity doğrulama
- [ReasoningMessageBuilder.md](ReasoningMessageBuilder.md) — L1 prompt hazırlığı
- [ReasoningSanityChecker.md](ReasoningSanityChecker.md) — L3 tutarsızlık kontrolü
- [../ChatPortService.md](../Chat/ChatPortService.md) — Bu servisi çağıran orkestratör
