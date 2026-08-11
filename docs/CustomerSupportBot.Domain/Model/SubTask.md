# SubTask

**Dosya:** `Model/SubTask.cs`  
**Tür:** `class` (mutable)

## 1. Ne İşe Yarar

Compound query (bileşik sorgu) ayrıştırmasındaki **tek bir alt görevi** temsil eder. Kullanıcı tek mesajda birden fazla istek yaptığında (ör. "1030 siparişi nerede ve 1042 için şikayet açmak istiyorum") her istek bir `SubTask` olarak yapılandırılır.

## 2. Hangi Amaçla Kullanılır

`ReasoningResult.SubTasks` listesinin elemanıdır. `SubTaskOrchestrator` bu listeyi okuyarak paralel/sıralı yürütme planı oluşturur. Her alt görev kendi intent'i, hedef agent'ı ve entity'leri ile bağımsız bir iş birimi oluşturur.

> 💡 **Analiz notu:** Aynı anda iki farklı konuda yardım isteyen bir müşteri düşün. "Siparişim nerede?" ve "Şikayet açmak istiyorum" aynı mesajda gelince, bunlar iki ayrı SubTask olarak ayrıştırılır ve farklı agent'lara yönlendirilir.

## 3. Sorumlulukları

- ✅ Tek bir alt görevin tüm bilgisini (intent, agent, entities, dependencies) taşımak
- ❌ Alt görevi yürütmek — bu `SubTaskOrchestrator`'ın işi
- ❌ Alt görevleri ayrıştırmak — bu `ReasoningService`'in işi

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim üretir:** `ReasoningResultParser` (LLM JSON'ından)
- **Kim tüketir:** `SubTaskOrchestrator` (Application katmanı) — paralel/sıralı execution

## 5. Neden Böyle Tasarlandı (Analiz notu)

> 💡 **Dependencies listesi neden var?** Bazı alt görevler birbirine bağımlıdır. Örneğin "sipariş 1030'u iptal et, sonra yeni sipariş ver" durumunda ikinci görev birincisinin tamamlanmasını beklemeli. `Dependencies = [1]` demek "önce 1. subtask bitmeli" demektir. `SubTaskOrchestrator.Partition()` bu bağımlılıkları okuyarak hangi görevlerin paralel, hangilerinin sıralı çalışacağına karar verir.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `Order` | `int` | 1-indexed yürütme sırası |
| `Intent` | `string` | Bu alt görevin niyeti (üst düzey intent'ten farklı olabilir) |
| `Description` | `string` | İnsanlara yönelik kısa açıklama |
| `TargetAgent` | `string` | Bu alt görevi yürütecek specialist agent adı |
| `Entities` | `Dictionary<string, string>` | İhtiyaç duyulan entity'ler (ör. { "order_id": "1030" }) |
| `Dependencies` | `List<int>` | Bu görevin öncesinde tamamlanması gereken subtask sıraları |

## 7. Constructor Bağımlılıkları

Yok — saf veri sınıfı.

## Bağlantılar

- [ReasoningResult.md](ReasoningResult.md) — Bu SubTask'ları taşıyan model
- [../../CustomerSupportBot.Application/SubTaskOrchestrator.md](../../CustomerSupportBot.Application/SubTaskOrchestrator.md) — Alt görevleri yürüten orkestratör
