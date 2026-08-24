# TurnFinalizer

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/TurnFinalizer.cs`
- **Tür:** `internal sealed class`
- **Namespace:** `CustomerSupportBot.Adapters.Agents`

## Ne işe yarar?

`TurnFinalizer`, bir iş akışı turu tamamlandığında (başarı, zaman aşımı veya hata); bekleyen eskalasyonların işlenmesi, ajan ziyaret çıktılarının doldurulması, episodik bellek kaydı, müşteri profili güncellemesi ve [IReasoningTraceStore](../CustomerSupportBot.Application/Ports/Outbound/Observability/IReasoningTraceStore.md) üzerinde trace'in kapatılması gibi tüm tur sonu yan etkilerini tek bir merkezde toplayan ve yöneten sınıftır.

## Hangi amaçla kullanılır`?

- **Episodik Bellek Kirliliğini Önleme:** Compound (bileşik) bir sorgu N alt göreve bölündüğünde, her alt görev için ayrı sentetik episodik bellek yazılmasını ve müşteri profili sayacının N kez artırılmasını engellemek; alt koşularda bu yan etkileri atlayıp (`isSubTaskRun = true`), en sonda aggregate birleşik sonuçla tek bir kez (`FinalizeAggregateTurnAsync`) yazmak.
- **Trace Çıktılarını Tamamlama (`PopulateAgentVisitOutputs`):** Ziyaret edilen ajanların boş kalan çıktı alanlarını (PlanningResult JSON, SpecialistReasoning JSON, ResponseAgent metni) otomatik doldurarak `/traces` ve `/replay` panellerinde eksiksiz görünmesini sağlamak.
- **Güvenli Yan Etki Yürütümü:** Bellek yazımı veya profil güncellemelerinde hata oluşsa bile kullanıcının yanıt almasını engellemeyecek şekilde korumalı (`Safe`) metodlar işletmek.

## Sorumlulukları

- **Üstlendiği:**
  - `FinalizeAsync` ile tek alt görev veya tekil tur trace'ini kapatmak.
  - `FinalizeAggregateTurnAsync` ile compound sorgunun birleşik sonucunu ve episodik belleğini yazmak.
  - Eskalasyon durumunda onay kapısı üzerinden `ProcessPendingEscalationsAsync` çağırmak.
  - Ajan ziyaret kayıtlarındaki JSON çıktılarını biçimlendirmek (`PrettyJson`).

## Constructor ve Başlatma Mantığı

```csharp
public TurnFinalizer(
    IReasoningTraceStore traceStore,
    ApprovalGateService approvalGate,
    ILoggerFactory loggerFactory,
    ISemanticMemoryWriter? semanticMemory,
    ICustomerProfileService? profileService)
```

### Constructor İçerisinde Yapılan İşler:
- `_traceStore`: Trace kapatma işlemlerini yürütür.
- `_approvalGate`: Eskalasyonları işler.
- `_loggerFactory`: Günlükleme motorunu kurar.
- `_semanticMemory`: Vektör tabanlı episodik bellek yazıcısını saklar.
- `_profileService`: Müşteri etkileşim profili güncelleyicisini saklar.

## Metotlar ve İç Çalışma Mantıkları

### 1. `FinalizeAsync`
```csharp
public async Task FinalizeAsync(
    ReasoningTrace trace,
    AgentSession? session,
    string query,
    string result,
    string terminationReason,
    CancellationToken ct = default,
    bool isSubTaskRun = false)
```
- **Ne işe yarar?:** Tek bir iş akışı koşusunu sonlandırır.
- **İç Mantığı:**
  1. `_approvalGate.ProcessPendingEscalationsAsync`: Varsa tur sırasında doğan eskalasyonları işler.
  2. `PopulateAgentVisitOutputs(trace, result)`: Ajan ziyaret kayıtlarının çıktılarını doldurur.
  3. `if (!isSubTaskRun)`: Alt görev koşusu DEĞİLSE; `WriteEpisodicMemorySafe` ile episodik belleği ve `UpdateCustomerProfileSafeAsync` ile müşteri profilini günceller.
  4. `_traceStore.Complete`: Trace kaydını `finalResponse` ve `terminationReason` ile tamamlandı durumuna çeker.

### 2. `FinalizeAggregateTurnAsync`
```csharp
public async Task FinalizeAggregateTurnAsync(
    AgentSession? session,
    string query,
    string aggregateResult,
    string? intent,
    CancellationToken ct = default)
```
- **Ne işe yarar?:** Compound bir sorgunun tüm alt görevleri bittiğinde tur bazlı yan etkileri bir kez yazar.
- **İç Mantığı:** Kullanıcının orijinal sorusunu (`query`) ve birleşik yanıtını (`aggregateResult`) alarak tek bir episodik bellek kaydı oluşturur ve müşteri profilini 1 tur ilerletir.

### 3. `PopulateAgentVisitOutputs` (Internal Static)
- **Ne işe yarar?:** `ReasoningTrace.AgentVisits` koleksiyonundaki her bir ziyaret kaydını inceler; Planning için `trace.Planning` JSON'unu, uzmanlar için `trace.SpecialistReasonings` JSON'unu, ResponseAgent için ise nihai metni ziyaret çıktısı olarak yazar (uzun metinler 1500 karakterle kırpılır).
- 🐞 **Eşleşme mantığı değişti (bulgu 4.5).** Eskiden `visit.AgentName.Split('_', 2)[0]` ile bir "temel ad" çıkarılıp sabit `"Planning"`/`"Response"` string literalleriyle ve specialist eşleşmesinde TERS yönde (`s.AgentName.StartsWith(baseName, ...)`) karşılaştırılıyordu — bu, gelecekte bir ajan adı `_` içerirse kırılabilirdi. `WorkflowResponseExtractor.ExtractSpecialistReasonings`'in kurulu deseniyle hizalandı: split YOK, doğrudan tam `visit.AgentName` üzerinden `WellKnown.AgentNames.Planning`/`.Response` sabitleriyle ve specialist eşleşmesinde `name.StartsWith(s.AgentName, ...)` yönünde karşılaştırma (çünkü `s.AgentName` her zaman temiz bir `WellKnown.AgentNames` değeridir, ham executor id değil). Test edilebilirlik için `private` yerine `internal` yapıldı — bkz. `TurnFinalizerPureLogicTests`.

## Bağımlılıklar

- [IReasoningTraceStore](../CustomerSupportBot.Application/Ports/Outbound/Observability/IReasoningTraceStore.md)
- [ApprovalGateService](ApprovalGateService.md)
- [ReasoningTrace](../CustomerSupportBot.Domain/Model/ReasoningTrace.md)
- [AgentSession](../CustomerSupportBot.Domain/Model/AgentSession.md)
- `CustomerSupportBot.Application.Services.ISemanticMemoryWriter`
- `CustomerSupportBot.Application.Services.ICustomerProfileService`
