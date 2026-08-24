# IApprovalContextAccessor (+ ApprovalContext)

**Kaynak:** `Ports/Outbound/IApprovalContextAccessor.cs`
**Implementasyon:** [`ApprovalContextAccessor`](../../Services/Approval/ApprovalContextAccessor.md)

## 1. Ne İşe Yarar

Bir turun akışı boyunca taşınan "ambient" (dolaylı, parametre olarak geçirilmeyen) bağlama
erişim için secondary port: hangi oturum, hangi trace, hangi kullanıcı sorgusu, hangi
müşteri kimliği, şu an hangi uzman ajan çalışıyor.

## 2. Hangi Amaçla Kullanılır

Application servisleri turun başında `SetScope` ile bağlamı kurar; HITL adaptörü
(`ApprovalGateService`) ve tool fonksiyonları (örn.
[`IUiHintEmitter`](IUiHintEmitter.md)) bu bağlamı **parametre almadan** okuyabilmek için
`Context`'i kullanır — LLM'in tool çağrısına `sessionId`/`customerId` eklemesini beklemek
yerine, bunlar zaten güvenilir bir kaynaktan (login/JWT) ambient bağlama yazılmış olur.

## 3. Sorumlulukları

- **Üstlendiği:** Bağlamı set etme (`SetScope`), okuma (`Context`), turun ortasında güncelleme
  (`SetCurrentAgent`, `SetTraceId`).
- **Üstlenmediği:** Bağlamın NASIL saklandığı (AsyncLocal, ConcurrentDictionary vb.) — bu
  implementasyon detayıdır; port yalnızca sözleşmeyi tanımlar.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.Agents/ApprovalGateService`'in **tüm 4 onay-gerektiren tool'unun** `customerId`
parametresini artık LLM'den değil `Context.CustomerId`'den aldığı, güvenlik açısından kritik
bir port'tur (bkz. [`IOrderToolsService`](IOrderToolsService.md) içindeki "customerId LLM
parametresi DEĞİL" notları).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`SetCurrentAgent`'in var olma nedeni: tool çağrılarının (ör. `IUiHintEmitter.Emit`)
ürettikleri event'i doğru ajana etiketlemesi gerekir, ama bunu stream event zamanlamasına/
sırasına bağlı kalarak çıkarsamak kırılgan olurdu — bu yüzden mevcut ambient bağlamda
fiilen çalışan ajanın adı açıkça güncellenir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `ApprovalContext? Context { get; }` | Mevcut ambient bağlam (yoksa `null`). |
| `IDisposable SetScope(string? sessionId, string? traceId, string? userQuery, string? customerId = null)` | Yeni bir kapsam açar; dispose edildiğinde önceki kapsama döner. |
| `void SetCurrentAgent(string? agentName)` | Şu an çalışan uzman ajanın adını günceller. |
| `void SetTraceId(string? traceId)` | Trace kimliğini bağlar (trace oluşturulduktan sonra). |

**`ApprovalContext(string? SessionId, string? TraceId, string? UserQuery, string? AgentName = null, string? CustomerId = null)`**

## 7. Bağımlılıklar

Yok — port arayüzü bağımlılıksızdır.
