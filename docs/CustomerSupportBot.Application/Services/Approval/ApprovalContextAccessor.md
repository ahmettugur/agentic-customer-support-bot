# ApprovalContextAccessor

**Dosya:** `Services/Approval/ApprovalContextAccessor.cs`
**Tür:** `public sealed class : IApprovalContextAccessor`
**Namespace:** `CustomerSupportBot.Application.Services.Approval`

## 1. Ne İşe Yarar

`IApprovalContextAccessor` port'unun `AsyncLocal<T>` tabanlı implementasyonu — bir workflow
turunun **hangi oturuma, hangi trace'e, hangi müşteriye ait olduğunu**, o turun tüm async
çağrı zinciri boyunca (agent → tool → tool içindeki başka bir await) taşıyan "ambient context"
mekanizması. ASP.NET Core'daki `HttpContext.Items` benzeri, ama HTTP isteğiyle değil,
**bir workflow turunun yürütme akışıyla** kapsamlı.

## 2. Hangi Amaçla Kullanılır

Bir tool metodu (ör. `OrderCancelTool`) LLM tarafından çağrıldığında, tool'un "hangi müşteri
adına çalıştığını" bilmesi gerekir. Bu bilgi LLM'e **parametre olarak sorulmaz** (LLM'in
serbest metinden `customerId` uydurmasına izin vermek güvenlik açığıdır); bunun yerine
[`ApprovalGateService`](../../../../CustomerSupportBot.Adapters.Agents/ApprovalGateService.md)
bir turun başında `SetScope(...)` ile context'i kurar, tool içindeki kod ise
`IApprovalContextAccessor.Context.CustomerId`'yi okur.

## 3. Sorumlulukları

- **Üstlendiği:** Context'i `AsyncLocal` alanında tutmak, kapsam (`scope`) açılıp kapandığında
  önceki değere geri dönmek (`IDisposable` ile), agent adı/trace id gibi tekil alanları
  kapsamı yeniden açmadan güncelleyebilmek (`SetCurrentAgent`, `SetTraceId`).
- **Üstlenmediği:** Context'in İÇERİĞİNİN doğruluğunu doğrulamak (bu, context'i kuran tarafın
  — `ApprovalGateService`'in — sorumluluğudur); bu sınıf sadece bir taşıyıcıdır.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IApprovalContextAccessor` port'unu implemente eder (`Ports/Outbound/IApprovalContextAccessor.cs`).
- Kapsamı kimin açtığı: `ApprovalGateService` (Adapters.Agents katmanı), her workflow turunun
  başında `SetScope` çağırır.
- Kapsamı kimin okuduğu: `OrderToolsService`/`ComplaintToolsService` gibi tool servisleri ve
  [`ApprovalExecutionRouter`](ApprovalExecutionRouter.md) (onay kararı sonrası gerçek işi
  tetiklerken müşteri kimliğini buradan değil `ApprovalRequest.CustomerId`'den okur — orası
  zaten kalıcı kayıtta saklı bir alan).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**Neden `AsyncLocal<T>` ve neden statik alan:** `AsyncLocal<T>`, .NET'in bir `Task`/`async`
zincirinin çatallandığı her noktada değeri **otomatik olarak kopyalayan**, ama zincirler
birbirinden bağımsız kaldığı sürece birbirini **etkilemeyen** özel bir depolama mekanizmasıdır.
Bu, uygulamanın aynı anda birden çok workflow turunu (farklı oturumlar, farklı müşteriler)
paralel işleyebildiği bu kod tabanında kritik: statik bir `ApprovalContext` alanı kullanılsaydı,
iki eşzamanlı turun context'leri birbirine karışırdı (turun A'nın tool'u, turun B'nin
müşteri kimliğini görebilirdi — ciddi bir veri sızıntısı). `AsyncLocal` sayesinde her turun
kendi async akışı kendi kopyasını görür.

`SetScope` bir `IDisposable` döner ve önceki değeri saklar: bu, `using (accessor.SetScope(...))`
deseniyle kapsamın **iç içe geçebilmesini** (nested scope) ve kapsam kapandığında önceki
duruma (genelde `null`) güvenle dönülmesini sağlar — bir workflow bir alt-workflow tetiklerse
alt-workflow'un context'i üst workflow'unkini kalıcı olarak ezmez.

`SetCurrentAgent`/`SetTraceId`, mevcut context `null` olsa bile çalışır (yoksa yeni bir context
yaratır) — bu, agent yönlendirmesi (routing) context kurulmadan önce başlarsa `NullReferenceException`
yerine sessizce eksik bir context oluşmasını sağlar.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Context` (`ApprovalContext?`, salt okunur) | Mevcut async akışın context'i; kapsam açılmamışsa `null`. |
| `SetScope(string? sessionId, string? traceId, string? userQuery, string? customerId = null): IDisposable` | Yeni bir context kapsamı açar, `Dispose()` çağrıldığında önceki context'e döner. |
| `SetCurrentAgent(string? agentName): void` | Mevcut context'te (varsa) `AgentName`'i günceller; yoksa yeni bir context oluşturur. |
| `SetTraceId(string? traceId): void` | Mevcut context'te (varsa) `TraceId`'yi günceller; yoksa yeni bir context oluşturur. |

## 7. Bağımlılıklar

Yok — dışarıdan hiçbir servis inject etmez; sadece `AsyncLocal<ApprovalContext?>` statik alanını yönetir.

## Bağlantılar

- [ApprovalExecutionRouter.md](ApprovalExecutionRouter.md) — onay sonrası gerçek işi tetikleyen taraf
- [ApprovalPortService.md](ApprovalPortService.md) — admin panelin onay kuyruğunu yönettiği servis
