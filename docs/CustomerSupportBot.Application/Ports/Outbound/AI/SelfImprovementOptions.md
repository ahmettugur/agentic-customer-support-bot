# SelfImprovementOptions

**Kaynak:** `Ports/Outbound/AI/SemanticMemoryOptions.cs` (aynı dosyada `SemanticMemoryOptions`
ile birlikte tanımlıdır — konu farklı olduğu için ayrı belgelenmiştir, bkz.
[SemanticMemoryOptions](SemanticMemoryOptions.md))
**Ayar bölümü:** `appsettings.json` → `"SelfImprovement"` (`SelfImprovementOptions.SectionName`)

## 1. Ne İşe Yarar

Self-Improving Loop'un (düşük puanlı konuşmaları tarayıp "ders" adayı çıkaran arka plan
süreci) ayarlarını taşır.

## 2. Hangi Amaçla Kullanılır

`Improvement/LessonMiner` (Application/Services/Improvement) taraması çalıştırıldığında bu
ayarları okur: kaç konuşmaya bakılacağı, hangi puanın altındakilerin aday sayılacağı, admin
onayının zorunlu olup olmadığı.

## 3. Sorumlulukları

- **Üstlendiği:** Taramanın davranışını (kapsam, eşik, onay zorunluluğu) parametrize etmek.
- **Üstlenmediği:** Taramayı tetiklemek — bu servisin işi değil, dışarıdan (admin panelinden)
  tetiklenir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Improvement/LessonMiner` ve ilgili servisler `IOptions<SelfImprovementOptions>` ile inject
eder; üretilen adaylar [`ILessonStore`](../Persistence/ILessonStore.md)'a yazılır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **Tarama yalnızca MANUELDİR** — admin panelindeki "Yeni Tarama Çalıştır" düğmesi
> (`POST /improvements/mine`) dışında tetikleyen bir şey yoktur. Zamanlanmış bir background
> service bulunmuyor; bu bilinçli bir tercih, çünkü otomatik tarama hem LLM maliyeti üretir
> hem de kimsenin bakmadığı bir onay kuyruğu biriktirir.
>
> Not: Burada eskiden bir `MiningIntervalHours = 24` ayarı vardı ama onu okuyan hiçbir kod
> yoktu — "günde bir otomatik taranır" izlenimi veren ölü bir ayardı, kaldırıldı. Zamanlanmış
> tarama istenirse önce bir hosted service yazılmalı, ayar ondan sonra geri eklenmelidir.

## 6. Metotlar / Üyeler

| Üye | Varsayılan | Açıklama |
|---|---|---|
| `bool Enabled` | `true` | Self-improving loop tümden kapatılabilir. |
| `int MinRatingForLesson` | `3` | Bu yıldız sayısının ALTINDAKİ konuşmalar ders adayı olur. |
| `int RecentTracesToScan` | `50` | Tek taramada bakılacak trace sayısı. |
| `bool RequireApprovalBeforeActivation` | `true` | Üretilen ders admin onayı olmadan devreye giremez. |

## 7. Bağımlılıklar

Yok — saf options sınıfı.
