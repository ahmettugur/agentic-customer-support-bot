# PostgresAttachmentStore

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/Postgres/PostgresAttachmentStore.cs`
- **Tür:** `public sealed class : IAttachmentStore`
- **Tablo:** `chat.attachments` (migration `AddChatAttachments`)

## Ne işe yarar?

Sohbet fotoğraflarını (`bytea`) saklar. Diğer store'ların aksine **bellek içi cache tutmaz**:
görüntü verisi büyüktür, her pod'da kopyası tutulmamalı; okumalar seyrektir.

| Üye | Açıklama |
|---|---|
| `SaveAsync` | Kayıt. `session_id` → `chat.sessions` FK, **cascade**: oturum silinince fotoğraflar da silinir. |
| `GetAsync` | Görüntü verisiyle tek kayıt. |
| `ListForSessionAsync` | Oturumun fotoğrafları, eskiden yeniye — görüntü verisi **okunmaz** (yalnızca meta sütunlar). |
| `MarkSentAsync` | `sent_at IS NULL` koşullu tek UPDATE — ilk gönderim zamanı korunur. |
| `DeleteUnsentAsync` | `sent_at IS NULL` koşullu tek DELETE — gönderimle yarışan silme gönderilmiş fotoğrafı silemez. |
| `LinkToApprovalAsync` | `approval_id IS NULL` koşullu tek UPDATE — eşzamanlı iki onaydan ilk bağlayan kazanır. |

Koşullu UPDATE/DELETE (`ExecuteUpdate/ExecuteDelete`) EF InMemory sağlayıcısında yoktur; HTTP
entegrasyon testleri bu yüzden `InMemoryAttachmentStore` kullanır, bu sınıf
`PostgresAttachmentStoreTests`'te gerçek Postgres (Testcontainers) ile sınanır.
