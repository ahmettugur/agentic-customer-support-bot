# EscalationPolicyService

**Dosya:** `Services/Escalation/EscalationPolicyService.cs`
**Tür:** `public class` (Adapters'tan bağımsız, Application katmanında)
**Namespace:** `CustomerSupportBot.Application.Services.Escalation`

## 1. Ne İşe Yarar

Bir workflow turunun `ReasoningTrace`'inden **"insan gerekiyor mu?"** sorusunu cevaplayan iş
politikası: hangi specialist agent'ların "eskalasyon gerekli" işareti verdiğini bulur, aynı
oturumda zaten açık bir eskalasyon varsa tekrar açmaz (dedup), skills-based routing ile hangi
insan temsilciye yönlendirileceğine karar verir, önceliklendirir.

## 2. Hangi Amaçla Kullanılır

Bir workflow turu bittikten sonra (agent takımı çalışıp `ReasoningTrace` üretildikten sonra),
`ProcessPendingEscalationsAsync` çağrılır. Herhangi bir specialist agent'ın son
"post-tool reflection"ı `NeedsEscalation` durumundaysa, bir `EscalationRequest` kaydı açılır.

## 3. Sorumlulukları

- **Üstlendiği:** Adaylık belirleme (hangi agent'lar eskalasyon istedi), dedup (aynı
  oturum+agent için tekrar açmama), çoklu aday çakışmasında önceliklendirme (Complaint
  öncelikli), skills-based routing kararını uygulamak, öncelik yükseltme kuralları.
- **Üstlenmediği:** Eskalasyon kaydının kalıcılığı (`IEscalationSink`), routing algoritmasının
  kendisi (`ISkillsBasedRouter`), admin panelin eskalasyonu göstermesi/kararı
  ([`EscalationPortService`](EscalationPortService.md)).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Inject eder:** `IEscalationSink` (zorunlu), `IOptions<ApprovalOptions>` (zorunlu),
  `ISkillsBasedRouter?`, `IHumanAgentRegistry?`, `ICustomerProfileStore?`, `ISessionManager?`
  (hepsi opsiyonel — routing altyapısı yapılandırılmamışsa servis yine de temel dedup+kayıt
  işlevini sürdürür, sadece routing kararı atlanır).
- **Kimin tarafından çağrılır:** Workflow turu tamamlandıktan sonra (Adapters.Agents
  katmanındaki `WorkflowRunner`/`WorkflowTraceEventProcessor` zinciri).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **Dedup neden Session+Agent bazlı, sadece Session bazlı değil.** Eski davranış yalnızca
> `SessionId`'ye bakıyordu: aynı oturumda ZATEN açık bir eskalasyon varsa, farklı bir agent'tan
> gelen tamamen bağımsız bir eskalasyon isteği de bastırılıyordu. Örneğin bir Order eskalasyonu
> açıkken, aynı oturumda daha sonra bir Complaint eskalasyonu tetiklenirse bu HİÇ açılmıyordu —
> oysa ikisi farklı, birbirinden bağımsız sorunlardı. Dedup anahtarı `(SessionId, AgentName)`
> ikilisine genişletildi.

**Çoklu aday çakışması:** Bir turda birden fazla specialist agent aynı anda eskalasyon
isteyebilir (nadir ama mümkün). Bu durumda TÜMÜ değil, **tek bir** eskalasyon açılır — Complaint
agent'ı varsa o öncelikli (şikayetler tipik olarak daha kritik), yoksa listedeki son aday. Bu,
aynı oturum için birden fazla çakışan eskalasyon kaydının admin panelini kirletmesini önler.

**`ApplyRoutingDecisionAsync` neden `AuthenticatedCustomerId` kullanır, `State.CustomerId`
değil:** Kod içi yorumda açıkça belirtilir — eskalasyon yönlendirmesi (hangi profile göre
skill eşleştirmesi yapılacağı) **başkasının profiline göre yapılmamalıdır**. `AuthenticatedCustomerId`
JWT'den doğrulanmış, güvenilir kimliktir; `State.CustomerId` (varsa) potansiyel olarak
LLM/kullanıcı girdisinden türeyen, güvenilmez bir alan olabilirdi.

**Routing hatası eskalasyon oluşturmayı engellemez:** `ApplyRoutingDecisionAsync` içindeki
`try/catch`, routing kararı başarısız olsa bile (`_router` hata verirse) eskalasyon kaydının
YİNE DE oluşturulmasını garanti eder — bir routing algoritması arızası, müşterinin insan
desteğine ulaşma hakkını engellememelidir; sadece atanmamış/genel bir kayıt olarak kalır.

**Öncelik yükseltme kuralları:** Complaint agent'tan gelen her eskalasyon otomatik `High`
öncelik alır (şikayetler varsayılan olarak kritik kabul edilir); ayrıca routing eşleşme skoru
düşükse (`< 0.3`, yani "bu isteğe uygun bir temsilci bulunamadı") öncelik de yükseltilir —
düşük eşleşme, bu isteğin daha dikkatli/öncelikli insan müdahalesi gerektirebileceğinin bir
işaretidir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `ProcessPendingEscalationsAsync(ReasoningTrace trace, string userQuery, string finalResponse, CancellationToken ct = default): Task` | Trace'ten eskalasyon adaylarını bulur, dedup uygular, routing kararı alır, kayıt oluşturur. `ApprovalOptions.EscalationEnabled` kapalıysa hiçbir şey yapmaz. |
| `ApplyRoutingDecisionAsync(...)` *(private)* | `ISkillsBasedRouter` varsa çağırıp sonucu `EscalationRequest`'e yazar; öncelik yükseltme kurallarını uygular. |

## 7. Bağımlılıklar (Constructor Injection)

- `IEscalationSink` *(zorunlu)* — eskalasyon kaydı oluşturma ve açık kayıtları okuma.
- `IOptions<ApprovalOptions>` *(zorunlu)* — `EscalationEnabled` bayrağı.
- `ISkillsBasedRouter?` *(opsiyonel)* — skill eşleştirme.
- `IHumanAgentRegistry?` *(opsiyonel)* — yönlendirilen temsilcinin yükünü artırma.
- `ICustomerProfileStore?` *(opsiyonel)* — routing kararı için müşteri profili.
- `ISessionManager?` *(opsiyonel)* — `AuthenticatedCustomerId`'yi okumak için.
- `ILogger<EscalationPolicyService>?` *(opsiyonel, `NullLogger` varsayılan)*.

## Bağlantılar

- [EscalationPortService.md](EscalationPortService.md) — admin panelin eskalasyonları yönettiği servis
- [HitlEventPortService.md](HitlEventPortService.md) — eskalasyon olaylarının canlı yayını
