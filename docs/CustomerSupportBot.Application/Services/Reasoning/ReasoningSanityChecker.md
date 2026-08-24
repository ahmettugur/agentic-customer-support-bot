# ReasoningSanityChecker (+ IReasoningSanityRule ve 8 kural sınıfı)

- **Kaynak:** `Services/Reasoning/ReasoningSanityChecker.cs`
- **Namespace:** `CustomerSupportBot.Application.Services.Reasoning`

> **Not:** Bu dosya 10 tipi (1 interface + `ReasoningSanityChecker` + 8 kural sınıfı) tek `.md`
> dosyasında topluyor. Bilinçli bir sapma: her kural sınıfı 10-30 satırlık, tek bir `Apply`
> metodundan oluşan, birbirine sıkı bağlı bir **Strategy pattern** üyesidir — kaynak dosyanın
> kendisi de hepsini tek dosyada tutuyor ("KURALLAR" yorum bloğu altında). Ayrı dosyalara
> bölmek, okuyucunun her birini `ReasoningSanityChecker`'ın çalıştırma bağlamından kopararak
> okumasına yol açardı.

## 1. Ne İşe Yarar

`ReasoningSanityChecker`, reasoning LLM'in ürettiği `ReasoningResult`'u `VerifiedEntities`
(DB'den doğrulanmış gerçekler) ile karşılaştırıp mantıksal tutarsızlıkları yakalayan
**deterministik, kural tabanlı** bir denetleyicidir. **Hiçbir LLM çağrısı yapmaz** — saf
kurallardır. Her kural `IReasoningSanityRule`'ı implemente eder ve `_rules` listesinde sırayla
çalıştırılır.

## 2. Hangi Amaçla Kullanılır

LLM'in reasoning çıktısındaki iç tutarsızlıkları (örn. "yüksek güven ama kullanıcıdan bilgi
istiyor", "DB'de bulunamayan bir sipariş numarasını görmezden geliyor") **LLM'e tekrar
sormadan**, ucuz ve deterministik biçimde yakalamak. Bulunan `ReasoningIssue`'lar reasoning
trace'ine eklenir — hem gözlemlenebilirlik hem de (yüksek `Severity`'de) olası bir replan
tetikleyicisi olarak kullanılır.

## 3. Sorumlulukları

**Üstlendiği (`ReasoningSanityChecker`):** 8 kuralı sırayla çalıştırmak; bir kural hata
fırlatırsa onu yutup loglamak (`try/catch` her kural için ayrı — bir kuralın çökmesi diğerlerini
engellemez); bulunan issue'ları `Severity`'ye göre loglamak.

**Üstlenmediği:** Reasoning sonucunu düzeltmek veya yeniden üretmek — bu checker yalnızca
**tespit** eder, düzeltme/replan kararı çağırana (`ReasoningService`) aittir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- Tüketicisi: [`ReasoningService`](ReasoningService.md) — reasoning akışının bir adımı olarak çağırır.
- `VerifiedEntities` — [`EntityVerifier`](EntityVerifier.md)'ın ürettiği, DB'den doğrulanmış
  entity durumu; kuralların "gerçeklik" referansı.
- `SubTaskOrchestrator.IsCompoundQuery` — `SubTasksIgnoredRule`'un doğrudan bağımlı olduğu
  karar kapısı (bkz. 6.9).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**Strategy pattern:** Yeni bir tutarsızlık kalıbı bulundukça sadece yeni bir sınıf yazılıp
`_rules` listesine eklenir — `ReasoningSanityChecker`'ın kendisi değişmez (Open/Closed ilkesi).

**Neden LLM'e değil kurala:** Bu tutarsızlıklar (confidence/action çelişkisi, DB'de olmayan
bir ID'nin görmezden gelinmesi vb.) yapısal olarak `ReasoningResult`'un alanları arasında bir
ilişki sorunu — bunu tekrar LLM'e sormak hem maliyetli hem de aynı LLM'in aynı hatayı tekrar
yapma riskini taşır. Deterministik kural, garanti verir; LLM vermez.

## 6. Metotlar / Üyeler

### `IReasoningSanityRule` (interface)

| Üye | Açıklama |
|---|---|
| `Code` (`string`, get) | Kural kodu — `ReasoningIssue.Code` ile aynı, loglama ve testte kullanılır. |
| `Apply(result, verified, issues)` | Kuralı uygular; issue tespit edilirse `issues` listesine ekler. |

### `ReasoningSanityChecker`

| Üye | Açıklama |
|---|---|
| `Check(result, verified)` | 8 kuralı sırayla çalıştırır (her biri ayrı `try/catch` ile izole), toplam/`Error`/`Warn` sayısını loglar, `Error` seviyesindeki issue'ları ayrıca uyarı olarak loglar, tüm issue listesini döner. |

### 6.1 `OverconfidentClarificationRule` — `Code: "overconfident_clarification"`

`ConfidenceScore ≥ 0.7` iken `NextAction` bir netleştirme/soru formundaysa (`"iste"`,
`"sor"`, `"netleş"`, `"açıkla"`, `"clarif"` kelimelerinden biri) `Warn` üretir — yüksek güvenle
"bilmiyorum, sorayım" demek çelişkilidir.

### 6.2 `RedundantRequiredInfoRule` — `Code: "redundant_required_info"`

`RequiredInfo`'da, `VerifiedEntities`'te zaten mevcut olan bir alanı (sipariş no, müşteri
kimliği, şikayet no) tekrar isteyen bir madde varsa `Error` üretir — "ping-pong" riskine karşı:
kullanıcıya zaten sağladığı bilgiyi tekrar sormak.

### 6.3 `IntentActionMismatchRule` — `Code: "intent_action_mismatch"`

`Intent` ile `NextAction`'da işaret edilen ajan çelişiyorsa (ör. intent="complaint" ama
action OrderAgent/ProductInquiry'ye işaret ediyorsa) `Warn` üretir.

### 6.4 `LowConfidenceNoMissingRule` — `Code: "low_confidence_no_missing"`

`ConfidenceScore < 0.5` iken `RequiredInfo` boşsa `Info` üretir — düşük güvenin nedeni gizli
kalmış olabilir (model neyi bilmediğini açıklamamış).

### 6.5 `AssumptionHeavyStepsRule` — `Code: "assumption_based_step"`

`Steps` içinde `Grounding == "assumption"` olan her adım için ayrı `Info` üretir — kanıta değil
varsayıma dayanan adımları işaretler.

### 6.6 `OverconfidentAssumptionsRule` — `Code: "overconfident_assumptions"`

`ConfidenceScore ≥ 0.8` iken `Assumptions.Count ≥ 3` ise `Warn` üretir; önerilen düzeltme
`ConfidenceScore - 0.1 * varsayım_sayısı` formülüyle hesaplanır.

### 6.7 `NotFoundIgnoredRule` — `Code: "not_found_ignored"`

DB'de bulunamayan (`EntityVerification.NotFoundInDb`) bir `order_id`/`complaint_id` varken bu
durum ne `Assumptions`'ta ne `RequiredInfo`'da açıkça belirtilmemişse VE model zaten düşük
güvenli değilse (`ConfidenceScore ≥ 0.55`) `Error` üretir — hallucination riskine karşı en
kritik kural. **Dil-bağımsız tasarlanmıştır**: Türkçe/İngilizce anahtar kelime aramak yerine
`confidence`/`requiredInfo`/`assumptions` alanlarının yapısına bakar.

### 6.8 `SubTasksIgnoredRule` — `Code: "subtasks_ignored"`

`SubTasks.Count ≥ 2` iken `SubTaskOrchestrator.IsCompoundQuery(r)` **false** dönüyorsa (yani
decompose kapısı — 2+ alt görev VE 2+ farklı hedef ajan — açılmıyorsa) `Warn` üretir: alt
görevler bildirilmiş ama orkestratör bunları hiç yürütmeyecek, tek-runner yoluna düşüp
**tamamen yok sayılacaklar**.

> 🐞 **Kural eskiden tam tersini yapıyordu:** Aynı kapı koşulunu (`Count≥2 && agents≥2`)
> kullanıp `NextAction` tüm ajan adlarını anmıyorsa uyarı üretiyordu — yani decompose'un
> **doğru** çalıştığı tek durumda ateşleniyordu. Decompose yolunda `NextAction`'ın hiçbir
> yönlendirme etkisi yok; üstelik bu yanlış pozitif `LessonMiner`'ın ders çıkarma prompt'una
> da giriyordu. Kural mantığı tersine çevrilerek düzeltildi.

## 7. Bağımlılıklar

`ReasoningSanityChecker` constructor injection ile: `ILogger<ReasoningSanityChecker>` alır ve
8 kural sınıfını **kendi içinde `new` ile** oluşturur (DI konteynerine kayıtlı değiller —
stateless, parametresiz kurallar oldukları için gerek yok). Kural sınıflarının hiçbiri kendi
başına bir bağımlılık taşımaz (`Domain.Model` dışında).

## Bağlantılar

- [ReasoningService.md](ReasoningService.md) — tüketici
- [EntityVerifier.md](EntityVerifier.md) — `VerifiedEntities`'in üreticisi
- [SubTaskOrchestrator.md](SubTaskOrchestrator.md) — `IsCompoundQuery` kapı koşulu
