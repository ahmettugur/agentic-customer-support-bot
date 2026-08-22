# InMemoryReasoningTraceStore

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryReasoningTraceStore.cs`
- **Port:** `IReasoningTraceStore`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.InMemory`

## 1. Ne İşe Yarar

Her sohbet turunun akıl yürütme izini (`ReasoningTrace` — hangi ajan, hangi tool'lar, ne kadar sürdü, hangi bağlam kullanıldı) `ConcurrentDictionary<string, ReasoningTrace>` ile bellekte, ring-buffer (varsayılan kapasite 500) mantığıyla tutar.

## 2. Hangi Amaçla Kullanıldığı

`PostgresReasoningTraceStore`'un tek-process karşılığı. Debug/gözlemlenebilirlik ekranlarının (`/admin` trace görüntüleyici) veri kaynağıdır.

## 3. Sorumlulukları

- `StartTrace` — yeni trace başlatır, `_insertionOrder` kuyruğuna ekler, kapasite aşımında en eski trace'i düşürür.
- `Update` — trace zaten referans olarak sözlükte tutulduğundan pratikte no-op'a yakındır (yine de üzerine yazar).
- `Complete` — bitiş zamanı, sonlanma nedeni, nihai yanıt (2000 karakterden uzunsa kırpılır) ve hata bilgisini doldurur.
- `Get`, `GetRecent(count)`, `GetBySession(sessionId)`.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `PostgresReasoningTraceStore` (`../Postgres/StoresAndSinks.md`) ile aynı arayüzü uygular.
- `ReasoningTrace`, `WorkflowRunner`/`WorkflowTraceEventProcessor` (Adapters.Agents katmanı) tarafından doldurulur; bu sınıf sadece saklar.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`FinalResponse` kırpması (2000 karakter) — çok uzun LLM çıktılarının trace deposunu şişirmesini önleyen basit bir bütçe kontrolüdür (`ContextPipeline`'ın karakter bütçesi mantığıyla aynı motivasyon, farklı katman).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `InMemoryReasoningTraceStore(maxCapacity = 500)` | Kapasiteyi ayarlayan constructor parametresi. |
| `StartTrace(sessionId, userQuery)` | Yeni `ReasoningTrace` oluşturur ve kaydeder. |
| `Update(trace)` | Trace'i günceller (referans zaten paylaşılıyor). |
| `Complete(traceId, terminationReason, finalResponse, error)` | Trace'i sonlandırır. |
| `Get(traceId)` | Tek trace. |
| `GetRecent(count)` | Son `count` trace, başlangıç zamanına göre azalan. |
| `GetBySession(sessionId)` | Bir oturuma ait tüm trace'ler. |

## 7. Bağımlılıklar

- Yok.
