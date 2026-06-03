# Workflow Designer

**Dosyalar:**
- `Pages/WorkflowDesigner.razor` — `/workflow-designer`
- `Pages/WorkflowDefaults.cs` — Yeni workflow varsayılan değerleri
- `wwwroot/js/x6.js` — AntV X6 v2.19.2 graf kütüphanesi (495 KB, UMD bundle)
- `wwwroot/js/workflow-x6.js` — Özel workflow tasarımcısı implementasyonu
- `wwwroot/css/workflow.css` — Tasarımcı stilleri

Low-code workflow editor — LLM'siz deterministic akışların admin tarafından **görsel olarak** oluşturulması.

---

## Genel Bakış

Eski JSON text editor'ın yerini **X6.js tabanlı görsel graf tasarımcısı** aldı. Workflow adımları, birbirine oklarla bağlanmış düğümler (node) olarak çizilir.

3 panelli layout:

```
┌──────────────┬─────────────────────────────────┬──────────────────┐
│  Sol Panel   │         Orta — Canvas           │   Sağ Panel      │
│  Workflow    │  ┌──────────┐  ┌──────────┐    │  Meta form       │
│  Listesi     │  │  BRANCH  │→ │  LOOKUP  │    │  (isim, tetikl.) │
│              │  └──────────┘  └──────────┘    │                  │
│  + Yeni      │       ↓             ↓           │  Seçili Node     │
│              │  ┌──────────┐  ┌──────────┐    │  özellikleri     │
│  Yenile      │  │  YANIT   │  │  YANIT   │    │                  │
│              │  └──────────┘  └──────────┘    │  Test Runner     │
└──────────────┴─────────────────────────────────┴──────────────────┘
```

---

## Toolbar (üst bar)

| Buton | İşlev |
|-------|-------|
| YANIT / SORGULA / KOSUL / DEGISKEN | Yeni adım ekler (canvas ortasına) |
| ↩ / ↪ | Undo / Redo |
| + / − / ⊡ | Zoom in / out / fit |
| ⊹ | Canvas'ı ortala |
| Kaydet | Workflow'u API'ye kaydeder |
| Sil | Seçili workflow'u siler |

---

## Node tipleri

Her adım tipi farklı renkte ve sol şerit tasarımıyla gösterilir:

| Tip | Renk | Açıklama |
|-----|------|---------|
| YANIT (Respond) | Mavi | Template render, kullanıcıya gönderir |
| SORGULA (Lookup) | Yeşil | Tool çağrısı + variable'a yazar |
| KOSUL (Branch) | Sarı | Koşul değerlendirme; OnTrue/OnFalse dallanma |
| DEGISKEN (SetVariable) | Mor | Variable atama |

---

## Port sistemi ve bağlantı yapımı

Her node'un **port** dairecikleri vardır:

| Port | Konum | Renk | Anlamı |
|------|-------|------|--------|
| `in` | Üst | Gri (boş) | Giriş — bağlantı alır |
| `out` | Alt | Renkli (dolu) | Çıkış — `Next` bağlantısı |
| `onTrue` | Sağ | Yeşil (dolu) | Branch True dalı |
| `onFalse` | Alt | Kırmızı (dolu) | Branch False dalı |

**Bağlantı yapmak için:**
1. Kaynak node'un üzerine gel → çıkış portları büyür
2. Renkli (dolu) porta **tıkla + sürükle**
3. Hedef node'un **gri üst portuna** bırak → ok çizilir

Aynı çıkış portundan yalnızca bir bağlantıya izin verilir.

---

## Blazor ↔ JavaScript interop

Sayfa açıldığında JS dosyaları dinamik yüklenir:

```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (!firstRender) return;
    _dotNetRef = DotNetObjectReference.Create(this);

    await JS.InvokeVoidAsync("loadScript", "/js/x6.js", "js-x6");
    var wfV = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    await JS.InvokeVoidAsync("loadScript", $"/js/workflow-x6.js?v={wfV}", "js-wf-x6");
    await JS.InvokeVoidAsync("wfDesigner.init", "wf-canvas", _dotNetRef);
}
```

`workflow-x6.js` her sayfa yüklenişinde taze yüklenir (geliştirme cache sorunu önleme).  
`x6.js` (büyük kütüphane) sabit — browser cache'de kalır.

### `wfDesigner` JS API

| Metot | Açıklama |
|-------|---------|
| `init(containerId, dotNetRef)` | Grafiği başlatır, Blazor callback'i kaydeder |
| `loadGraph(jsonString)` | Workflow JSON'ını canvas'a çizer |
| `getGraphData()` | Canvas'tan workflow JSON'ını üretir |
| `addStep(type)` | Yeni node ekler (`"Respond"`, `"Lookup"`, `"Branch"`, `"SetVariable"`) |
| `updateSelectedNode(dataJson)` | Seçili node'un verilerini günceller |
| `undo()` / `redo()` | Geçmişe dön / ileri |
| `zoomIn()` / `zoomOut()` / `zoomFit()` | Zoom |
| `center()` | Canvas'ı ortalar |
| `deleteSelected()` | Seçili hücreleri siler |
| `destroy()` | Belleği temizler (component dispose) |

### Blazor tarafından çağrılan JS metodları

```csharp
[JSInvokable]
public void OnNodeSelected(string? dataJson)   // Node seçildi/seçim kaldırıldı
[JSInvokable]
public void OnGraphChanged(string? _)          // Edge eklendi/silindi
```

---

## Auto-layout algoritması

Workflow JSON yüklendiğinde node'lar otomatik yerleştirilir:

```javascript
// BFS ile graf traversal
queue = [{ id: startStepId, x: 0, y: 60 }]
while (queue) {
    next    → aynı x, y + V_GAP
    onTrue  → x + H_GAP*0.55, y + V_GAP
    onFalse → x - H_GAP*0.55, y + V_GAP
}
// Bağlı olmayan node'lar sağa hizalanır
// Tüm x'ler merkeze normalize edilir
```

---

## Kaydetme akışı

```csharp
private async Task SaveWorkflowAsync()
{
    var graphJson = await JS.InvokeAsync<string>("wfDesigner.getGraphData");
    // graphJson → { startStepId, steps: [...] }

    var req = new WorkflowRequest {
        Name = _meta.Name, Description = _meta.Description,
        IsActive = _meta.IsActive,
        TriggerKeywords = ..., InputPatterns = ...,
        Steps = steps  // graf'tan gelen Next/OnTrue/OnFalse bağlantılarıyla
    };
    await WorkflowApi.SaveAsync(_currentId, req);
}
```

`_currentId` null ise POST (oluştur), doluysa PUT (güncelle).

---

## Test runner

Sağ panelin alt kısmında — workflow'u kaydetmeden test eder.

```csharp
private async Task RunTestAsync()
{
    _testResult = await WorkflowApi.TestAsync(_currentId, _testInput);
}
```

`/workflows/{id}/test` endpoint'ini çağırır. Her step'in trace'i, değişkenlerin son durumu ve hata mesajları JSON olarak gösterilir.

---

## WorkflowDefaults.cs

Yeni workflow oluşturulduğunda meta form varsayılan değerleri sağlar:

```csharp
public static class WorkflowDefaults
{
    public const string DefaultName = "Yeni Workflow";
    // ... diğer default'lar
}
```

---

## Encoding

Workflow JSON'ı `UnsafeRelaxedJsonEscaping` ile serialize edilir — Türkçe karakterler ve emoji'ler `\uXXXX` escape dizileri yerine doğrudan UTF-8 olarak saklanır.

```csharp
private static readonly JsonSerializerOptions _json = new()
{
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};
```

---

## Bağlantılar

- [Domain Model-Workflow](../domain/Model-Workflow.md) — WorkflowDefinition / WorkflowStep
- [Application WorkflowExecutor](../application/WorkflowExecutor.md) — Graf traversal engine
- [Api Endpoints-Admin](../api/Endpoints-Admin.md) — `/workflows` CRUD
- [Application WorkflowPortService](../application/WorkflowPortService.md)
- [Web Services.md](Services.md) — WorkflowApiService
