# DependencyInjection

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/DependencyInjection/AgentsAdapterServiceCollectionExtensions.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Adapters.Agents.DependencyInjection`

## Ne işe yarar?

`AgentsAdapterServiceCollectionExtensions`, `CustomerSupportBot.Adapters.Agents` katmanında yer alan tüm servisleri, MAF ajan takımını (`CustomerSupportTeam` -> `IAgentTeamPort`), HITL onay kapısını (`ApprovalGateService`), dış sistemler için A2A kataloğunu (`A2AAgentCatalog`) ve senaryo değerlendirme motorunu (`EvaluationRunner` -> `IEvaluationPort`) `IServiceCollection` IoC konteynerine kaydeden DI uzantısıdır.

## Hangi amaçla kullanılır`?

Composition Root (`CustomerSupportBot.Api`) tarafında tek satırla (`services.AddAgentsAdapter()`) tüm ajan adaptör bağımlılıklarını güvenli şekilde ayağa kaldırmak ve `IChatClient` kaydının önceden yapıldığını doğrulamak (fail-fast) için kullanılır.

## Sorumlulukları

- **Üstlendiği:**
  - `IChatClient` kaydının varlığını denetlemek.
  - `ApprovalGateService`, `CustomerSupportTeam` ve `IAgentTeamPort` servislerini Singleton olarak kaydetmek.
  - `A2AAgentCatalog` ve `EvaluationRunner` (`IEvaluationPort`) servislerini kaydetmek.

## Metotlar / Üyeler

| Üye | Tür | İmza / Tanım | Açıklama |
|---|---|---|---|
| `AddAgentsAdapter` | Metot | `public static IServiceCollection AddAgentsAdapter(this IServiceCollection services)` | Ajan adaptörü servislerini ve port implementasyonlarını IoC konteynerine ekler. |

## Bağımlılıklar

- [IAgentTeamPort](../../CustomerSupportBot.Application/Ports/Outbound/IAgentTeamPort.md)
- [IEvaluationPort](../../CustomerSupportBot.Application/Ports/Inbound/IEvaluationPort.md)
- [CustomerSupportTeam](../CustomerSupportTeam.md)
- [ApprovalGateService](../ApprovalGateService.md)
- [A2AAgentCatalog](../A2A/A2AAgentCatalog.md)
- [EvaluationRunner](../Evaluation/EvaluationRunner.md)
