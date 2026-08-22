# CriteriaEvaluator

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/Evaluation/CriteriaEvaluator.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Adapters.Agents.Evaluation`

## Ne işe yarar?

`CriteriaEvaluator`, `evaluation-scenarios.yaml` dosyasında tanımlanan yapılandırılmış (typed) senaryo başarı kriterlerini (`success_criteria`), Microsoft Agents Framework (MAF) ve Microsoft Extensions AI değerlendirme tipleri (`EvalCheck`, `EvalItem`, `EvalCheckResult`, `FunctionEvaluator`) üzerinden analiz eden ve puanlayan kural motorudur.

## Hangi amaçla kullanılır`?

- Çoklu ajan yanıtlarının beklenen kelimeleri içerip içermediğini (`contains_any`).
- Belirli araçların çağrılıp çağrılmadığını (`tool_called`, `tool_not_called`, `tool_call_args_match`).
- Modelin eksik parametre durumunda kullanıcıdan alan isteyip istemediğini (`agent_requests_field`).
- Tur ve yineleme sınırlarının aşılıp aşılmadığını (`turn_count`, `iteration_count`).
- Yanıtta geçerli sipariş veya şikayet numarası dönülüp dönülmediğini (`order_id_returned`, `complaint_id_returned`) deterministik olarak ölçmek için kullanılır.

## Sorumlulukları

- **Üstlendiği:**
  - Kriter türlerine göre `Checks` tablosundaki `EvalCheck` fabrikalarını çalıştırmak.
  - `Evaluate` metodu ile TEK bir kriteri değerlendirip `CriterionResult` üretmek — `EvaluationRunner.RunSingleAsync` senaryodaki her kriter için bu metodu kendi `foreach` döngüsünde ayrı ayrı çağırır (bu sınıfta çoklu-kriter değerlendiren bir "EvaluateAll" metodu YOKTUR).
  - ReDoS saldırılarına karşı güvenli regex (`RegexTimeout = 500ms`) denetimi sağlamak.

## Metotlar ve İç Çalışma Mantıkları

### 1. `Evaluate`
```csharp
public static CriterionResult Evaluate(CriterionSpec spec, EvalItem item, ScenarioRunContext ctx)
```
- **Ne işe yarar?:** Tek bir başarı kriterini (`CriterionSpec`) çalıştırır.
- **İç Mantığı:** `Checks` sözlüğünde `spec.Type` anahtarını arar. Bulunursa `factory(spec, ctx)(item)` çağrılarak `EvalCheckResult` üretilir ve `CriterionResult`'a dönüştürülür. Bilinmeyen türlerde veya `manual_review`'da `Skipped = "manual_review_needed"` olarak işaretlenir.

### 2. Desteklenen Kriter Türleri (`Checks` Sözlüğü)

| Kriter Tipi | Değerlendirme Mantığı |
|---|---|
| `contains_any` | Yanıt metninin `spec.Values` listesindeki herhangi bir ifadeyi büyük/küçük harf duyarsız içermesi. |
| `tool_called` | MAF `EvalChecks.ToolCalledCheck` ile belirtilen araçların çağrılmış olması. |
| `tool_not_called` | Belirtilen araçların kesinlikle çağrılmamış olması. |
| `tool_call_args_match` | Çağrılan aracın argümanlarının beklenen değerlerle eşleşmesi. |
| `turn_count` / `iteration_count` | Tur sayısının belirtilen operatör (`<`, `<=`, `==`) ile eşleşmesi. |
| `no_extra_tool_calls` | Gerçekleşen araç sayısının beklenen araç sayısından fazla olmaması. |
| `no_missing_param_tool` | Eksik parametreli araç çalıştırma hatasının olmaması. |
| `agent_requests_field` | Yanıtın kullanıcıdan müşteri/sipariş no istemesi veya `awaiting_user_input` durumuna geçmesi. |
| `complaint_id_returned` | Yanıtta en az 4 haneli şikayet numarasının bulunması. |
| `order_id_returned` | Yanıtta en az 4 haneli sipariş numarasının bulunması. |
| `customer_id_used` | `SpecialistReasoning` içinde `customer_id` parametresinin araca iletildiğinin doğrulanması. |
| `order_status_contains` | Yanıt metninin "durum", "teslim", "işleniyor" veya "kargo" kelimelerinden birini içermesi. |
| `manual_review` | Otomatik değerlendirilemez; her zaman `Passed=false` + `Skipped="manual_review_needed"` ile işaretlenir (insan incelemesi gerekir). |

## Bağımlılıklar

- `Microsoft.Agents.AI.Evaluation.EvalCheck`
- `Microsoft.Agents.AI.Evaluation.EvalItem`
- `Microsoft.Agents.AI.Evaluation.FunctionEvaluator`
- [EvaluationScenario](../../CustomerSupportBot.Domain/Model/EvaluationScenario.md)
