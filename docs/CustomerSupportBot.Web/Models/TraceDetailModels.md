# TraceDetailModels

## Ne İşe Yarar
Reasoning trace görüntüleme ve adım-adım replay bileşenlerinin model tanımlarını içerir.

## Hangi Amaçla Kullanılır
`TraceDetailPanel.razor` bileşeninde trace detayı gösterilirken ve `Replay.razor` sayfasında trace adımları oynatılırken kullanılır.

## Sorumlulukları
- Backend'den gelen trace detay verisini tipli olarak temsil etmek.
- Replay sayfası için adım bazlı polymorphic model hiyerarşisi sunmak.

## Diğer Katman ve Bileşenlerle İlişkileri
- **Kullanan servis**: [TracesApiService](../Services/TracesApiService.md).
- **Kullanan bileşenler**: `Components/TraceDetailPanel.razor`, `Pages/Replay.razor`.
- **İlişkili helper**: [JsonExtensions](../Helpers/JsonExtensions.md) — `Reasoning` ve `Planning` `JsonElement?` alanlarına güvenli erişim.

## Üyeler

### Trace Detay
| Sınıf/Record | Açıklama |
|-------------|----------|
| `TraceDetail` | Ana trace verisi: TraceId, SessionId, UserQuery, StartedAt, CompletedAt, DurationMs, IterationCount, Error, FinalResponse, Reasoning (JsonElement), Planning (JsonElement), AgentVisits, SpecialistReasonings, ToolCalls, WasRevised, FirstDraftResponse, FinalCritique. |
| `TraceAgentVisit` | Agent ziyareti: AgentName, StartedAt, DurationMs, Output. |
| `TraceToolCall` | Tool çağrısı: ToolName, AgentName, InvokedAt, Success, ParametersSummary, ResultSummary. |

### Replay Modelleri
| Record | Açıklama |
|--------|----------|
| `ReplayStepPayload` (abstract) | Replay adım payload'unun base tipi. |
| `ReplayInitPayload` | Başlangıç adımı: TraceId, SessionId, UserQuery. |
| `ReplayFinalPayload` | Bitiş adımı: TerminationReason, DurationMs, IterationCount, Error, Response. |
| `ReplayToolPayload` | Tool çağrısı adımı: ToolName, AgentName, Success, Parameters, Result. |
| `ReplayAgentPayload` | Agent ziyareti adımı: AgentName, DurationMs, Output. |
| `ReplayJsonPayload` | Ham JSON adımı: Json. |
| `ReplayStep` | Tek bir replay adımı: Kind, Time, Title, Payload. |

## Kullanılma Nedeni ve Tasarım Yaklaşımı
`Reasoning` ve `Planning` alanları `JsonElement?` olarak tutulur çünkü bu veriler yarı-yapılandırılmıştır ve her trace'de farklı yapıda olabilir. Replay modelleri abstract record inheritance ile polymorphism sağlar — `ReplayStep.Payload` tipi runtime'da concrete tipe göre render edilir.

## Bağımlılıklar
- `System.Text.Json` — `JsonElement` tipi.
