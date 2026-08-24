# ParallelExecutionOptions

**Kaynak:** `Ports/Outbound/ParallelExecutionOptions.cs`
**Ayar bölümü:** `appsettings.json` → `"ParallelExecution"`

## 1. Ne İşe Yarar

Bileşik (compound) bir sorgudaki alt görevlerin (`SubTask`) paralel yürütme politikasını
taşır; ayrıca bir alt görevin paralel çalıştırılıp çalıştırılamayacağına karar veren
`IsReadOnly` yardımcı metodunu içerir.

## 2. Hangi Amaçla Kullanılır

Planlama sonucu birden fazla alt göreve bölündüğünde (örn. "siparişimi ve şikayetimi sorgula"),
workflow orkestrasyonu bu options'ı okuyarak hangi alt görevlerin eşzamanlı çalıştırılabileceğine
karar verir.

## 3. Sorumlulukları

- **Üstlendiği:** Paralellik parametrelerini taşımak VE bir `SubTask`'ın salt-okunur olup
  olmadığını (`IsReadOnly`) belirlemek.
- **Üstlenmediği:** Paralel yürütmenin kendisi (thread/task koordinasyonu) — bu workflow
  orkestrasyon kodunun işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`IsReadOnly`, `WellKnown.AgentNames.ReadOnly` sabit listesine bakar (Domain katmanı).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Paralel çalıştırma yalnızca **bütün tool'ları salt-okunur olan ajanlara** açılır. LLM'in
ürettiği intent, karma bir ajanın hangi tool'u gerçekten çağıracağını garanti etmez —
`OrderAgent` gibi hem okuma hem yazma tool'u taşıyan ajanlar bu nedenle HER ZAMAN sıralı
çalışır. Bu, olası bir yazma yan etkisinin (sipariş iptali gibi) yanlışlıkla başka bir paralel
alt görevle çakışmasını (örn. aynı anda okunan/yazılan veri) önler.

## 6. Metotlar / Üyeler

| Üye | Varsayılan | Açıklama |
|---|---|---|
| `bool Enabled` | `true` | Paralel sub-task çalıştırma açık mı. |
| `int MaxDegreeOfParallelism` | `4` | Aynı anda en fazla kaç yan-etkisiz alt görev. |
| `int MaxSubTasks` | `6` | Tek bir compound sorguda kabul edilen en yüksek alt görev sayısı. |
| `int TimeoutSeconds` | `180` | Tüm alt görevlerin paylaştığı uçtan uca zaman bütçesi. |
| `bool IsReadOnly(SubTask sub)` | — | Alt görevin hedef ajanı `WellKnown.AgentNames.ReadOnly` listesindeyse `true`. |

## 7. Bağımlılıklar

Port `CustomerSupportBot.Domain.Model.SubTask`/`WellKnown`'a bağımlıdır.
