# Services/Complaints

Şikayet yaşam döngüsü — tasarım: [2026-10-12-complaint-lifecycle-design.md](../../../superpowers/specs/2026-10-12-complaint-lifecycle-design.md).

## ComplaintManagementService (`IComplaintManagementPort`)

Panel/API'nin tek girişi: liste, sayaç, ayrıntı ve durum komutları.

1. Şikayeti okur; yoksa `NotFound`.
2. Kuralı [`ComplaintTransitions`](../../../CustomerSupportBot.Domain/Services/ComplaintTransitions.md) ile denetler
   (`InvalidTransition` / `Forbidden` / `Invalid` + Türkçe mesaj).
3. Atamada temsilcinin var ve aktif olduğunu doğrular (`IHumanAgentRegistry`).
4. İstemci `ExpectedVersion` gönderdiyse ve okunan sürümden farklıysa yazmadan `Conflict` döner.
5. `Note` yalnızca olay satırı ekler; diğer eylemler `IComplaintRepository.TryTransitionAsync` ile **koşullu** yazılır
   (`status` + `version`). Kaybeden istek `Conflict` alır — iki temsilci aynı şikayeti aynı anda üstlenemez.
6. Gerçek geçişten sonra müşteriye e-posta: `Claim`, `Reopen` ve Beklemede'den `Assign` → "inceleniyor";
   `Resolve`/`Reject` → "çözüldü" / "sonuçlandı" (açıklamayla). Yeniden atama ve not e-posta göndermez.

**Ayrıntı:** sipariş özeti (`IOrderRepository`), olay geçmişi ve fotoğraf kimlikleri — fotoğraflar kopyalanmaz,
şikayeti doğuran onay kaydının `attachmentIds` parametresinden okunur (`approval_id`).

**İç not** yalnızca olay satırına yazılır; müşteriye, bota (`complaint_status_tool`) ve KVKK dışa aktarımına gitmez.

## Testler

`tests/CustomerSupportBot.Application.Tests/ComplaintManagementServiceTests.cs`.
