# CustomerSupportChatManager

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/CustomerSupportChatManager.cs`
- **Tür:** `public class : GroupChatManager`
- **Namespace:** `CustomerSupportBot.Adapters.Agents`

## Ne işe yarar?

`CustomerSupportChatManager`, Microsoft Agents Framework'ün `GroupChatManager` sınıfından türeyen; çoklu ajan takımındaki konuşma sırasını (`SelectNextAgentAsync`) ve iş akışının ne zaman sonlanacağını (`ShouldTerminateAsync`) yöneten akıllı orkestrasyon yöneticisidir.

## Hangi amaçla kullanılır`?

- Konuşma geçmişine bakarak bir sonraki ajanı dinamik stratejilerle ([FirstTurnStrategy](Routing.md), [PlanRoutingStrategy](Routing.md), [ReflectionRoutingStrategy](Routing.md)) belirlemek.
- Ajanlar arası sonsuz devirleri ve döngüleri (`EnforceHandoffLimit` ve `MaxHandoffsPerAgent`) engellemek.
- Mükerrer araç çağrılarını (`DetectRepeatedToolCall` ve `BuildToolSignature`) tespit edip akışı güvenle sonlandırmak.
- `WellKnown.Termination.Marker` görüldüğünde akışı başarıyla bitirmek.

## Sorumlulukları

- **Üstlendiği:**
  - `SelectNextAgentAsync` ile konuşma sırasındaki sonraki ajanı seçmek.
  - Handoff limitini aşan ajanları doğrudan `ResponseAgent`'a zorlamak.
  - `ShouldTerminateAsync` ile sonlanma belirteçlerini ve tekrarlı araç çağrılarını denetlemek.
  - Araç çağrı imzalarını (`BuildToolSignature`) takip ederek deterministik döngü kırmak.
- **Üstlenmediği:**
  - Ajanların prompt'larını veya araçlarını doğrudan çalıştırmak (bu MAF executor'ları tarafından yapılır).

## Constructor ve Başlatma Mantığı

```csharp
public CustomerSupportChatManager(
    IReadOnlyList<AIAgent> agents,
    WorkflowGuardOptions guards,
    ILogger<CustomerSupportChatManager> logger,
    string? constrainedSpecialistName = null)
```

### Constructor İçerisinde Yapılan İşler:
1. **Ajan Sözlüğünün Oluşturulması:** `agents` listesindeki tüm ajanlar büyük/küçük harf duyarsız adlarına (`StringComparer.OrdinalIgnoreCase`) göre `Dictionary<string, AIAgent>` yapısına aktarılır.
2. **Zorunlu Ajan Doğrulaması:**
   - `PlanningAgent` ve `ResponseAgent` sözlükte aranır; bulunamazsa fail-fast olarak `InvalidOperationException` fırlatılır.
3. **Kısıtlanmış Uzman Denetimi:**
   - Eğer `constrainedSpecialistName` verilmişse, bunun geçerli bir uzman (`WellKnown.AgentNames.Specialists`) olup olmadığı denetlenir ve `constrainedSpecialist` değişkenine atanır.
4. **RoutingContext Oluşturulması:**
   - Ajan sözlüğü, `PlanningAgent`, `ResponseAgent`, güvenlik kuralları (`Guards`), `Logger` ve varsa `ConstrainedSpecialist` referansları [RoutingContext](Routing.md) nesnesi altında toplanır.
5. **Yönlendirme Stratejilerinin Sıraya Dizilmesi:**
   - `_strategies` listesine sırasıyla `FirstTurnStrategy`, `PlanRoutingStrategy` ve `ReflectionRoutingStrategy` eklenir.

## Metotlar ve İç Çalışma Mantıkları

### 1. `SelectNextAgentAsync` (Protected Override)
```csharp
protected override async ValueTask<AIAgent> SelectNextAgentAsync(
    IReadOnlyList<ChatMessage> history,
    CancellationToken cancellationToken = default)
```
- **Ne işe yarar?:** Konuşma geçmişinin son durumunu inceleyerek sıradaki ajanın kim olması gerektiğine karar verir.
- **İç Mantığı:**
  1. Geçmişin son mesajı (`lastMessage`) alınır.
  2. `_strategies` listesindeki stratejiler sırayla denenir (`strategy.TrySelectAsync(...)`):
     - `FirstTurnStrategy`: Konuşma henüz başladıysa `PlanningAgent` (veya kısıtlanmış uzman) seçilir.
     - `PlanRoutingStrategy`: Son mesaj `PlanningAgent`'tan gelmişse ve plan hazırsa seçilen uzman ajan belirlenir.
     - `ReflectionRoutingStrategy`: Son mesaj bir uzman ajandan gelmişse ve görev bittiyse `ResponseAgent`, eskalasyon gerekiyorsa `HumanHandoffAgent` seçilir.
  3. Eşleşen strateji sonucu (`RoutingResult`) dönerse, `EnforceHandoffLimit` çağrılarak handoff sınırı denetlenir ve seçilen ajan döndürülür.
  4. Hiçbir strateji eşleşmezse emniyet sübabı olarak `PlanningAgent`'a geri dönülür (fallback).

### 2. `EnforceHandoffLimit` (Private)
```csharp
private void EnforceHandoffLimit(ref AIAgent selected, ref string branch)
```
- **Ne işe yarar?:** Bir uzman ajana aynı tur içerisinde maksimum izin verilen devir sayısından (`_guards.MaxHandoffsPerAgent`) fazla gidilmesini engeller.
- **İç Mantığı:**
  - `PlanningAgent` ve `ResponseAgent` bu kontrolden muaftır.
  - Seçilen uzmanın devir sayısı `_handoffCounts` tablosunda artırılır.
  - Sınır aşılmışsa seçim zorla `ResponseAgent` yapılır ve loglanır (`branch += "+handoff_limited"`).

### 3. `ShouldTerminateAsync` (Protected Override)
```csharp
protected override ValueTask<bool> ShouldTerminateAsync(
    IReadOnlyList<ChatMessage> history,
    CancellationToken cancellationToken = default)
```
- **Ne işe yarar?:** Çoklu ajan grup sohbetinin tamamlanıp tamamlanmadığını belirler.
- **İç Mantığı:**
  1. Son mesajın metninde `WellKnown.Termination.Marker` ("TERMINATE") varsa akış hemen sonlandırılır (`true`).
  2. `DetectRepeatedToolCall(history)` kontrol edilir; aynı araç aynı argümanlarla limitin üzerinde çağrılmışsa döngü kırılarak akış sonlandırılır (`true`).
  3. Aksi halde akış devam eder (`false`).

### 4. `DetectRepeatedToolCall` (Private)
```csharp
private bool DetectRepeatedToolCall(IReadOnlyList<ChatMessage> history)
```
- **Ne işe yarar?:** Modelin halüsinasyona girip aynı aracı aynı parametrelerle defalarca çağırmasını (infinite tool loop) tespit eder.
- **İç Mantığı:**
  - Son 10 mesaj taranır.
  - Bulunan `FunctionCallContent` nesneleri `BuildToolSignature` ile benzersiz bir imza metnine dönüştürülür.
  - Bir imzanın sayısı `_guards.MaxDuplicateToolCalls` eşiğine ulaşırsa `true` döner.

### 5. `BuildToolSignature` (Private Static)
```csharp
private static string BuildToolSignature(string toolName, IDictionary<string, object?>? args)
```
- **Ne işe yarar?:** Araç adı ve parametrelerinden sıralı, deterministik bir imza anahtarı (ör. `product_inquiry_tool:{"query":"ayakkabı"}`) üretir.
- **İç Mantığı:** Argüman anahtarları alfabetik sıralanır (`StringComparer.Ordinal`) ve JSON formatında serileştirilir. Serileştirme hatası durumunda fallback parametre dizgisi oluşturulur.

## Bağımlılıklar

- `Microsoft.Agents.AI.Workflows.GroupChatManager`
- `Microsoft.Extensions.AI.ChatMessage`
- [RoutingContext](Routing.md)
- [WorkflowGuardOptions](../CustomerSupportBot.Application/Ports/Outbound/WorkflowGuardOptions.md)
- `CustomerSupportBot.Adapters.Agents.Routing.*`
