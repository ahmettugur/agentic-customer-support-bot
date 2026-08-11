# SlaPortService

**Dosya:** `Services/Sla/SlaPortService.cs`

## 1. Ne İşe Yarar

SLA Guardian'ı orkestre eder — bekleyen onay ve eskalasyonları periyodik tarar, konfigüre edilen süre aşılırsa SlaEvent oluşturur ve otomatik aksiyon alır (auto-reject, auto-escalate).

## 2. Hangi Amaçla Kullanılır

Background hosted service olarak çalışır. `SlaOptions` konfigürasyonundaki thresholds'a göre warn/breach olayları üretir.

> 💡 **Analiz notu:** Bir pizzacının "30 dakikada gelmezse bedava" sistemi — zaman aşımı kontrolü.

## `Sla.Approvals.OnBreach` varsayılanı — `AutoReject` → `None`

`ApprovalSlaOptions.OnBreach`'in varsayılanı eskiden `AutoReject`'ti. Bu, HITL onayları hâlâ **bloklayan** modeldeyken (tool çağrısı `AwaitDecisionAsync` ile admin kararını bekliyordu, `ApprovalOptions.TimeoutSeconds`=60sn) o timeout'u yedekleyen ikinci bir uygulama katmanıydı. Onaylar bloklamayan modele taşındığında (`ApprovalGateService.ExecuteWithApprovalGateAsync` — tool artık kararı beklemeden hemen "onaya gönderildi" döner, kayıt admin karar verene ya da `ApprovalOptions.StalePendingHours` — varsayılan 72 saat — aşılana kadar kuyrukta kalır) bu SLA config'i güncellenmeyi unutulmuştu.

**Sonuç:** `ScanOnceAsync` her `PollIntervalSeconds`de (5sn) tüm bekleyen onayları tarıyor, `BreachAfterSeconds`i (60sn) aşanları `AutoReject` ile **gerçekten reddediyordu** — admin panelinde `Onay Bekleyen Tool Çağrıları` sekmesindeki *"60 saniye sonra otomatik reddedilir"* metni bu yüzden hâlâ doğru bir sayı söylüyordu, ama söylediği davranışın kendisi bloklamayan modelin "admin ne zaman bakarsa baksın" amacını fiilen geçersiz kılıyordu.

Varsayılan artık `None` — `BreachAfterSeconds` aşılınca yalnızca bir `SlaEvent` (breach) kaydı üretilir, admin panelinde bir uyarı rozeti olarak görünür, karar verilmez. `AutoReject`/`AutoApprove` hâlâ opsiyonel bir operatör kararı olarak duruyor (`appsettings.json` → `Sla.Approvals.OnBreach`), varsayılan değil. Regresyon koruması: `SlaGuardianServiceTests.ScanOnce_Approval_DefaultOptions_BreachDoesNotAutoReject`.

> ⚠️ **Bu düzeltme ilk seferinde eksik kaldı.** `appsettings.json`'daki `OnBreach` `None`'a çekildi ama `CustomerSupportBot.Api/appsettings.Development.json`'daki AYRI kopyası unutuldu — ASP.NET Core config layering'i Development ortamında (dotnet run'ın varsayılanı, bkz. `launchSettings.json`) bu dosyayı base'in **üzerine** yazdığı için gerçek çalışan davranış hâlâ `AutoReject`'ti; kullanıcı düzeltmeden sonra bile 60 saniyede otomatik red gözlemledi. İki dosya da düzeltildi ve `AppSettingsConfigTests` (`CustomerSupportBot.Api.Tests/Infrastructure`) ikisinin senkron kalmasını zorunlu kılıyor — bu, "yalnızca base appsettings.json'ı test eden" `PromptContractTests`'in kaçırdığı bir sınıf hatadır (Development kopyası hiç okunmuyordu).

## Bağlantılar

- [SlaPolicyEvaluator.md](SlaPolicyEvaluator.md) — SLA kural değerlendirmesi
- [../../CustomerSupportBot.Domain/Model/SlaEvent.md](../../CustomerSupportBot.Domain/Model/SlaEvent.md) — SLA olay modeli
