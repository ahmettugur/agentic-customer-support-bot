# AgentAssistService

**Dosya:** `Services/Escalation/AgentAssistService.cs`
**Tür:** `public sealed class` — [`IAgentAssistPort`](../../Ports/Inbound/IAgentAssistPort.md) uygulaması
**Namespace:** `CustomerSupportBot.Application.Services.Escalation`

## 1. Ne İşe Yarar

Bir sohbeti devralan temsilciye (admin veya Agent) tek çağrıda yardım üretir: konuşma özeti,
müşterinin şu anki talebi, düzenlenebilir bir yanıt taslağı ve LLM gerektirmeyen bağlam (duygu
durumu, müşteri profili, ilgili bilgi tabanı makaleleri, oturumun açık işleri).

## 2. Hangi Amaçla Kullanılır

Canlı sohbet panelindeki "🤖 Asistan" kartı `GET …/chat-sessions/{sid}/assist` ile çağırır.
Temsilci geçmişi baştan okumadan durumu anlar; taslağı mesaj kutusuna alıp düzenleyerek gönderir.
**Taslak hiçbir zaman otomatik gönderilmez.**

## 3. Sorumlulukları

- **Üstlendiği:** oturum + son 20 mesaj; JWT'ye bağlı müşterinin profili; bilgi tabanında müşterinin
  son 2 mesajıyla arama (en fazla 3 makale); oturumun açık eskalasyonları ve bekleyen onayları; LLM'den
  özet/talep/taslak üretip JSON'u ayrıştırmak.
- **Üstlenmediği:** mesaj gönderme, eskalasyon/onay kararı.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`ISessionManager`, `ICustomerProfileStore`, `IMemoryPort` (Knowledge), `IEscalationSink`,
`IApprovalQueue`, `IGeneralChatClient`, `IPromptRepository` (`services/agent-assist`),
`IContextSanitizer`. Uçlar: `AdminEndpoints` ve `AgentPanelEndpoints`.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

- **İstek üzerine:** LLM çağrısı her panel açılışında değil, temsilci butona bastığında yapılır (maliyet).
- **Kısmi sonuç:** LLM hatası ya da okunamayan çıktıda özet/taslak boş, `AssistError` dolu döner;
  duygu, profil, makaleler ve açık işler yine gösterilir. Vektör arama hatası yalnızca makale listesini
  boşaltır.
- **Doğru müşteri:** profil yalnızca `State.AuthenticatedCustomerId` (JWT) ile seçilir — LLM'in metinden
  çıkardığı `State.CustomerId` değil; aksi hâlde temsilciye başka bir müşterinin profili gösterilebilirdi.
  Anonim oturumda profil yoktur.
- **Prompt injection:** müşteri metni, profil, açık işler ve makaleler LLM'e `retrieved_data` bloklarında
  **veri** olarak gider; prompt bu blokların içindeki talimatların uygulanmamasını söyler.
- **Taslak kuralları** (`Prompts/services/agent-assist.md`): yalnızca verilen bağlamdaki bilgiler; onay
  bekleyen işlemi tamamlanmış gibi sunmamak; kaydın başka müşteriye ait olduğunu ima etmemek; temsilci
  adına söz vermemek. Kodun ürettiği blok etiketleri (`KONUŞMA`, `MÜŞTERİ PROFİLİ`, `AÇIK İŞLER`,
  `BİLGİ TABANI`) ve JSON alanları prompt'ta aynı adlarla geçer — `PromptContractTests` kilitler.
- Model JSON'u kod bloğu (```json) içinde dönse de ayrıştırılır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Task<AgentAssistResult?> GetAssistAsync(string sessionId, CancellationToken ct)` | Oturum yoksa `null`; aksi hâlde kart verisi. |
| `PromptKey` *(const)* | `"services/agent-assist"`. |
| `ParseModelOutput(string?)` *(internal static)* | `{summary, customerRequest, suggestedReply}` ayrıştırma. |

## 7. Bağımlılıklar

Yukarıdaki sekiz port + `ILogger<AgentAssistService>`.

## Bağlantılar

- Tasarım: [`docs/superpowers/specs/2026-10-04-agent-assist-design.md`](../../../superpowers/specs/2026-10-04-agent-assist-design.md)
- Testler: `AgentAssistServiceTests`, `AgentAssistEndpointTests`, `PromptContractTests`
