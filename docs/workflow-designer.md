# Workflow Designer — Detaylı Analiz

## 1. Genel Konsept

Workflow Designer, **LLM çağrısı yapmadan** çalışan, tamamen **deterministik** bir low-code iş akışı motorudur. Mevcut multi-agent sisteme dokunmadan, admin'in basit görevleri otomatize etmesini sağlar.

**Mevcut Durum:** Workflow Designer şu an tamamen **izole bir sandbox**. Admin workflow tasarlar, test eder, sonuçları görür — ancak gerçek müşteri konuşmalarına entegre değildir.

```
Kullanıcı mesajı → ReasoningService → Multi-Agent Workflow → Yanıt
                   ↑
                   Workflow Designer'ın buraya bağlantısı YOK

Admin "Test" butonu → WorkflowExecutor → Sonuç (sadece admin görür)
```

---

## 2. Veri Modeli

### WorkflowDefinition

| Alan | Tür | Açıklama |
|------|-----|----------|
| `Id` | string | Slug formatında URL-safe ID (Türkçe karakterler otomatik normalize edilir) |
| `Name` | string | Workflow adı |
| `Description` | string | Açıklama |
| `Version` | int | Her upsert'te otomatik artırılır |
| `IsActive` | bool | Pasifse executor reddeder |
| `TriggerKeywords` | List\<string\> | Kullanıcı mesajında eşleşirse workflow tetiklenir (henüz kullanılmıyor) |
| `InputPatterns` | Dict\<string, string\> | Regex ile değişken çıkarma (ör: `"orderId": "(ORD-\\d+)"`) |
| `Steps` | List\<WorkflowStep\> | Sıralı adımlar |
| `CreatedAt` | DateTime | Oluşturma zamanı |
| `UpdatedAt` | DateTime | Son güncelleme zamanı |
| `UpdatedBy` | string? | Son düzenleyen admin (audit) |

### Adım Tipleri (WorkflowStepType)

| Tip | Kullanılan Alanlar | Davranış |
|-----|-------------------|----------|
| **Respond** | `Template` | `{varName}` placeholder'larını değişkenlerle değiştirir, çıktıya ekler |
| **Lookup** | `Tool`, `Parameters`, `StoreAs` | Sadece güvenli tool'ları çağırır, sonucu değişkene yazar |
| **Branch** | `Condition`, `SkipNext` | Koşul **false** ise sonraki N adımı atlar |
| **SetVariable** | `VariableName`, `VariableValue` | Yeni değişken atar (template substitution destekler) |

### WorkflowStep Alanları

| Alan | Tip | Açıklama |
|------|-----|----------|
| `Id` | string | Otomatik üretilen 6 karakterlik GUID |
| `Type` | WorkflowStepType | Adım tipi |
| `Label` | string? | UI ve trace için açıklama |
| `Template` | string? | Respond için şablon mesaj |
| `Tool` | string? | Lookup için tool adı |
| `Parameters` | Dict\<string, string\> | Tool parametreleri (`$varName` veya literal) |
| `StoreAs` | string? | Tool çıktısının yazılacağı değişken adı |
| `Condition` | string? | Branch koşul ifadesi |
| `SkipNext` | int | Koşul false olduğunda atlanacak adım sayısı (default 1) |
| `VariableName` | string? | SetVariable için değişken adı |
| `VariableValue` | string? | SetVariable için değer (template destekler) |

### Çıktı Modeli (WorkflowExecutionResult)

| Alan | Açıklama |
|------|----------|
| `WorkflowId` | Çalıştırılan workflow ID |
| `Success` | Hiçbir adımda hata yoksa true |
| `FinalResponse` | Tüm Respond adımlarının birleşik çıktısı |
| `StepTraces` | Her adımın yürütme sonucu (debug + admin UI) |
| `FinalVariables` | Yürütme sonundaki tüm değişkenler |
| `DurationMs` | Toplam süre (ms) |
| `Error` | Varsa hata mesajı |

---

## 3. Executor — Yürütme Motoru

### Yürütme Akışı

```
Kullanıcı Input'u
      │
      ▼
┌─ IsActive kontrolü (pasifse → hata)
│
├─ Variables init ("input" = userInput + initialVariables)
│
├─ InputPatterns ile regex extraction
│   ör: "(ORD-\d+)" → vars["orderId"] = "ORD-42"
│
└─ Adım adım yürütme (for döngüsü):
    ├─ Respond  → Render(template, vars) → output'a ekle
    ├─ Lookup   → Tool çağır → sonucu StoreAs'e yaz
    ├─ Branch   → EvaluateCondition → false ise i += SkipNext
    └─ SetVariable → Render(value, vars) → vars'a yaz
```

### Güvenlik: Yasak Tool'lar

Yan etkili tool'lar workflow içinden çağrılamaz (HITL approval gate bypass'ı önlemek için):

| Yasak Tool | Neden |
|------------|-------|
| `order_placement_tool` | Sipariş oluşturur (yan etkili) |
| `complaint_registration_tool` | Şikayet kaydeder (yan etkili) |
| `human_handoff_tool` | İnsan aktarımı başlatır (yan etkili) |

İzin verilen (okuma amaçlı) tool'lar:

| İzin Verilen Tool | İşlev |
|-------------------|-------|
| `product_inquiry_tool` | Ürün bilgisi sorgulama |
| `order_status_tool` | Sipariş durumu sorgulama |
| `get_last_order_tool` | Son siparişi getirme |
| `get_all_orders_tool` | Tüm siparişleri listeleme |

### Lookup Sonuç Depolama

Bir Lookup adımı `storeAs: "result"` ile çalıştığında şu değişkenler oluşur:

| Değişken | İçerik |
|----------|--------|
| `result` | Tool mesajı (kısa erişim) |
| `result.message` | Tool mesajı |
| `result.success` | `"true"` veya `"false"` |
| `result.data` | Tool data JSON'ı |

### Koşul Değerlendirme (EvaluateCondition)

| İfade | Örnek | Açıklama |
|-------|-------|----------|
| `exists` | `orderId exists` | Değişken var ve boş değilse true |
| `missing` | `orderId missing` | Değişken yok veya boşsa true |
| `==` | `status == delivered` | Eşitlik kontrolü |
| `!=` | `status != cancelled` | Eşitsizlik kontrolü |

**Önemli:** Branch sadece koşul **FALSE** olduğunda `SkipNext` kadar adım atlar. TRUE olduğunda normal sıralı yürütmeye devam eder.

### Parametre Çözümleme

Tool parametreleri iki yolla çözülür:

- `$orderId` → Doğrudan değişken referansı
- `{orderId}` → Template substitution (Render ile)

---

## 4. Store — Tanım Saklama

### InMemoryWorkflowDefinitionStore

- **Thread-safe**: `ConcurrentDictionary` kullanır
- **Slugify**: Türkçe karakterler normalize edilir (ş→s, ğ→g, ı→i, ö→o, ü→u, ç→c)
- **Auto-versioning**: Aynı ID ile upsert yapıldığında `Version` otomatik artırılır
- **CreatedAt korunur**: Güncelleme orijinal oluşturma tarihini bozmaz
- **Sıralama**: Aktifler önce, sonra isme göre alfabetik
- **Not**: Uygulama restart olunca veriler silinir (Postgres implementasyonu yok)

### Slug Üretim Örneği

```
"Sipariş Durumu Hızlı Yanıt"
  → ToLowerInvariant: "sipariş durumu hızlı yanıt"
  → Türkçe normalize: "siparis durumu hizli yanit"
  → NonAlnum → "-": "siparis-durumu-hizli-yanit"
```

---

## 5. API Endpoint'leri

Tümü `RequireAuthorization("Admin")` scope altındadır.

| Method | Route | İşlev |
|--------|-------|-------|
| `GET` | `/workflows` | Tüm workflow listesi (`{ count, items }`) |
| `GET` | `/workflows/{id}` | Tek workflow detayı |
| `POST` | `/workflows` | Yeni workflow oluştur (`name` zorunlu) |
| `PUT` | `/workflows/{id}` | Güncelle (version otomatik artar) |
| `DELETE` | `/workflows/{id}` | Sil |
| `POST` | `/workflows/{id}/test` | Test çalıştır (`{ input, variables? }`) |

---

## 6. Frontend Arayüzü

### Sayfa: `/workflow-designer.html`

```
┌─────────────────┬──────────────────────────────────────────┐
│  Workflow Listesi│  JSON Editör                             │
│  ───────────────│  ──────────────────────────────────────── │
│  [+ Yeni][Yenile]│  textarea (workflow JSON tanımı)         │
│                  │                                          │
│  ▸ Sipariş Hızlı │  [💾 Kaydet] [🗑️ Sil]                  │
│    aktif · v2    │  ──────────────────────────────────────── │
│  ▸ İade Akışı    │  Test Çalıştır                           │
│    pasif · v1    │  User input: [______________] [▶ Test]   │
│                  │  ┌─────────────────────────────────────┐ │
│                  │  │ // test sonuçları JSON               │ │
│                  │  └─────────────────────────────────────┘ │
└─────────────────┴──────────────────────────────────────────┘
```

### Kullanıcı Akışı

1. **Sayfa yüklenince** → `GET /workflows` → sol panelde liste
2. **Workflow seçimi** → `GET /workflows/{id}` → editörde JSON gösterilir
3. **"+ Yeni"** → Örnek SAMPLE JSON yüklenir
4. **"Kaydet"** → Yeni ise `POST /workflows`, mevcut ise `PUT /workflows/{id}`
5. **"Sil"** → `DELETE /workflows/{id}` (onay dialog'u)
6. **"Test"** → `POST /workflows/{id}/test` → Sonuç JSON olarak gösterilir

---

## 7. Örnek Workflow — Sipariş Durumu Hızlı Yanıt

### JSON Tanımı

```json
{
  "name": "Sipariş Durumu Hızlı Yanıt",
  "description": "Kullanıcı sipariş numarası verirse direkt durumu döner.",
  "isActive": true,
  "triggerKeywords": ["sipariş", "kargo", "teslimat"],
  "inputPatterns": {
    "orderId": "(ORD-\\d+)"
  },
  "steps": [
    {
      "type": "Branch",
      "label": "Sipariş numarası YOK mu?",
      "condition": "orderId missing",
      "skipNext": 2
    },
    {
      "type": "Respond",
      "template": "Merhaba! Sipariş numaranızı paylaşır mısınız? (ör. ORD-1)"
    },
    {
      "type": "Branch",
      "label": "Numarasız çıkış",
      "condition": "orderId exists",
      "skipNext": 99
    },
    {
      "type": "Lookup",
      "label": "Sipariş sorgula",
      "tool": "order_status_tool",
      "parameters": { "orderId": "$orderId" },
      "storeAs": "result"
    },
    {
      "type": "Respond",
      "template": "📦 Sipariş {orderId} durumu:\n\n{result}"
    }
  ]
}
```

### Senaryo 1: orderId VAR (`"ORD-1 kargom nerede?"`)

```
Regex extraction: orderId = "ORD-1" ✓

Step 0: Branch "orderId missing" → FALSE → skip 2 (Step 1 ve 2 atlanır)
Step 3: Lookup order_status_tool(orderId=ORD-1) → result'a yazılır ✓
Step 4: Respond "📦 Sipariş ORD-1 durumu: ..." ✓

Çıktı: "📦 Sipariş ORD-1 durumu:\n\nSipariş ORD-1 bulundu..."
```

### Senaryo 2: orderId YOK (`"kargom nerede?"`)

```
Regex extraction: orderId bulunamadı

Step 0: Branch "orderId missing" → TRUE → devam (skip yok)
Step 1: Respond "Sipariş numaranızı paylaşır mısınız?" ✓
Step 2: Branch "orderId exists" → FALSE → skip 99 → workflow biter ✓

Çıktı: "Merhaba! Sipariş numaranızı paylaşır mısınız? (ör. ORD-1)"
```

### Branch Mantığı Açıklaması

Branch "koşul FALSE ise atla" mantığıyla çalıştığı için if-else akışları kurarken
**koşulları ters düşünmek** gerekir:

```
"orderId varsa Lookup'a git" yerine:
"orderId yoksa 2 adım atla" (FALSE → skip) yazılır.

"orderId yoksa workflow'u bitir" yerine:
"orderId varsa 99 adım atla" (FALSE → skip) yazılır.
```

---

## 8. Chat Akışıyla Entegrasyon Durumu

### Mevcut: Entegre DEĞİL

- `ChatEndpoints.cs`'de workflow'a hiçbir referans yok
- `TriggerKeywords` alanı modelde tanımlı ama hiçbir serviste okunmuyor
- Workflow'un çalıştırıldığı tek yer `/workflows/{id}/test` endpoint'i
- Gerçek kullanıcı mesajları bu endpoint'e ulaşmaz

### Olması Gereken (Entegrasyon)

```
Kullanıcı mesajı gelir
      │
      ▼
  TriggerKeywords eşleşmesi var mı?
      │
      ├── EVET → WorkflowExecutor çalıştır → Yanıtı döndür (LLM maliyeti 0)
      │
      └── HAYIR → Mevcut akış (ReasoningService → Multi-Agent → Yanıt)
```

---

## 9. Örnek Workflow JSON'ları

### 9.1 Sipariş Durumu Hızlı Yanıt

Kullanıcı sipariş numarası verirse LLM'e gitmeden doğrudan `order_status_tool` ile yanıt döner.
Sipariş numarası yoksa numarayı sorar ve workflow'u sonlandırır.

```json
{
  "name": "Sipariş Durumu Hızlı Yanıt",
  "description": "ORD- ile başlayan sipariş numarası varsa OrderStatus tool'unu çağırır.",
  "isActive": true,
  "triggerKeywords": ["sipariş", "kargo", "teslimat", "durumu"],
  "inputPatterns": {
    "orderId": "(ORD-\\d+)"
  },
  "steps": [
    {
      "type": "Branch",
      "label": "Sipariş numarası YOK mu?",
      "condition": "orderId missing",
      "skipNext": 2
    },
    {
      "type": "Respond",
      "template": "Merhaba! Sipariş numaranızı paylaşır mısınız? (ör. ORD-1)"
    },
    {
      "type": "Branch",
      "label": "Numarasız çıkış",
      "condition": "orderId exists",
      "skipNext": 99
    },
    {
      "type": "Lookup",
      "label": "Sipariş sorgula",
      "tool": "order_status_tool",
      "parameters": { "orderId": "$orderId" },
      "storeAs": "result"
    },
    {
      "type": "Respond",
      "template": "📦 Sipariş {orderId} durumu:\n\n{result}"
    }
  ]
}
```

**Test senaryoları:**

| Input | orderId | Akış | Çıktı |
|-------|---------|------|-------|
| `"ORD-1 kargom nerede?"` | `ORD-1` | Step 0 FALSE→skip 2 → Lookup → Respond | `"📦 Sipariş ORD-1 durumu: ..."` |
| `"kargom nerede?"` | (yok) | Step 0 TRUE→devam → Respond → Step 2 FALSE→skip 99 | `"Sipariş numaranızı paylaşır mısınız?"` |

### 9.2 Ürün Bilgisi Sorgulama

Kullanıcı bir ürün adı söylerse stoktan bilgi çeker.

```json
{
  "name": "Ürün Bilgisi Sorgulama",
  "description": "Ürün adı geçerse product_inquiry_tool ile stok/fiyat bilgisi döner.",
  "isActive": true,
  "triggerKeywords": ["ürün", "fiyat", "stok", "bilgi"],
  "inputPatterns": {
    "productName": "(Laptop|Telefon|Kulaklık|Tablet|Klavye)"
  },
  "steps": [
    {
      "type": "Branch",
      "label": "Ürün adı var mı?",
      "condition": "productName missing",
      "skipNext": 2
    },
    {
      "type": "Respond",
      "template": "Hangi ürün hakkında bilgi almak istersiniz? (Laptop, Telefon, Kulaklık, Tablet, Klavye)"
    },
    {
      "type": "Branch",
      "label": "Ürünsüz çıkış",
      "condition": "productName exists",
      "skipNext": 99
    },
    {
      "type": "Lookup",
      "label": "Ürün sorgula",
      "tool": "product_inquiry_tool",
      "parameters": { "productName": "$productName" },
      "storeAs": "product"
    },
    {
      "type": "Respond",
      "template": "🛍️ {productName} bilgileri:\n\n{product}"
    }
  ]
}
```

### 9.3 Müşteri Son Sipariş Durumu

Müşteri ID'sine göre son siparişi bulur ve durumunu gösterir. İki aşamalı lookup zincirleme örneği.

```json
{
  "name": "Son Siparişim Ne Durumda",
  "description": "Müşteri ID ile son siparişi bulur, durumunu döner.",
  "isActive": true,
  "triggerKeywords": ["son sipariş", "son siparişim"],
  "inputPatterns": {
    "customerId": "(CST-\\d+)"
  },
  "steps": [
    {
      "type": "Branch",
      "label": "Müşteri ID var mı?",
      "condition": "customerId missing",
      "skipNext": 2
    },
    {
      "type": "Respond",
      "template": "Müşteri numaranızı paylaşır mısınız? (ör. CST-1)"
    },
    {
      "type": "Branch",
      "label": "ID'siz çıkış",
      "condition": "customerId exists",
      "skipNext": 99
    },
    {
      "type": "Lookup",
      "label": "Son siparişi bul",
      "tool": "get_last_order_tool",
      "parameters": { "customerId": "$customerId" },
      "storeAs": "lastOrder"
    },
    {
      "type": "Branch",
      "label": "Sipariş bulundu mu?",
      "condition": "lastOrder.success == false",
      "skipNext": 1
    },
    {
      "type": "Respond",
      "template": "📦 Son siparişiniz:\n\n{lastOrder}"
    }
  ]
}
```

### 9.4 Hoş Geldin Mesajı (SetVariable Örneği)

Değişken atama ve şablon birleştirme örneği.

```json
{
  "name": "Hoş Geldin Akışı",
  "description": "Müşteriye kişiselleştirilmiş hoş geldin mesajı üretir.",
  "isActive": true,
  "triggerKeywords": ["merhaba", "selam", "iyi günler"],
  "inputPatterns": {},
  "steps": [
    {
      "type": "SetVariable",
      "label": "Karşılama mesajı oluştur",
      "variableName": "greeting",
      "variableValue": "Merhaba! TechStore müşteri destek hattına hoş geldiniz."
    },
    {
      "type": "SetVariable",
      "label": "Menü oluştur",
      "variableName": "menu",
      "variableValue": "1️⃣ Sipariş durumu sorgulama\n2️⃣ Ürün bilgisi\n3️⃣ İade ve şikayet\n4️⃣ Diğer"
    },
    {
      "type": "Respond",
      "template": "{greeting}\n\nSize nasıl yardımcı olabilirim?\n\n{menu}"
    }
  ]
}
```

### 9.5 Tüm Siparişleri Listeleme

```json
{
  "name": "Siparişlerimi Listele",
  "description": "Müşterinin tüm siparişlerini listeler.",
  "isActive": true,
  "triggerKeywords": ["siparişlerim", "tüm siparişler", "sipariş listesi"],
  "inputPatterns": {
    "customerId": "(CST-\\d+)"
  },
  "steps": [
    {
      "type": "Branch",
      "label": "Müşteri ID var mı?",
      "condition": "customerId missing",
      "skipNext": 2
    },
    {
      "type": "Respond",
      "template": "Müşteri numaranızı paylaşır mısınız? (ör. CST-1)"
    },
    {
      "type": "Branch",
      "label": "ID'siz çıkış",
      "condition": "customerId exists",
      "skipNext": 99
    },
    {
      "type": "Lookup",
      "label": "Tüm siparişleri getir",
      "tool": "get_all_orders_tool",
      "parameters": { "customerId": "$customerId" },
      "storeAs": "orders"
    },
    {
      "type": "Branch",
      "label": "Sipariş var mı?",
      "condition": "orders.success == false",
      "skipNext": 1
    },
    {
      "type": "Respond",
      "template": "📋 Müşteri {customerId} siparişleri:\n\n{orders}"
    }
  ]
}
```

### 9.6 Koşullu Yanıt (Branch + SetVariable Kombinasyonu)

Sipariş durumuna göre farklı mesaj üreten gelişmiş akış.

```json
{
  "name": "Sipariş Durumu Detaylı Yanıt",
  "description": "Sipariş durumuna göre farklı mesajlar üretir.",
  "isActive": true,
  "triggerKeywords": ["sipariş", "kargo", "teslim"],
  "inputPatterns": {
    "orderId": "(ORD-\\d+)"
  },
  "steps": [
    {
      "type": "Branch",
      "label": "orderId var mı?",
      "condition": "orderId missing",
      "skipNext": 2
    },
    {
      "type": "Respond",
      "template": "Sipariş numaranız nedir? (ör. ORD-1)"
    },
    {
      "type": "Branch",
      "label": "Çıkış",
      "condition": "orderId exists",
      "skipNext": 99
    },
    {
      "type": "Lookup",
      "label": "Durumu sorgula",
      "tool": "order_status_tool",
      "parameters": { "orderId": "$orderId" },
      "storeAs": "status"
    },
    {
      "type": "SetVariable",
      "label": "Ek bilgi notu",
      "variableName": "note",
      "variableValue": "Detaylı bilgi için sipariş numaranızla tekrar yazabilirsiniz."
    },
    {
      "type": "Respond",
      "template": "📦 {orderId} durumu:\n\n{status}\n\n💡 {note}"
    }
  ]
}
```

---

## 10. Test Kapsamı

### WorkflowExecutorTests (10 test)

| Test | Ne Test Eder |
|------|-------------|
| `Execute_InactiveWorkflow_ReturnsErrorWithoutSteps` | Pasif workflow reddedilir |
| `Execute_RespondStep_RendersTemplateWithVariables` | Template değişken substitution |
| `Execute_InputPattern_ExtractsVariableFromUserInput` | Regex ile değişken çıkarma |
| `Execute_BranchFalse_SkipsNextStep` | Koşul false → adım atlama |
| `Execute_BranchTrue_RunsNextStep` | Koşul true → normal devam |
| `Execute_SetVariable_RendersAndStores` | Değişken atama + template |
| `Execute_LookupForbiddenTool_StepHasError` | Yan etkili tool engellenir |
| `Execute_LookupOrderStatus_KnownOrderSucceeds` | Gerçek tool çağrısı başarılı |
| `EvaluateCondition_VariousOps` (6 case) | ==, !=, exists, missing operatörleri |
| `Render_UnknownPlaceholder_LeavesAsIs` | Bilinmeyen placeholder korunur |

### InMemoryWorkflowDefinitionStoreTests (7 test)

| Test | Ne Test Eder |
|------|-------------|
| `Empty_GetAll_ReturnsEmpty` | Boş store kontrolü |
| `Upsert_NoIdProvided_GeneratesSlugFromName` | Slug üretimi (Türkçe normalize) |
| `Upsert_ExistingId_IncrementsVersion` | Versiyon artırma |
| `Upsert_TracksUpdatedBy` | Audit trail |
| `GetActive_ExcludesInactive` | Pasif filtreleme |
| `Delete_RemovesEntry` | Silme + var olmayan silme |
| `Get_PreservesCreatedAtAcrossUpserts` | CreatedAt korunması |

---

## 11. Dosya Haritası

| Dosya | Rol |
|-------|-----|
| `Models/Workflow/WorkflowDefinition.cs` | Veri modeli (definition, step, result, trace) |
| `Services/Workflow/IWorkflowDefinitionStore.cs` | Store arayüzü |
| `Services/Workflow/InMemoryWorkflowDefinitionStore.cs` | In-memory store (ConcurrentDictionary) |
| `Services/Workflow/WorkflowExecutor.cs` | Deterministik yürütme motoru |
| `Endpoints/WorkflowEndpoints.cs` | Admin CRUD + test-run API |
| `wwwroot/workflow-designer.html` | Frontend sayfası |
| `wwwroot/js/workflow-designer.js` | Frontend davranışı |
| `Tests/.../WorkflowExecutorTests.cs` | Executor birim testleri |
| `Tests/.../InMemoryWorkflowDefinitionStoreTests.cs` | Store birim testleri |

---

## 12. Güçlü Yanlar ve İyileştirme Alanları

### Güçlü Yanlar

- LLM maliyeti sıfır — tamamen deterministik
- Güvenlik: Yan etkili tool'lar açıkça engellenmiş (HITL bypass önlemi)
- Auto-versioning ve audit trail (updatedBy)
- Türkçe slug desteği
- Test-run özelliği ile admin doğrudan sonucu görebilir
- Kapsamlı birim testleri

### İyileştirme Alanları

- **Chat entegrasyonu**: TriggerKeywords ile gerçek mesaj akışına bağlama
- **Görsel editör**: JSON textarea yerine drag & drop adım editörü
- **Loop/döngü desteği**: Sadece tek yönlü ileriye akış + skip var
- **Değişken tipleri**: Her şey string
- **Frontend validation**: JSON parse dışında schema kontrolü yok
- **Error recovery**: Hata sonrası fallback adım tanımlama yok
- **Sub-workflow**: Workflow'lar arası çağrı desteği yok
- **Persistence**: Postgres implementasyonu yok (restart'ta veri kaybı)
