# Workflow Designer

**Dosyalar:**
- `Pages/WorkflowDesigner.razor` — `/workflow-designer`
- `Pages/WorkflowDefaults.cs` — Default workflow JSON şablonu
- `Pages/NotFound.razor` — `*` (404)

Low-code workflow editor — LLM'siz deterministic akışların admin tarafından oluşturulması.

---

## WorkflowDesigner

İki panel: sol workflow listesi, sağ JSON editor + tester.

### Layout

```razor
<div class="workflow-designer">
    <!-- Sol: liste -->
    <aside class="workflow-list">
        <div class="header">
            <h3>Workflow'lar</h3>
            <button @onclick="CreateNewWorkflow">+ Yeni</button>
            <button @onclick="RefreshListAsync">Yenile</button>
        </div>
        @foreach (var wf in _workflows)
        {
            <div class="wf-item @(_selectedId == wf.Id ? "selected" : "")"
                 @onclick="() => SelectWorkflowAsync(wf.Id)">
                <div class="name">@wf.Name</div>
                <small>
                    @(wf.IsActive ? "Aktif" : "Pasif") · v@wf.Version · @wf.StepCount adım
                </small>
            </div>
        }
    </aside>

    <!-- Sağ: editor + tester -->
    <main class="workflow-editor">
        <div class="editor-section">
            <h3>JSON Tanımı</h3>
            <textarea @bind="_rawJson" class="json-editor" spellcheck="false"></textarea>

            <div class="actions">
                <button @onclick="SaveAsync">Kaydet</button>
                <button @onclick="DeleteAsync" class="danger">Sil</button>
            </div>
        </div>

        <div class="tester-section">
            <h3>Test Et</h3>
            <input @bind="_testInput" placeholder="Örn: ORD-1 durumu nedir" />
            <button @onclick="TestAsync">▶ Çalıştır</button>

            @if (_testResult != null)
            {
                <pre class="test-result @(_testResult.Success ? "success" : "failed")">
@JsonSerializer.Serialize(_testResult, _prettyJson)
                </pre>
            }
        </div>
    </main>
</div>
```

---

## JSON editor

`textarea` ile basit text editor — sözdizimi vurgu yok, ama:
- Monospace font
- Spellcheck kapalı
- Indent korunur

Gelişmiş editor (Monaco, CodeMirror) ileride entegre edilebilir. Şimdilik basit tutuldu (Blazor WASM bundle boyutu için).

### Save akışı

```csharp
private async Task SaveAsync()
{
    try
    {
        // JSON parse — geçerlilik kontrolü
        var doc = JsonDocument.Parse(_rawJson);

        await Workflow.SaveAsync(_selectedId, _rawJson);
        await RefreshListAsync();
        ShowToast("Kaydedildi");
    }
    catch (JsonException ex)
    {
        ShowError($"JSON hatası: {ex.Message}");
    }
    catch (HttpRequestException ex)
    {
        ShowError($"Server hatası: {ex.Message}");
    }
}
```

`_selectedId` null ise POST (create), doluysa PUT (update).

### Delete

```csharp
private async Task DeleteAsync()
{
    if (string.IsNullOrEmpty(_selectedId)) return;
    if (!await ConfirmAsync($"'{_selectedName}' silinsin mi?")) return;

    await Workflow.DeleteAsync(_selectedId);
    _selectedId = null;
    _rawJson = "";
    await RefreshListAsync();
}
```

`ConfirmAsync` JS interop: `window.confirm(message)`.

---

## Tester

Workflow'u kaydetmeden test ediyor.

```csharp
private async Task TestAsync()
{
    if (string.IsNullOrEmpty(_selectedId)) return;

    try
    {
        _testResult = await Workflow.TestAsync(_selectedId, _testInput, variables: null);
    }
    catch (Exception ex)
    {
        _testResult = new WorkflowExecutionResult { Success = false, Error = ex.Message };
    }
}
```

`/workflows/{id}/test` endpoint'i çalıştırır. Response her step'in trace'ini içerir:

```json
{
  "workflowId": "siparis-takibi",
  "success": true,
  "finalResponse": "Sipariş ORD-1 durumu: Kargoda",
  "durationMs": 12,
  "stepTraces": [
    { "stepId": "0", "type": "Branch", "skipped": false, "output": "..." },
    { "stepId": "1", "type": "Lookup", "skipped": false, "output": "..." },
    { "stepId": "2", "type": "Respond", "skipped": false, "output": "Sipariş ORD-1 durumu: Kargoda" }
  ],
  "finalVariables": { "order_id": "ORD-1", "status": "Kargoda" }
}
```

Admin step-by-step debug yapabilir.

---

## WorkflowDefaults.cs

```csharp
public static class WorkflowDefaults
{
    public const string SampleJson = @"{
  ""name"": ""Yeni Workflow"",
  ""description"": ""Yeni oluşturulan workflow"",
  ""version"": 1,
  ""isActive"": true,
  ""triggerKeywords"": [],
  ""inputPatterns"": {},
  ""steps"": []
}";
}
```

`CreateNewWorkflow` butonu bu template'i editor'a koyar — boş bir iskelet. Admin alanları doldurur.

---

## Step tipleri (server-side validation)

Editor JSON yazımına izin verir; ama server `WorkflowExecutor` geçersiz adımları execute etmez:

| Type | Required field'lar |
|---|---|
| `Respond` | `template` |
| `Lookup` | `tool` (forbidden tools yasak), `storeAs` |
| `Branch` | `condition`, `skipNext` |
| `SetVariable` | `variableName`, `variableValue` |

Save'de JSON geçerli ama mantıksal hata varsa save işlemi başarılı olur — test'te fail eder. Daha sıkı validation eklemek isterseniz client-side `WorkflowRequest` schema check ekleyebilirsiniz.

### Forbidden tools

```
order_placement_tool        — yeni sipariş (HITL gerekir)
complaint_registration_tool — şikayet (HITL gerekir)
human_handoff_tool          — insan çağrısı (text chat'te)
```

Editor uyarı vermez ama server execute fail eder:

```json
{
  "success": false,
  "error": "Tool 'order_placement_tool' is forbidden in workflows"
}
```

Detay: [Domain Model-Workflow](../domain/Model-Workflow.md).

---

## NotFound.razor

```razor
@page "/{*pageRoute}"
@layout EmptyLayout

<div class="not-found">
    <h1>404 — Bulunamadı</h1>
    <p>İstenen sayfa mevcut değil.</p>
    <a href="/">Ana sayfaya dön</a>
</div>
```

Catch-all route — `App.razor`'daki `<NotFound>` template'i tarafından da kullanılır.

`@page "/{*pageRoute}"` — herhangi bir URL eşleşir. Diğer `@page` direktifleri öncelikli, sadece eşleşmeyen path'ler buraya gelir.

---

## Bağlantılar

- [Api Endpoints-Improvements](../api/Endpoints-Improvements.md) — `/workflows` CRUD
- [Application WorkflowPortService](../application/WorkflowPortService.md)
- [Application WorkflowExecutor](../application/WorkflowExecutor.md)
- [Domain Model-Workflow](../domain/Model-Workflow.md)
- [Services.md](Services.md) — WorkflowApiService
- [Models.md](Models.md) — WorkflowModels.cs
