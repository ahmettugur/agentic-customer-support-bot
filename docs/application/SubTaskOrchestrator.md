# SubTaskOrchestrator

**Dosya:** `CustomerSupportBot.Application/Services/SubTaskOrchestrator.cs`  
**Tür:** `public class` (tüm metotlar static)  
**Yaşam döngüsü:** Singleton (DI'da kayıtlı, ama state taşımaz)

## Ne yapar?

Reasoning aşaması kullanıcının sorgusunun birden fazla bağımsız göreve bölünebileceğini tespit ettiğinde (compound query), `SubTaskOrchestrator` bu alt görevlerin nasıl sıralanacağına ve hangilerinin paralel çalışabileceğine karar verir.

**Temel fikir:** "1001'i iptal et ve iade başlat" gibi sorgular tek bir workflow run'ı değil, bağımsız iki görev içerir. Her görevi ayrı workflow run'ı olarak işlemek daha temiz sonuç üretir.

## `IsCompoundQuery`

```csharp
public static bool IsCompoundQuery(ReasoningResult? r)
```

**Compound query koşulu:** En az 2 farklı `TargetAgent`'a yönlenen en az 2 subtask var.

> **Neden 2 farklı agent?** Aynı agent'a iki görev tek bir workflow run'ında daha verimli işlenebilir. Farklı agent'lar ise bağımsız uzmanlık gerektirir — ayrıştırmanın değeri burada ortaya çıkar.

## `Partition`

```csharp
public static List<SubTaskGroup> Partition(
    IEnumerable<SubTask> subTasks,
    ParallelExecutionOptions options)
```

Sıralı subtask listesini **ardışık aynı türdeki** görevleri gruplar.

**`IsReadOnly` kriteri:** Alt görevin hedef specialist ajanı (`TargetAgent`) `WellKnown.AgentNames.ReadOnly` statik setinde (`ProductAgent`) bulunuyorsa yan-etkisiz (read-only) kabul edilir. Konfigürasyonda ayrı bir dinamik arama listesi tutulmaz, statik set kullanılır.

```
Örnek subtask'lar:
  1. OrderAgent:  1001 durum sorgula  [read-only]
  2. OrderAgent:  2002 durum sorgula  [read-only]
  3. ComplaintAgent: Şikayet kaydet       [write]
  4. OrderAgent:  3003 durum sorgula  [read-only]

Partition çıktısı:
  Grup 1: [1, 2]  → Parallel=true   (ikisi aynı türde ard arda)
  Grup 2: [3]     → Parallel=false  (write)
  Grup 3: [4]     → Parallel=true   (read-only ama tek — paralel çalışmanın önemi yok)
```

**Sıra korunur:** Grup 1 bitmeden Grup 2 başlamaz; Grup 2 bitmeden Grup 3 başlamaz.

## `CreateSubTaskReasoning`

```csharp
public static ReasoningResult CreateSubTaskReasoning(ReasoningResult parent, SubTask subTask)
```

Her subtask için downstream `RunAsync` çağrısına gidecek mini `ReasoningResult` üretir.

**Kritik:** `SubTasks = new List<SubTask>()` (boş). Bu sayede downstream `IsCompoundQuery` false döndürür — sonsuz recursive decomposition engellenir.

## `FormatSubTaskQuery`

```csharp
public static string FormatSubTaskQuery(SubTask subTask)
```

Subtask'ı downstream workflow'a gönderilecek kullanıcı sorgusu formatına çevirir:

```
Input:  SubTask { Description="1001 iptal et", Entities={"order_id":"1001"} }
Output: "1001 iptal et (order_id=1001)"
```

## `FormatSubTaskResult`

```csharp
public static string FormatSubTaskResult(SubTask subTask, string subResponse)
```

Her subtask sonucuna numaralı başlık ekler:

```
**1) 1001 iptal et**

Siparişiniz başarıyla iptal edilmiştir.
```

## `AggregateSubTaskResults`

```csharp
public static string AggregateSubTaskResults(IReadOnlyList<string> parts)
```

Tüm subtask sonuçlarını `---` ayırıcısıyla birleştirir:

```
**1) 1001 iptal et**

Siparişiniz başarıyla iptal edilmiştir.

---

**2) İade başlat**

İade talebiniz kayıt altına alınmıştır.
```

## `SubTaskGroup` record

```csharp
public record SubTaskGroup(bool Parallel, IReadOnlyList<SubTask> Items);
```

- `Parallel=true` → `CustomerSupportTeam` bu grubu `Task.WhenAll` ile çalıştırabilir
- `Parallel=false` → Sırayla çalıştırılmalı

## `ParallelExecutionOptions` yapılandırması

`appsettings.json` → `ParallelExecution:` bölümü:

```json
{
  "ParallelExecution": {
    "Enabled": true,
    "MaxDegreeOfParallelism": 4
  }
}
```

| Ayar | Açıklama | Varsayılan |
|------|---------|------------|
| `Enabled` | `false` ise tüm subtask'lar sıralı (sequential) çalışır. | `true` |
| `MaxDegreeOfParallelism` | Paralel gruptaki eş zamanlı maksimum alt görev çalıştırma sayısı. | `4` |

## Bütünleşik akış örneği

```
Kullanıcı: "1001 ve 2002'nin durumunu öğren, sonra 1001'i iptal et"

Reasoning → SubTasks:
  1. OrderAgent: 1001 durum sorgula [intent: inquiry]
  2. OrderAgent: 2002 durum sorgula [intent: inquiry]
  3. OrderAgent: 1001 iptal          [intent: cancel]

Partition:
  Grup 1: [1, 2] Parallel=true
  Grup 2: [3]    Parallel=false

CustomerSupportTeam.RunDecomposedAsync:
  → Task.WhenAll([RunAsync(sub1), RunAsync(sub2)])   ← eş zamanlı
  → RunAsync(sub3)                                   ← bekle

AggregateSubTaskResults → tek yanıt
```
