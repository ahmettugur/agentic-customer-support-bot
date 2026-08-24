# ReasoningMessageBuilder

- **Kaynak:** `Services/Reasoning/ReasoningMessageBuilder.cs`
- **Tür:** `public class`
- **Namespace:** `CustomerSupportBot.Application.Services.Reasoning`

## 1. Ne İşe Yarar

[`ReasoningService`](ReasoningService.md)'in LLM çağrısı için gönderdiği mesaj listesini
(system prompt + kısaltılmış geçmiş + kullanıcı sorgusu) kurar. Prompt şablonunu
`services/reasoning-system` (`IPromptRepository` üzerinden, `.md` dosyası olarak saklanır)
gerçek verilerle doldurur.

## 2. Hangi Amaçla Kullanılır

Reasoning LLM'inin gerçekten neyi bilmesi gerektiğini (doğrulanmış entity'ler, oturum durumu,
yakın geçmiş, admin yeniden-planla notu) tek bir yerde birleştirmek — böylece prompt formatı
tek noktadan değişebilir, `ReasoningService`'in kendisi prompt detaylarıyla uğraşmaz.

## 3. Sorumlulukları

**Üstlendiği:**
- System prompt'u `services/reasoning-system` şablonundan `STATE_INFO`/`HISTORY_NOTE`/
  `VERIFIED_ENTITIES` değişkenleriyle render etmek.
- Geçmişi `maxHistoryMessages` ile kırpmak (yalnızca en yakın N mesaj).
- Admin "Yeniden Planla" tetiklediyse (`ForceReplanNextTurn`) ek bir sistem ipucu eklemek.

**Üstlenmediği:** Entity doğrulama ([`EntityVerifier`](EntityVerifier.md)'ın işi), prompt
şablonunun içeriği (`.md` prompt dosyasının işi — `src/CustomerSupportBot.Api/Prompts/services/reasoning-system.md`).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IPromptRepository` — prompt şablonunu okuyup değişken enjeksiyonuyla render eder.
- `EntityVerifier.BuildPromptBlock(verified)` — `[RESOLVED ENTITIES]` bloğunun kaynağı.
- Tüketicisi: [`ReasoningService`](ReasoningService.md) — DI'dan değil, kendi constructor'ında
  `new` ile oluşturulur (bkz. o servisin dokümanı, madde 7).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

### Geçmiş neden tamamen değil sadece son N mesaj

Tamamını göndermek uzun oturumlarda token maliyetini, gecikmeyi ve bağlam sınırını aşma
riskini birlikte büyütüyordu; sınır aşılırsa reasoning fallback'e düşer ve tur sessizce
kalitesizleşir. Uzak turların özeti zaten workflow bağlamında ayrıca (`ConversationSummaryProvider`
ile) taşınır — burada tekrarlanmaz.

### `AuthenticatedCustomerId` kullanılır, `State.CustomerId` kullanılmaz

`AuthenticatedCustomerId` JWT'den gelir; `State.CustomerId` ise LLM'in kullanıcı metninden
çıkardığı, kullanıcının **"ben 1008 numaralı müşteriyim" diyerek değiştirebildiği** alandır.
Reasoning'e ikincisini vermek, ajanı yanlış kimlik üzerinden akıl yürütmeye iter — sistem
bağlamındaki `STATE_INFO` bilinçli olarak yalnızca doğrulanmış kimliği taşır.

### Admin "Yeniden Planla" ipucu neden burada okunur, temizlenmez

`ForceReplanNextTurn` bayrağı burada yalnızca **okunur** — reasoning agent'ın bunu görüp
buna göre akıl yürütmesi için. Bayrağın temizlenmesi (state güncellemesi) Planning aşamasında
yapılır; bu sınıf yalnızca okuyucu taraftır, state mutasyonu yapmaz.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Build(maxHistoryMessages, query, session, history?, verified)` | Sırasıyla: `BuildSystemPrompt` ile system mesajı → varsa son `maxHistoryMessages` geçmiş mesajı → `ForceReplanNextTurn` set ise (varsa `ReplanNote` ile birlikte) ek sistem ipucu → kullanıcı sorgusu. Tam mesaj listesini döner. |
| `BuildSystemPrompt(session, verified, hasHistory)` *(private)* | `STATE_INFO` (`CustomerId`, `Phase`, `TurnCount`), `HISTORY_NOTE` (geçmiş varsa `services/reasoning-history-note` şablonu), `VERIFIED_ENTITIES` (`EntityVerifier.BuildPromptBlock`) değişkenleriyle `services/reasoning-system` şablonunu render eder. |

## 7. Bağımlılıklar

Constructor injection ile: `IPromptRepository`.

## Bağlantılar

- [ReasoningService.md](ReasoningService.md) — tüketici
- [EntityVerifier.md](EntityVerifier.md) — `VERIFIED_ENTITIES` bloğunun kaynağı
