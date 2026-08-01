# EvaluationRunner

**Dosya:** `CustomerSupportBot.Adapters.Agents/Evaluation/EvaluationRunner.cs`
**Implements:** `IEvaluationPort` (Application katmanı portu)
**Yaşam döngüsü:** Singleton

## Ne işe yarar?

`docs/evaluation-scenarios.yaml`'daki senaryoları sistem üzerinde otomatik çalıştırır: her senaryo için izole bir session açar, reasoning + workflow akışını koşturur, trace'i inceler ve `CriteriaEvaluator` ile başarı kriterlerini değerlendirir.

## Hangi amaçla kullanılır?

`EvaluationEndpoints` (`GET /eval/scenarios`, `POST /eval/run`, `POST /eval/run/{id}`), admin panelinden veya API'den tetiklenir — regresyon testi, yeni ajan/tool doğrulaması ve prompt değişikliği etkisini ölçmek için kullanılır.

## Sorumlulukları

- `RunAsync(scenarios, ct)`: senaryoları sırayla çalıştırır, `EvaluationRunResult` (toplam/geçen/kalan/kısmi sayıları + `PassRate`) üretir.
- `RunScenarioAsync(scenario, ct)`: tek bir senaryoyu izole session'da çalıştırır —
  1. `_sessionManager.GetOrCreate(null)` ile yeni session.
  2. `_reasoningService.ReasonAsync` (PlanningAgent ön-analiz bağlamı için önce çalıştırılır).
  3. `_team.RunAsync` (`IAgentTeamPort` — gerçek workflow).
  4. `_traceStore.GetBySession(...)`'dan son trace'i alıp sonuçları toplamak.
  5. `trace.ToolCalls`'tan gerçek tool adı listesini çıkarmak (**önceki heuristic'in yerine** — bkz. tasarım notu).
  6. `EvalChecks.ToolCalledCheck` gibi built-in'lerin çalışabilmesi için sentetik bir `EvalItem.Conversation` kurmak (`trace.ToolCalls`'tan türetilen `FunctionCallContent`'li mesajlar).
  7. Her `success_criteria` girdisini `CriteriaEvaluator.Evaluate` ile değerlendirmek.
  8. `expected_intent` varsa otomatik intent-match kontrolü eklemek.

**Üstlenmediği işler:** Kriterlerin gerçek değerlendirme mantığı (`CriteriaEvaluator`), YAML yükleme (`ScenarioLoader` — `CustomerSupportBot.Api/Infrastructure/`), HTTP sözleşmesi (`EvaluationEndpoints`).

## Diğer katman ve bileşenlerle ilişkileri

**Implements:** `CustomerSupportBot.Application.Ports.Inbound.IEvaluationPort`.

**Bağımlılıkları:** `IAgentTeamPort` (gerçek workflow koşusu — somut tipten habersiz), `IReasoningPort`, `ISessionManager`, `IReasoningTraceStore` — dördü de Application port'ları, framework-agnostic.

**Kimler çağırır:** `EvaluationEndpoints` (`CustomerSupportBot.Api`), DI kaydı `AgentsAdapterServiceCollectionExtensions.AddAgentsAdapter`'da.

**Ne kullanır:** `CriteriaEvaluator` (aynı klasör), `Microsoft.Agents.AI.EvalItem`/`ExpectedToolCall`, `Microsoft.Extensions.AI.ChatMessage`/`FunctionCallContent`.

## Kullanılma nedeni ve tasarım yaklaşımı

Bu sınıfın kendisi `IAgentTeamPort`/`IReasoningPort`/`ISessionManager`/`IReasoningTraceStore` gibi soyutlamalar üzerinden çalıştığı için MAF'a doğrudan bağımlı DEĞİLDİ — `Adapters.Agents`'a taşınmasının tek sebebi, `CriteriaEvaluator.Evaluate`'i çağırmak için gereken `EvalItem`/`ChatMessage`/`FunctionCallContent` inşasıdır (bkz. [Evaluation/README.md](README.md)'deki hexagonal mimari notu).

**Tool listesi doğruluğu düzeltmesi:** Önceki tasarımda `ToolsCalled`, `SpecialistReasonings`'den agent-adı → tool-adı **heuristic**'iyle tahmin ediliyordu (kod yorumunda "Hangi tool çağrıldı bilinmiyor — agent adından türet" deniyordu — ör. `OrderAgent` → her zaman `order_status_tool` varsayılırdı, oysa 6 farklı tool'dan biri olabilirdi). `ReasoningTrace.ToolCalls` (`List<ToolInvocation>`) zaten gerçek `ToolName`'i tutuyordu ama kullanılmıyordu. `CriteriaEvaluator` redesign'ının `EvalItem.Conversation`'ı doğru sentezlemesi için doğru veri gerektiği için bu heuristic gerçek veri kaynağıyla değiştirildi — hem doğruluk düzeltmesi hem redesign'ın önkoşuluydu.

## Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `RunAsync(scenarios, ct)` | Tüm senaryoları sırayla çalıştırır, `EvaluationRunResult` döner. |
| `RunScenarioAsync(scenario, ct)` | Tek senaryoyu izole session'da çalıştırır, `ScenarioResult` döner. |
| `SimplifyAgentName(name)` (private static) | Agent adından `_` sonrası (varsa) kısmı atarak sadeleştirir (trace görünümü için). |

## Bağımlılıklar

Constructor injection: `IAgentTeamPort team`, `IReasoningPort reasoningService`, `ISessionManager sessionManager`, `IReasoningTraceStore traceStore`.
