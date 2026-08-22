# InputLimitedAgent

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/A2A/InputLimitedAgent.cs`
- **Tür:** `public sealed class : DelegatingAIAgent`
- **Namespace:** `CustomerSupportBot.Adapters.Agents.A2A`

## Ne işe yarar?

`InputLimitedAgent`, A2A protokolüyle dışarıdan gelen isteklerin karakter uzunluğunu (`MaxMessageChars`) ve parça sayısını (`MaxParts`) ajanı çalıştırmadan önce denetleyen ve sınır aşılırsa LLM'e gitmeden açıklayıcı bir bilgilendirme metni dönen güvenlik sarmalayıcısıdır.

## Hangi amaçla kullanılır`?

Dış partnerlerin çok büyük metinler veya aşırı parçalı istekler göndererek gereksiz token maliyeti ve sunucu yükü oluşturmasını engellemek amacıyla kullanılır.

## Sorumlulukları

- **Üstlendiği:**
  - `RunCoreAsync` ve `RunCoreStreamingAsync` öncesinde mesajları denetlemek (`Check`).
  - Sınır aşıldığında kullanıcı dostu hata mesajı üretmek.

## Metotlar / Üyeler

| Üye | Tür | İmza / Tanım | Açıklama |
|---|---|---|---|
| `RunCoreAsync` | Metot | `protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options = null, CancellationToken cancellationToken = default)` | Girdiyi denetler ve izin verilirse iç ajanı koşturur. |
| `RunCoreStreamingAsync` | Metot | `protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)` | Girdiyi denetler ve akış yanıtı üretir. |

## Bağımlılıklar

- `Microsoft.Agents.AI.DelegatingAIAgent`
- `CustomerSupportBot.Application.Services.A2A.A2AOptions`
