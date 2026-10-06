# PostgresVoiceCallStore

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/Postgres/PostgresVoiceCallStore.cs`
- **Tür:** `public sealed class : IVoiceCallStore`
- **Tablo:** `voice.calls` (migration `AddVoiceCalls`)

## Ne işe yarar?

Sesli görüşme kayıtlarını saklar.

| Üye | Açıklama |
|---|---|
| `TryCreateAsync` | INSERT; `ux_calls_agent_open` / `ux_calls_session_open` ihlalinde `false` (temsilci ya da oturum zaten görüşmede). |
| `TryUpdateAsync(call, expectedStatus)` | `status = expected` koşullu tek UPDATE; yarışı kaybeden `false` alır. |
| `TouchChunkAsync` | Son parça zamanı — zaman aşımı süpürmesi bunu kullanır. |
| `GetOpenForAgentAsync`, `ListOpenAsync`, `ListForSessionAsync` | Açık görüşmeler ve oturum geçmişi. |

Koşullu güncellemeler EF InMemory'de olmadığından API entegrasyon testleri `InMemoryVoiceCallStore`
kullanır; bu sınıf `PostgresVoiceStoresTests`'te Testcontainers ile sınanır.
