# WorkflowGuardOptions

**Kaynak:** `Ports/Outbound/WorkflowGuardOptions.cs`
**Ayar bölümü:** `appsettings.json` → `"WorkflowGuards"`

## 1. Ne İşe Yarar

Workflow seviyesindeki tüm koruma (guard) parametrelerini taşır: genel timeout, tekrarlı
tool-çağrı limiti, reasoning'e giden geçmiş boyutu/timeout'u, maksimum iterasyon ve handoff
sayısı.

## 2. Hangi Amaçla Kullanılır

`WorkflowRunner` (Adapters.Agents) bu ayarları okuyarak workflow'un sonsuza kadar dönmesini,
aynı tool'u tekrar tekrar çağırmasını veya reasoning'in aşırı büyük bir bağlamla çalışmasını
engeller.

## 3. Sorumlulukları

- **Üstlendiği:** Yalnızca yapılandırma değerlerini taşımak.
- **Üstlenmediği:** Guard mantığının uygulanması — bu `WorkflowRunner`'ın işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`WorkflowRunner`, `TrimmingDeltaStreamer`, `TurnFinalizer` gibi Adapters.Agents sınıfları bu
options'ı kullanır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **`ReasoningHistoryMessages` neden var:** Workflow tarafında geçmiş özetlenip
> kırpılıyordu ama reasoning aynı korumadan yararlanmıyor, oturumun TAMAMINI modele
> gönderiyordu. Uzun oturumlarda bu üç şeyi birden büyütür: token maliyeti, gecikme ve
> modelin bağlam sınırını aşma riski — sınır aşılırsa reasoning fallback'e düşer ve tur
> sessizce kalitesizleşir. Reasoning'in işi niyet çıkarımı ve varlık takibidir; her ikisi de
> konuşmanın YAKIN geçmişine dayanır — uzak turların özeti zaten workflow bağlamında taşınır.
>
> `ReasoningTimeoutSeconds` ayrı bir alan çünkü `TimeoutSeconds` yalnızca workflow'u kapsar ve
> reasoning ondan ÖNCE çalışır — bu ayar olmadan asılı kalan bir reasoning çağrısı hiçbir
> bütçeye tabi değildi.

`MaxDuplicateToolCalls` ve `MaxHandoffsPerAgent`, LLM'in bir döngüye girip (aynı tool'u/aynı
ajana handoff'u tekrar tekrar çağırması) kaynak tüketmesini önleyen sonlu-durum korumalarıdır.

## 6. Metotlar / Üyeler

| Üye | Varsayılan | Açıklama |
|---|---|---|
| `int TimeoutSeconds` | `60` | Tüm workflow için saniye cinsinden timeout. |
| `int MaxDuplicateToolCalls` | `3` | Aynı tool + aynı parametre kombinasyonunun maksimum tekrarı. |
| `int ReasoningHistoryMessages` | `12` | Reasoning çağrısına gönderilecek en fazla geçmiş mesaj sayısı. |
| `int ReasoningTimeoutSeconds` | `45` | Reasoning çağrısı için ayrı timeout. |
| `int MaxIterations` | `20` | Maksimum workflow iterasyonu (MAF superstep). |
| `int MaxHandoffsPerAgent` | `2` | Aynı specialist agent'a yapılabilecek maksimum dinamik handoff. |

## 7. Bağımlılıklar

Yok — saf options sınıfı.
