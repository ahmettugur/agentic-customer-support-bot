# PlanningResult

**Dosya:** `Model/PlanningResult.cs`  
**Tür:** `class` (mutable)  
**İlişkili:** `RejectedAlternative` class (aynı dosyada)

## 1. Ne İşe Yarar

`PlanningAgent`'ın yapılandırılmış çıktısıdır — hangi specialist agent seçildi, neden seçildi, hangi alternatifler reddedildi, kullanıcıdan açıklama gerekiyor mu bilgilerini taşır.

## 2. Hangi Amaçla Kullanılır

Agent workflow'unda `PlanningAgent` çalıştıktan sonra bu sonuç parse edilir. `SelectedAgent` alanına göre specialist agent (OrderAgent, ProductAgent, ComplaintAgent) seçilir. `NeedsClarification=true` ise specialist çağrılmaz, kullanıcıya soru sorulur.

> 💡 **Analiz notu:** Bir hastanede triyaj görevlisi gibi düşün — hastanın semptomlarına bakıp hangi bölüme (ortopedi, dahiliye, acil) yönlendireceğine karar verir. Bu class o kararın yapılandırılmış halidir.

## 3. Sorumlulukları

- ✅ Routing kararını (hangi agent) taşımak
- ✅ Karar gerekçesini ve reddedilen alternatifleri taşımak
- ✅ Clarification ihtiyacını belirtmek
- ❌ **Intent tespiti yapmak** — intent'in tek sahibi `ReasoningService`'tir (`ReasoningResult.Intent`)

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim üretir:** `PlanningResultParser.Parse()` — LLM JSON çıktısını parse eder
- **Kim tüketir:** `CustomerSupportChatManager` (routing kararı) ve `WorkflowRunner` (clarification kontrolü)

## 5. Neden Böyle Tasarlandı (Analiz notu)

> 💡 **PlanningAgent routing-only'dir — intent tespiti YAPMAZ!** Intent `ReasoningResult.Intent`'tedir. PlanningAgent reasoning hint'indeki intent'i nihai karar kabul eder ve buna göre ajan seçer. Bu separation of concerns ilkesine uygundur.

> 💡 **AlternativesRejected** listesi neden var? Transparency (şeffaflık) için — model neden OrderAgent'ı seçip ComplaintAgent'ı reddettiğini açıklar. Debug sırasında routing hatalarını anlamak çok kolaylaşır.

## 6. Metotlar / Üyeler

### PlanningResult

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `SupportingEvidence` | `List<string>` | Kullanıcı mesajından alıntılar (routing kararının kanıtları) |
| `SelectedAgent` | `string` | Seçilen ajan adı (ör. "OrderAgent") |
| `Rationale` | `string` | Bu ajanın seçilme gerekçesi |
| `AlternativesRejected` | `List<RejectedAlternative>` | Reddedilen alternatifler |
| `NeedsClarification` | `bool` | Kullanıcıdan açıklama gerekiyor mu? |
| `ClarificationQuestion` | `string?` | Sorulacak netleştirme sorusu |
| `TaskDescription` | `string` | Seçilen agent'a verilecek görev açıklaması |

### RejectedAlternative

| Üye | Tip | Açıklama |
|-----|-----|----------|
| `Agent` | `string` | Reddedilen ajan adı |
| `Reason` | `string` | Neden reddedildi |

## 7. Constructor Bağımlılıkları

Yok — saf veri sınıfı.

## Bağlantılar

- [ReasoningResult.md](ReasoningResult.md) — PlanningAgent'a hint olarak verilen reasoning çıktısı
- [SubTask.md](SubTask.md) — Compound query'de birden fazla planning yapılması
- [../../CustomerSupportBot.Adapters.Agents/Team/PlanningAgent.md](../../CustomerSupportBot.Adapters.Agents/Team/PlanningAgent.md) — Bu sonucu üreten ajan
- [../Services/PlanningResultParser.md](../Services/PlanningResultParser.md) — JSON → PlanningResult dönüşümü
