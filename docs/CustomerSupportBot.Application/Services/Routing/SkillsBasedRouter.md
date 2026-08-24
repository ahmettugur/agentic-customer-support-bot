# SkillsBasedRouter

- **Kaynak:** `Services/Routing/SkillsBasedRouter.cs`
- **Tür:** `public class : ISkillsBasedRouter`
- **Namespace:** `CustomerSupportBot.Application.Services.Routing`

## 1. Ne İşe Yarar

Bir eskalasyon anında (canlı temsilciye devir), reasoning trace ve müşteri profilinden gerekli
skill etiketlerini çıkarır, `IHumanAgentRegistry`'deki müsait adaylar arasında en iyi skill +
dil eşleşmesine sahip temsilciyi bulur. **LLM çağırmaz** — tamamen deterministik ve hızlı
(<1ms sınıfında).

## 2. Hangi Amaçla Kullanılır

Eskalasyon olduğunda, "hangi temsilciye" sorusunu rastgele/round-robin yerine **konuya uygun**
(şikayet uzmanı şikayete, sipariş uzmanı siparişe) ve **dil uyumlu** biçimde cevaplamak; ayrıca
yük dengeleme ile aşırı yüklü temsilcileri hafifçe cezalandırmak.

## 3. Sorumlulukları

**Üstlendiği:**
- `Decide` — nihai yönlendirme kararını (`RoutingDecision`) üretmek.
- `ExtractRequiredSkills` — reasoning intent'i, hedeflenen specialist ajan adı, müşteri
  profilinin admin notu ve tercih edilen dilden gerekli skill etiketlerini türetmek.
- Skor hesaplama ve tie-break mantığı.

**Üstlenmediği:** Temsilci kaydının kendisi (`IHumanAgentRegistry`'nin işi), eskalasyonun ne
zaman tetikleneceği (Escalation servislerinin işi — bu sınıf yalnızca "kime" sorusuna cevap verir).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IHumanAgentRegistry` — müsait/aktif temsilci listesinin kaynağı.
- `RoutingOptions` — bkz. [RoutingOptions.md](RoutingOptions.md).
- Girdi: `ReasoningTrace` (Reasoning aşamasının çıktısı), `CustomerProfile` (Personalization).
- Tüketicisi: Escalation servisleri (`EscalationPolicyService` vb., Services/Escalation).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

### Skor formülü

```
skillMatch    = matchedSkills / max(requiredSkills, 1)         ∈ [0,1]
langMatch     = profile-language ∈ agent.Languages ? 1 : 0     ∈ {0,1}
loadFactor    = 1 - (currentLoad / maxLoad)                    ∈ [0,1]
priorityBoost = agent.Priority * 0.05                          (üst sınır +0.2)

score = (1 - LanguageWeight) * skillMatch + LanguageWeight * langMatch + priorityBoost
        (+ loadFactor * 0.05, LoadBalancingEnabled ise — hafif tie-break)
```

### Gerekli skill'lerin 4 kaynağı (`ExtractRequiredSkills`)

1. **Final intent** (`trace.Reasoning.Intent` — intent'in tek sahibi `ReasoningService`) →
   `RoutingOptions.IntentSkillMap` üzerinden eşlenir.
2. **Specialist ajan adı** → tematik skill: adında `"Complaint"` geçiyorsa `"complaint"`,
   `"Order"`/`"OrderPlacement"` geçiyorsa `"order"`, `"Product"` geçiyorsa `"product"`.
3. **Müşteri profilinin admin notu** → `RoutingOptions.ProfileKeywordSkillMap`'teki anahtar
   kelimelerden biri geçiyorsa ilgili skill eklenir (ör. "VIP" → "vip").
4. **Tercih edilen dil** → doğrudan bir skill etiketi olarak eklenir (`"tr"`/`"en"`).

### `requiredSkills` boşsa neden tarafsız skor (0.5)

`ScoreSkillMatch`, gerekli skill listesi boşsa `(matched=[], missing=[], score=0.5)` döner —
hiçbir aday "daha uygun" görünmesin diye kasıtlı bir tarafsız değer; aksi halde `0/0` skor
hesaplaması ya hata verir ya da yanlış bir sinyal (ör. 0 veya 1) üretirdi.

### Dil bilgisi olmayan temsilci için varsayım

`AgentSpeaksLanguage`, temsilcinin `Languages` listesi boşsa **Türkçe konuştuğunu varsayar**
(`preferredLanguage == "tr"`) — sistemin birincil dili Türkçe olduğu için, dil bilgisi
girilmemiş eski/varsayılan temsilci kayıtlarının otomatik olarak elenmesini önler.

### `MinMatchScore` altındaki sonuçlar neden yine de kaydedilir

`Decide`, en iyi skor eşiğin altındaysa `SuggestedAgentId`'yi boş bırakır ama `MatchedSkills`/
`MissingSkills`/`MatchScore`'u yine de doldurup döner — admin panelinin "otomatik atama
yapılamadı ama neden" bilgisini görebilmesi için (manuel atama gerektiğinde bağlam kaybolmaz).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Decide(trace, agentName, customerProfile)` | `Enabled=false` ise no-op `RoutingDecision` döner. Aksi halde: gerekli skill'leri çıkarır → `CurrentLoad < MaxConcurrentLoad` olan aktif adayları filtreler (hiç yoksa "müsait temsilci yok" notuyla döner) → her adayı skorlar → en yüksek skorlu adayı seçer → skor `MinMatchScore`'un altındaysa `SuggestedAgentId` boş, üstündeyse dolu bir `RoutingDecision` döner. |
| `ExtractRequiredSkills(trace, agentName, customerProfile)` | Yukarıdaki 4 kaynaktan gerekli skill `HashSet`'ini (normalize edilmiş: trim + lowercase) üretir, `List<string>` olarak döner. |
| `ScoreSkillMatch(agentSkills, requiredSkills)` *(private static)* | Eşleşen/eksik skill listelerini ve oranı hesaplar. |
| `AgentSpeaksLanguage(agent, preferredLanguage)` *(private static)* | Temsilcinin tercih edilen dili konuşup konuşmadığını kontrol eder (boş dil listesi → "tr" varsayımı). |
| `BuildNote(agent, matched, lang, required)` *(private static)* | Kararın insan-okunur açıklamasını (`RoutingDecision.Note`) üretir. |
| `AddNorm(set, tag)` *(private static)* | Skill etiketini trim + lowercase normalize ederek sete ekler. |

## 7. Bağımlılıklar

Constructor injection ile: `IHumanAgentRegistry`, `IOptions<RoutingOptions>`.

## Bağlantılar

- [RoutingOptions.md](RoutingOptions.md) — davranış ayarları
