# AdminModels

## Ne İşe Yarar
Admin panelinin tüm DTO ve record tanımlarını içeren model dosyasıdır. Backend API yanıtlarının deserialize edilmesi için kullanılır.

## Hangi Amaçla Kullanılır
[AdminApiService](../Services/AdminApiService.md), [AnalyticsApiService](../Services/AnalyticsApiService.md), [SlaApiService](../Services/SlaApiService.md) ve [TracesApiService](../Services/TracesApiService.md) tarafından API yanıtlarının tipli olarak alınmasında kullanılır.

## Sorumlulukları
Yalnızca veri taşıma — iş mantığı içermez.

## Diğer Katman ve Bileşenlerle İlişkileri
- **Backend karşılığı**: Bu record'lar `CustomerSupportBot.Api`'deki endpoint yanıt şemalarının mirror'ıdır.
- **Kullanan servisler**: `AdminApiService`, `AnalyticsApiService`, `SlaApiService`, `TracesApiService`.
- **Kullanan sayfalar**: `Admin.razor`, `Sla.razor`.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Blazor WASM projesinde backend model DLL'lerine referans verilmez (izolasyon). Bu nedenle API şemaları client-side record'lar olarak yeniden tanımlanır. Record kullanımı immutability ve value-based equality sağlar.

> ⚠️ **Bu mirror'ın kırılganlığı:** Alan adları API JSON'ıyla System.Text.Json'ın
> case-insensitive eşleştirmesiyle uyuşmalı — yalnızca büyük/küçük harf farkı tolere
> edilir, farklı bir ad **sessizce default değere** deserialize olur (hata fırlatmaz).
> `SlaApprovalStats.WarnAfter`/`BreachAfter` tam olarak bu şekilde kırılmıştı (API
> `warnAfterSeconds`/`breachAfterSeconds` dönüyordu); ayrıntı için
> [Sla.md](../Pages/Sla.md#erişim). Yeni bir alan eklerken adın API'deki adla (case hariç)
> birebir eşleştiğini doğrulayın.

## Üyeler

### Approvals
| Record | Alanlar |
|--------|---------|
| `ApprovalRequest` | Id, ToolName, AgentName, UserQuery, SessionId, Parameters, Status, RequestedAt, DecidedAt, DecidedBy, DecisionReason, ReasonRequired, Justification, TraceId, TimeoutSeconds |

### Escalations
| Record | Alanlar |
|--------|---------|
| `EscalationRequest` | Id, AgentName, AssignedTo, Reason, UserQuery, SessionId, Status, CreatedAt, ResolvedAt, Resolution, MissingContext, ResponseSummary |

### Chat Sessions
| Record | Alanlar |
|--------|---------|
| `ActiveChatSession` | SessionId, HumanAgent, MessageCount, SentimentLabel, SentimentScore, EnteredHumanModeAt |
| `ChatHistoryMessage` | Sender, Text, Timestamp |

### Analytics
| Record | Alanlar |
|--------|---------|
| `AnalyticsDashboard` | TotalSessions, TotalMessages, AverageRating, TotalRatings, RatingDistribution, SentimentDistribution, IntentDistribution, PhaseDistribution, ApprovalStats, EscalationStats, RecentRatings |
| `ApprovalStats` | Total, Approved, Rejected, TimedOut |
| `EscalationStats` | Total, Resolved, Dismissed |
| `RecentRating` | SessionId, Stars, Feedback, RatedAt |
| `SessionSummary` | SessionId, LastActivity, MessageCount, Title |
| `SessionAnalyticsModel` | 20+ alan — sentiment timeline, approval/escalation detayları dahil |

### Agents & SLA
| Record | Alanlar |
|--------|---------|
| `AgentInfo` | Id, DisplayName, IsActive |
| `SlaStatus` | Enabled, PollIntervalSeconds, Approvals, Escalations |
| `SlaApprovalStats` | PendingCount, OldestSeconds, WarnAfterSeconds, BreachAfterSeconds, OnBreach, BreachCountRecent |
| `SlaEscalationStats` | OpenCount, OldestSeconds, WarnAfterSeconds, BreachAfterSeconds, BoostPriorityOnBreach, BreachCountRecent |
| `SlaEvent` | Timestamp, Kind, Severity, TargetId, Action, AgeSeconds, Note |
| `SlaEventsResponse` | TotalCount, Items |

### Improvements
| Record | Alanlar |
|--------|---------|
| `LessonProposal` | Id, Title, LessonText, Observation, SuggestedAgent, SourceTraceIds, Status, DecidedBy, DecidedAt, DecisionReason, VectorMemoryId |

## Bağımlılıklar
Yok — saf DTO/record tanımları.
