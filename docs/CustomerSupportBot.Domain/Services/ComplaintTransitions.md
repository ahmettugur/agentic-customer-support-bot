# ComplaintTransitions

**Dosya:** `Services/ComplaintTransitions.cs`

Şikayet durum geçişlerinin **saf** kuralları. Veritabanı, saat ya da bildirimden habersizdir; `ComplaintManagementService`
uygular, panel düğmeleri (`AvailableActions`) buradan türetilir — kural tek yerde durur.

## Durumlar

`Beklemede → İncelemede → Çözüldü | Reddedildi`; kapanmış şikayet yeniden açılabilir (`İncelemede`'ye döner).
Sabitler `WellKnown.ComplaintStatuses` (`IsOpen`, `IsClosed` yardımcılarıyla).

## Geçiş tablosu

| Eylem | Nereden | Nereye | Kim | Zorunlu girdi |
|---|---|---|---|---|
| `Claim` | Beklemede | İncelemede | Temsilci (kendine) | — |
| `Assign` | Beklemede / İncelemede | İncelemede | Admin | atanacak temsilci |
| `Resolve` / `Reject` | İncelemede | Çözüldü / Reddedildi | Admin ya da atanan temsilci | müşteriye görünen açıklama (≤ 2000) |
| `Reopen` | Çözüldü / Reddedildi | İncelemede | Admin ya da atanan temsilci | gerekçe (≤ 2000) |
| `Note` | herhangi | (değişmez) | Admin ya da atanan temsilci | not (≤ 2000) |

**Denetim sırası:** önce durum (`invalid_transition`), sonra yetki (`forbidden`), sonra girdi (`text_required`,
`too_long`, `assignee_required`). Yeniden açma yetkisi "atanan temsilci"dir: şikayeti kapatan ya admin ya da atanan
temsilci olabildiği için "kapatan temsilci" ile aynı kümeyi verir ve olay geçmişine bakmayı gerektirmez.

## Üyeler

| Üye | Açıklama |
|---|---|
| `Check(action, complaint, actor, assigneeAgentId, text)` | `ComplaintTransitionCheck(Allowed, TargetStatus, Error)`. |
| `AvailableActions(complaint, actor)` | Durum + yetkiye göre sunulabilecek eylemler (girdi hariç) — API `actions` alanı. |
| `ActionName(action)` | `"claim"`, `"assign"`, `"resolve"`, `"reject"`, `"reopen"`, `"note"`. |

`ComplaintActor(Role, AgentId, Name)`: admin için `AgentId` null; `Name` olay kaydına yazılan kullanıcı adıdır.

## Testler

`tests/CustomerSupportBot.Domain.Tests/ComplaintTransitionsTests.cs` — tablonun her satırı, yasak geçişler, rol/atama.
