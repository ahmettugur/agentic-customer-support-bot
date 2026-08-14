# ApprovalGateService

**Dosya:** `CustomerSupportBot.Adapters.Agents/ApprovalGateService.cs`
**Yaşam döngüsü:** Singleton

## Ne işe yarar?

**HITL (Human-in-the-Loop)** onay kapısını uygular. Yan etkili 4 tool (sipariş oluşturma, sipariş iptali, iade talebi, şikayet kaydı) config'de işaretliyse, tool çağrısı **gerçek işi yapmaz**: bir onay kaydı oluşturur ve kullanıcıya "talebiniz onaya gönderildi" der. Gerçek iş, admin karar verdiğinde ayrıca tetiklenir.

Aynı sınıf ayrıca salt-okunur sipariş tool'larını da kurar (`order_status`, `get_last_order`, `get_all_orders`) — bunlar onay kapısından geçmez ama `customerId`'yi aynı güvenli kaynaktan (JWT) alır.

> 💡 **Analiz notu:** Bir bankadaki "çift imza" sistemi gibi düşün — büyük para transferi için kasiyerin tek başına işlem yapması yetmez, müdür de onaylamalı. Fark şu: kasiyer müdürü **beklemez**, "talebiniz işleme alındı" der ve sıradaki müşteriye geçer; müdür imzalayınca işlem gerçekleşir ve müşteriye bildirim gider.

---

## Kritik: Onay artık **bloklamıyor**

Bu, dokümantasyonun en çok yanlış hatırlanan noktası. Üç model üst üste yaşandı:

| Model | Nerede beklenir? | Durum |
|---|---|---|
| 1. Bloklayan lambda | Tool lambda'sının içinde `await AwaitDecisionAsync` | ❌ Kaldırıldı |
| 2. Framework native | `ApprovalRequiredAIFunction` → `RequestInfoEvent` → workflow superstep duraklaması | ❌ Bu 4 tool için kullanılmıyor |
| 3. **Bloklamayan (bugünkü)** | **Hiçbir yerde beklenmez** — tool hemen `ToolResult.Pending` döner | ✅ Aktif |

### Neden 2. modelden vazgeçildi?

Her iki bekleyen model de aynı sorunu paylaşıyordu: kullanıcının turu, bir insanın ne zaman karar vereceğine bağlıydı. 50–100 eşzamanlı onay biriktiğinde admin `TimeoutSeconds` (60sn) içinde yetişemiyor, istekler sessizce otomatik red'e düşüyordu — kullanıcı bunu fark etmeden yanlış sonuç alıyordu.

Bugünkü modelde tur hemen biter, kullanıcı sohbete devam eder, admin ne zaman onaylarsa onaylasın sonuç bir **bildirim/badge** olarak gelir.

### 2. modelin kalıntısı hâlâ kodda

`WorkflowRunner.HandleRequestInfoEventAsync` köprüsü ve `ApprovalGateService.RequestApprovalAsync` (bloklayan metot) **duruyor** ama bu 4 tool için **hiç tetiklenmiyor** — hiçbiri artık `ApprovalRequiredAIFunction` ile sarılmıyor, dolayısıyla `RequestInfoEvent` üretmiyorlar. Köprü, ileride biri bilerek bir tool'u o modelde sararsa çalışsın diye korunmuştur; testleri de (`RequestApprovalAsync_*`) o yolu doğrular.

> ⚠️ **Kodda arayacağınız yer:** Onay kapısının tamamı `ExecuteWithApprovalGateAsync` metodundadır. `RequestApprovalAsync`'i (ve `WorkflowRunner.HandleRequestInfoEventAsync`'i) okuyorsanız legacy yola bakıyorsunuz — ikisinin de XML dokümanı bunu açıkça söyler.

---

## Akış — bir sipariş isteği baştan sona

```
1. LLM order_placement_tool'u çağırır
        ↓
2. ExecuteWithApprovalGateAsync
   ├─ RequiresApproval(tool) == false → tool'u DOĞRUDAN çalıştır, bitti
   ├─ preflight() bir hata döndürdü   → onay kaydı OLUŞTURMA, hatayı hemen dön
   ├─ Aynı imzalı bekleyen kayıt var  → yenisini oluşturma, mevcudu kullan
   └─ IApprovalQueue.CreateAsync(req)
        ↓
3. ToolResult.Pending("Talebiniz onaya gönderildi (Kayıt: {id})...")
   → LLM bunu görür, ResponseAgent kullanıcıya iletir, TUR BİTER
        ↓
   ⏳ (dakikalar/saatler sonra, ayrı bir HTTP isteğinde)
        ↓
4. Admin panelden onaylar → IApprovalQueue.DecideAsync
        ↓
5. IApprovalExecutionRouter gerçek tool'u çalıştırır
        ↓
6. RequestDecided event → SSE → kullanıcının ekranında bildirim/badge
```

### `preflight` — neden var?

Gerçek iş admin kararından **sonra** çalıştığı için, baştan başarısız olacağı belli bir talep (ör. "1042'yi iptal et" ama 1042 diye bir sipariş yok, ya da başkasına ait) onay kuyruğuna düşer, admin'in zamanını harcar, onaylanır ve **ancak o zaman** sessizce başarısız olurdu.

`preflight`, onay kaydı **oluşturulmadan önce** çalışan salt-okunur bir ön kontroldür (`ValidateOrderActionable`). Yürütme anındaki kontrolün **yerine geçmez** — durum iki an arasında değişebilir; ikisi birlikte çalışır.

İptal/iade/şikayet tool'larında vardır; sipariş oluşturmada yoktur (doğrulanacak mevcut bir sipariş yok).

### Duplicate istek önleme

Aynı session'da, aynı tool için, aynı parametrelerle zaten **bekleyen** bir istek varsa yenisi oluşturulmaz. LLM tool çağrısını tekrarlarsa (retry davranışı) admin panelinde ikinci bir kart belirmez ve onaylandığında gerçek iş bir kez tetiklenir.

İmza `BuildParamSignature` ile üretilir: parametre sözlüğü alfabetik sıraya konup `anahtar=değer|anahtar=değer` biçiminde düzleştirilir.

---

## Çok ürünlü sipariş — neden tek tool çağrısı?

`order_placement_tool` bir ürün değil, bir **satır listesi** alır (`OrderLineRequest[] lines`). Alternatif tasarım (ürün başına bir tool çağrısı) kasıtlı olarak seçilmedi: her çağrı ayrı bir `ApprovalRequest` üretirdi, admin panelinde N ayrı kart görünürdü ve admin birini onaylayıp diğerini reddedebilirdi — müşteri **yarım bir sipariş** alırdı. Ayrıca duplicate-istek imzası ürün bazında farklılaştığı için bu çağrıları birbirine bağlayamazdı.

**Tek çağrı = tek onay kaydı = tek sipariş.** Admin ya sepetin tamamını onaylar ya da tamamını reddeder.

Bunun bir sonucu: `ApprovalRequest.Parameters["lines"]` bir **nesne dizisi** taşır — diğer tüm parametreler düz string/sayı. Bu iki yerde ayrıca ele alınmıştır:

- **Admin paneli** (`Admin.razor.cs` → `FormatArray`): dizi ham JSON değil, `productName=Kahve · quantity=2  |  productName=Çikolata · quantity=1` biçiminde gösterilir. Ham JSON gösterilseydi admin neyi onayladığını göremezdi ve HITL kapısı anlamını yitirirdi.
- **Onay sonrası yürütme** ([ApprovalExecutionRouter](../CustomerSupportBot.Application/Approval/ApprovalExecutionRouter.md) → `GetLines`): kayıt Postgres'ten hydrate edildiğinde dizi `JsonElement`'e döner; okunamazsa sipariş sessizce boş kalırdı.

---

## Güvenlik — `customerId` LLM parametresi değil

```csharp
private string CurrentCustomerId => _contextAccessor.Context?.CustomerId ?? "";
```

**Hiçbir** tool `customerId` diye bir parametre açmaz. Değer her zaman `IApprovalContextAccessor.Context`'ten, yani login'li kullanıcının JWT claim'inden gelir. Kullanıcı metinde başka bir müşteri numarası söylese bile (*"1008 numaralı müşteriyim"*) bu görmezden gelinir.

Bu, eskiden var olan "kullanıcı başkasının müşteri numarasını söyleyip onun adına işlem yaptırabilir" açığını kapatır. Aynı kimlik `ApprovalRequest.CustomerId` alanına da yazılır — böylece onay kaydı hangi hesaba ait olduğunu kanonik bir alanda taşır.

> `ExecuteWithApprovalGateAsync` `CustomerId`'yi doldurur; eski `RequestApprovalAsync` yolu doldurmaz (o yol bu 4 tool için kullanılmıyor).

---

## Hangi amaçla kullanılır?

`AgentTeamFactory`, `OrderAgent`/`ComplaintAgent` kurulurken bu servisin `Build*Tool()` metotlarını çağırıp dönen `AIFunction`'ları ilgili ajana tool olarak atar.

## Sorumlulukları

- 4 yan etkili tool için onay-kapılı `AIFunction` üretmek (`BuildOrderPlacementTool`, `BuildOrderCancelTool`, `BuildReturnRequestTool`, `BuildComplaintRegistrationTool`).
- 3 salt-okunur sipariş tool'unu üretmek (`BuildOrderStatusTool`, `BuildGetLastOrderTool`, `BuildGetAllOrdersTool`) — onay kapısından geçmez, `customerId`'yi JWT'den alır.
- `ExecuteWithApprovalGateAsync`: onay kaydını oluşturmak (veya mevcut bekleyeni yeniden kullanmak) ve **beklemeden** `ToolResult.Pending` dönmek.
- `ResolveAgentName(toolName)`: admin panelinde "hangi ajan istiyor" bilgisini göstermek için tool→ajan eşlemesi. Eşleme `WellKnown.SideEffectToolOwners` sözlüğünden okunur — burada elle sürdürülen ikinci bir switch **yoktur**.
- `ProcessPendingEscalationsAsync`: `EscalationPolicyService`'e delege ederek `needs_escalation` durumundaki trace'leri eskalasyon sink'ine yazdırmak.
- `RequestApprovalAsync`: (legacy) bloklayan onay yolu — `RequestInfoEvent` köprüsü için korunuyor.

**Üstlenmediği işler:** Onaydan sonra gerçek işi yürütmek (`IApprovalExecutionRouter`), admin arayüzü/SSE/SLA (`IApprovalQueue`, `ApprovalPortService`, `SlaGuardian`), yanıtsız kalan kayıtları temizlemek (`StaleApprovalSweepService`) — hepsi Application katmanındadır.

## Diğer katman ve bileşenlerle ilişkileri

**Implements:** Yok — somut bir servis sınıfı (arayüz yok, doğrudan enjekte edilir).

**Bağımlılıkları:** `IApprovalQueue` (Application outbound port), `ApprovalOptions` (config), `IEscalationSink`, `IApprovalContextAccessor` (ambient session/trace/query/customer bilgisi), `ICustomerSupportToolsService` (gerçek tool implementasyonları), `EscalationPolicyService`, `ILogger<ApprovalGateService>?`.

**Kimler çağırır:** `AgentTeamFactory` (constructor'da `OrderAgent`/`ComplaintAgent`'a geçirilir; `Team/OrderAgent.cs` ve `Team/ComplaintAgent.cs` `Build*Tool()` metotlarını doğrudan çağırır), `WorkflowRunner.HandleRequestInfoEventAsync` (`RequestApprovalAsync` — legacy yol), `WorkflowRunner`/`TurnFinalizer` (`ProcessPendingEscalationsAsync`).

## Kullanılma nedeni ve tasarım yaklaşımı

Bloklamayan modele geçiş hexagonal mimariyi bilinçli olarak korudu: Application katmanındaki `IApprovalQueue`/SSE/SLA altyapısı (dayanıklılık, cross-pod senkron, admin paneli) **hiç değişmedi** — yalnızca "kim ne zaman bekliyor" değişti. MAF-spesifik köprüleme bu adapter katmanında kaldı, Application katmanı framework-agnostic kalmaya devam etti.

**LLM'in `pendingApproval`'ı yanlış okuması riski:** `ToolResult.Pending` hem `Success=true` hem `PendingApproval=true` üretir. `Success=true` tek başına "iş oldu" demek DEĞİLDİR. Specialist prompt'ları (`order-agent.md`, `complaint-agent.md`) kararı açıkça `pendingApproval` alanına göre vermeye ve *"oluşturuldu"/"iptal edildi"* gibi kesin ifadeler kullanmamaya zorlanır.

**Red mesajı formatı (legacy yol):** `ApprovalRequiredAIFunction` ile sarılı bir tool reddedildiğinde LLM'e giden sonuç JSON zarfı DEĞİL, `FunctionInvokingChatClient`'ın sabit ürettiği düz bir string: `"Tool call invocation rejected. {reason}"` — özelleştirilemez (private metod, hook yok). Bu format `HitlRejectionFormatTests` ile kilitlenmiştir ve prompt'larda tanınır. **Bugünkü modelde bu 4 tool için ulaşılmaz**: red kararı turdan sonra geldiği için LLM'e hiç dönmez, kullanıcıya bildirim olarak ulaşır. Prompt talimatı, legacy yol yeniden kullanılırsa diye korunmuştur.

## Metotlar / Üyeler

| Üye | Açıklama |
| --- | --- |
| `BuildOrderPlacementTool()` | `order_placement_tool`. **Tek parametre: `lines` — `OrderLineRequest[]`** (her eleman `productName` + `quantity`). Onay kapısından geçer, `preflight` yok. |
| `BuildOrderCancelTool()` | `order_cancel_tool`, `orderId`/`reason`. Onay kapısı + `preflight: ValidateOrderActionable`. |
| `BuildReturnRequestTool()` | `return_request_tool`, `orderId`/`reason`. Onay kapısı + `preflight`. |
| `BuildComplaintRegistrationTool()` | `complaint_registration_tool`, `orderId`/`complaintText`. Onay kapısı + `preflight`. |
| `BuildOrderStatusTool()` | `order_status_tool`, `orderId`. **Salt-okunur — onay kapısı yok.** |
| `BuildGetLastOrderTool()` | `get_last_order_tool`, parametresiz. Salt-okunur. |
| `BuildGetAllOrdersTool()` | `get_all_orders_tool`, parametresiz. Salt-okunur. |
| `CurrentCustomerId` (private) | `IApprovalContextAccessor.Context?.CustomerId` — tüm tool'ların tek kimlik kaynağı. |
| `ExecuteWithApprovalGateAsync(toolName, parameters, executeDirectly, preflight?)` (private) | Onay kapısının tamamı: bypass → preflight → duplicate kontrolü → `CreateAsync` → `ToolResult.Pending`. |
| `RequiresApproval(toolName)` (private) | `ApprovalOptions.Enabled && ToolsRequiringApproval.Contains(toolName)`. |
| `ResolveAgentName(toolName)` (static) | `WellKnown.SideEffectToolOwners`'dan çözer; bulunamazsa `"UnknownAgent"`. |
| `RequestApprovalAsync(toolName, agentName, parameters, justification, ct)` | **Legacy bloklayan yol.** Onay talebi oluşturur/yeniden kullanır, `AwaitDecisionAsync` ile kararı bekler. İptal edilirse `(false, "İstek iptal edildi")`. |
| `ProcessPendingEscalationsAsync(trace, userQuery, finalResponse, ct)` | `EscalationPolicyService`'e delege eder. |
| `BuildParamSignature(parameters)` (private static) | Alfabetik sıralı deterministik imza (duplicate tespiti için). |
| `ApprovalDecisionResult` (record) | `(bool Approved, string? Reason)`. |

## Bağımlılıklar

Constructor injection: `IApprovalQueue approvalQueue`, `IOptions<ApprovalOptions> approvalOptions`, `IEscalationSink escalationSink`, `IApprovalContextAccessor contextAccessor`, `ICustomerSupportToolsService tools`, `EscalationPolicyService escalationPolicy`, `ILogger<ApprovalGateService>? logger = null`.

## `ApprovalOptions` yapılandırması

`appsettings.json` → `HumanInTheLoop:` bölümü:

```json
{
  "HumanInTheLoop": {
    "Enabled": true,
    "ToolsRequiringApproval": [
      "order_placement_tool",
      "order_cancel_tool",
      "return_request_tool",
      "complaint_registration_tool"
    ],
    "TimeoutSeconds": 60,
    "AutoApproveOnTimeout": false,
    "EscalationEnabled": true
  }
}
```

| Ayar | Varsayılan | Açıklama |
| ------ | --- | --------- |
| `Enabled` | `true` | `false` ise onay kapısı bypass edilir, tool'lar doğrudan çalışır (geliştirme ortamı) |
| `ToolsRequiringApproval` | 4 yan etkili tool | Hangi tool'ların onay gerektirdiği |
| `TimeoutSeconds` | `60` | ⚠️ **Artık bekleme süresi değil.** Bloklamayan modelde kimse beklemediği için yalnızca `ApprovalRequest.TimeoutSeconds` alanına metadata olarak yazılır. Bekleyen kayıtların ömrünü `StalePendingHours` belirler |
| `StalePendingHours` | `72` | Bu kadar saat yanıtsız kalan Pending kayıtları `StaleApprovalSweepService` otomatik reddeder |
| `AutoApproveOnTimeout` | `false` | Legacy bloklayan yol için |
| `EscalationEnabled` | `true` | Red/timeout durumunda eskalasyon oluşturulsun mu |

> **Dikkat:** Production'da `Enabled: false` olmamalı.
>
> **Tarihsel tuzak:** Admin panelinde bir dönem *"60 saniye sonra otomatik reddedilir"* yazıyordu ve bu **gerçekten oluyordu** — `Sla.Approvals.OnBreach` ayarı `AutoReject` olduğu için SLA guardian bekleyen onayları 60sn'de reddedip bloklamayan tasarımı fiilen bozuyordu. Varsayılan `None`'a çekildi ve `AppSettingsConfigTests` ile korunuyor (bkz. [SlaPortService](../CustomerSupportBot.Application/Sla/SlaPortService.md#slaapprovalsonbreach-varsayılanı--autoreject--none)).

## Yeni bir tool'u HITL kapısına bağlamak

1. `ApprovalGateService`'e yeni bir `Build___Tool()` metodu ekleyin — gövdesi `ExecuteWithApprovalGateAsync`'i çağırmalı (mevcut metotları örnek alın). Doğrulanabilir bir ön koşul varsa `preflight` geçin.
2. `ApprovalOptions.ToolsRequiringApproval` listesine yeni tool adını ekleyin.
3. `WellKnown.SideEffectToolOwners` sözlüğüne tool→ajan eşlemesini ekleyin — `ResolveAgentName` ve `HighRiskTools` buradan türetilir, ayrıca bir yer güncellemeniz gerekmez.
4. `ApprovalExecutionRouter.ExecuteAsync` switch'ine yeni tool'un case'ini ekleyin — **bu adım atlanırsa admin onaylar ama hiçbir şey çalışmaz.**
5. İlgili `Team/*Agent.cs` dosyasında yeni tool'u ajana atayın ve prompt'una `pendingApproval` davranışını ekleyin.

## Bağlantılar

- [../CustomerSupportBot.Application/Approval/ApprovalExecutionRouter.md](../CustomerSupportBot.Application/Approval/ApprovalExecutionRouter.md) — Onaydan sonra gerçek işi yürüten taraf
- [../CustomerSupportBot.Domain/Model/ApprovalRequest.md](../CustomerSupportBot.Domain/Model/ApprovalRequest.md) — Onay kaydı modeli
- [../CustomerSupportBot.Adapters.Persistence/StaleApprovalSweepService.md](../CustomerSupportBot.Adapters.Persistence/StaleApprovalSweepService.md) — Yanıtsız kalan kayıtları temizleyen servis
- [Team/OrderAgent.md](Team/OrderAgent.md) — Tool'ları tüketen ajan
- [../CustomerSupportBot.Web/Pages/Admin.md](../CustomerSupportBot.Web/Pages/Admin.md) — Onay kuyruğu arayüzü
