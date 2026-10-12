# Şikayet Yaşam Döngüsü Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Şikayetler `Beklemede → İncelemede → Çözüldü | Reddedildi` (yeniden açılabilir) akışıyla yönetilsin; müşteri e-postayla ve bottan haberdar olsun; geciken şikayetler SLA'da görünsün.

**Architecture:** Mevcut `catalog.complaints` tablosu genişletilir, `catalog.complaint_events` eklenir. Geçiş kuralları Domain'de saf bir sınıftadır (`ComplaintTransitions`); Application'daki `ComplaintManagementService` kuralı uygular, depo `status + version` koşullu UPDATE ile yarışı çözer ve olay satırını aynı transaction'da yazar. Panel ayrı bir `ComplaintsPanel` bileşenidir; hangi düğmelerin görüneceğini sunucu (`actions`) söyler.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, Minimal API, Blazor WASM, xUnit v3 (MTP), NSubstitute, FluentAssertions, Testcontainers.

**Spec:** `docs/superpowers/specs/2026-10-12-complaint-lifecycle-design.md`

## Global Constraints

- Durumlar: `Beklemede`, `İncelemede`, `Çözüldü`, `Reddedildi` (yeni sabit `WellKnown.ComplaintStatuses.Rejected = "Reddedildi"`).
- `resolution` ve `note` en fazla 2000 karakter; panelde ve e-postada HTML escape.
- E-posta iki anda: `İncelemede`'ye geçiş (claim / Beklemede'den assign / reopen) ve kapanış (resolve / reject). Defter anahtarı `complaint:{code}:{kind}:{version}`. Ayarlar `Email:Notifications:ComplaintInReview`, `ComplaintClosed` (varsayılan `true`).
- E-posta hatası geçişi geri almaz.
- İç notlar, olay geçmişindeki aktör adları ve temsilci kimliği **bota ve KVKK dışa aktarımına gitmez**.
- `Sla:Complaints` varsayılanları: `PickupWarnAfterSeconds` 14400, `PickupBreachAfterSeconds` 86400, `ResolveWarnAfterSeconds` 172800, `ResolveBreachAfterSeconds` 259200. Otomatik işlem yok.
- Hata eşlemesi: 404 bulunamadı, 409 geçersiz geçiş / yarış (`currentStatus` + `version` yanıtta), 403 başkasının şikayeti, 400 eksik/uzun girdi ya da hesaba bağlı temsilci yok.
- Testler async çağrılara `TestContext.Current.CancellationToken` geçirir.
- Commit mesajları `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` ile biter. Push yok.
- `appsettings.json` katı JSON (yorum yok).

## Review Focus

1. **Eşzamanlı iki üstlenme:** iki temsilci aynı anda "Üstlen"e basar → biri 200, diğeri 409; olay tablosunda tek `claimed` satırı. (Task 2 testi `ConcurrentClaims_OnlyOneWins`.)
2. **Başkasına atanmış şikayeti kapatma:** temsilci B, A'ya atanmış şikayeti çözmeye çalışır → 403, durum değişmez. (Task 1 `Resolve_ByOtherAgent_Forbidden`, Task 5 `Agent_CannotResolveSomeoneElses`.)
3. **Yeniden açılıp tekrar kapanan şikayet:** ikinci kapanış e-postası da gider (sürüm farklı). (Task 3 `ReopenThenCloseAgain_SendsSecondClosedEmail`.)
4. **Migration öncesi kayıtlar:** `created_at` migration anını alır, olay geçmişi boş; liste/ayrıntı/SLA patlamaz. (Task 2 `LegacyRow_WithoutEvents_ListsAndDetails`.)
5. **Bota iç not sızması:** kapanmış şikayette bot açıklamayı söyler, iç notu söylemez. (Task 4 `StatusTool_ShowsResolution_NotInternalNote`.)

---

## Dosya haritası

| Dosya | Sorumluluk |
|---|---|
| `src/CustomerSupportBot.Domain/Model/ComplaintInfo.cs` (değişir) | Yeni alanlar |
| `src/CustomerSupportBot.Domain/Model/ComplaintEvent.cs` (yeni) | Olay kaydı + tür sabitleri |
| `src/CustomerSupportBot.Domain/Model/WellKnown.cs` (değişir) | `Rejected`, `IsClosed` |
| `src/CustomerSupportBot.Domain/Services/ComplaintTransitions.cs` (yeni) | Saf geçiş kuralları + kullanılabilir eylemler |
| `src/CustomerSupportBot.Application/Ports/Outbound/Persistence/IComplaintRepository.cs` (değişir) | Yeni depo yöntemleri ve kayıtlar |
| `src/CustomerSupportBot.Adapters.Persistence/EfCore/Entities/Catalog/ComplaintEntity.cs`, `ComplaintEventEntity.cs` (yeni) | EF varlıkları |
| `src/CustomerSupportBot.Adapters.Persistence/EfCore/Configurations/Catalog/ComplaintConfiguration.cs`, `ComplaintEventConfiguration.cs` (yeni) | Eşleme |
| `src/CustomerSupportBot.Adapters.Persistence/Migrations/*_AddComplaintLifecycle.cs` (üretilir) | Şema |
| `src/CustomerSupportBot.Adapters.Persistence/Postgres/ComplaintRepository.cs` (değişir) | Koşullu geçiş, liste, sayaç, olaylar |
| `src/CustomerSupportBot.Application/Ports/Inbound/IComplaintManagementPort.cs` (yeni) | Panel/API için port |
| `src/CustomerSupportBot.Application/Services/Complaints/ComplaintManagementService.cs` (yeni) | İş akışı |
| `src/CustomerSupportBot.Application/Services/Notifications/ComplaintStatusEmailService.cs`, `ComplaintStatusEmailComposer.cs` (yeni) | E-posta |
| `src/CustomerSupportBot.Application/Ports/Outbound/EmailOptions.cs` (değişir) | İki bayrak |
| `src/CustomerSupportBot.Application/Services/Tools/ComplaintToolsService.cs`, `CustomerSupportToolsService.cs`, `Ports/Outbound/IComplaintToolsService.cs`, `Services/Approval/ApprovalExecutionRouter.cs` (değişir) | Onay kimliği + bot çıktısı |
| `src/CustomerSupportBot.Api/Endpoints/ComplaintEndpoints.cs` (yeni), `Program.cs` (değişir) | Uçlar |
| `src/CustomerSupportBot.Application/Services/Sla/*` , `Ports/Inbound/ISlaPort.cs` (değişir) | SLA kolu |
| `src/CustomerSupportBot.Application/Services/Privacy/DataPrivacyService.cs`, `Ports/Inbound/IDataPrivacyPort.cs` (değişir) | Dışa aktarma |
| `src/CustomerSupportBot.Web/Components/ComplaintsPanel.razor` (yeni), `Helpers/ComplaintView.cs` (yeni), `Models/AdminModels.cs`, `Services/AdminApiService.cs`, `Services/StaffBadgeState.cs`, `Helpers/AdminTabs.cs`, `Layout/StaffSidebar.razor`, `Pages/Admin.razor(.cs)`, `Pages/Sla.razor`, `wwwroot/css/admin.css` (değişir) | Panel |

---

### Task 1: Domain — model, olay ve geçiş kuralları

**Files:**
- Modify: `src/CustomerSupportBot.Domain/Model/ComplaintInfo.cs`
- Modify: `src/CustomerSupportBot.Domain/Model/WellKnown.cs:368-373`
- Create: `src/CustomerSupportBot.Domain/Model/ComplaintEvent.cs`
- Create: `src/CustomerSupportBot.Domain/Services/ComplaintTransitions.cs`
- Test: `tests/CustomerSupportBot.Domain.Tests/ComplaintTransitionsTests.cs`

**Interfaces:**
- Produces:
  - `ComplaintInfo` + `DateTime CreatedAt`, `DateTime UpdatedAt`, `string? AssignedAgentId`, `string? Resolution`, `string? ApprovalId`, `int Version`
  - `WellKnown.ComplaintStatuses.Rejected`, `WellKnown.ComplaintStatuses.IsClosed(string?)`, `WellKnown.ComplaintStatuses.IsOpen(string?)`
  - `record ComplaintEvent(DateTime At, string Actor, string Kind, string? FromStatus, string? ToStatus, string? AssignedAgentId, string? Note)`; `static class ComplaintEventKinds { Created, Claimed, Assigned, Resolved, Rejected, Reopened, Note }`
  - `enum ComplaintAction { Claim, Assign, Resolve, Reject, Reopen, Note }`, `enum ComplaintActorRole { Admin, Agent }`, `record ComplaintActor(ComplaintActorRole Role, string? AgentId, string Name)`
  - `static class ComplaintTransitionErrors { InvalidTransition, Forbidden, TextRequired, TooLong, AssigneeRequired }`
  - `record ComplaintTransitionCheck(bool Allowed, string? TargetStatus, string? Error)`
  - `ComplaintTransitions.Check(ComplaintAction, ComplaintInfo, ComplaintActor, string? assigneeAgentId, string? text) → ComplaintTransitionCheck`
  - `ComplaintTransitions.AvailableActions(ComplaintInfo, ComplaintActor) → IReadOnlyList<ComplaintAction>`
  - `ComplaintTransitions.ActionName(ComplaintAction) → string` (`"claim"`, `"assign"`, `"resolve"`, `"reject"`, `"reopen"`, `"note"`), `ComplaintTransitions.MaxTextLength = 2000`

Kural sırası: önce durum (geçersizse `invalid_transition`), sonra yetki (`forbidden`), sonra girdi. `Note` her durumda geçerlidir. Yeniden açma yetkisi: admin ya da **atanan** temsilci (kapatan ya admin ya atanan olduğundan spec'teki "kapatan temsilci" ile aynı kümeyi verir; Task 9'da spec metni buna göre güncellenir).

- [ ] **Step 1: Write the failing test**

`tests/CustomerSupportBot.Domain.Tests/ComplaintTransitionsTests.cs`:

```csharp
// Şikayet geçiş kuralları: durum → yetki → girdi sırasıyla.

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;

namespace CustomerSupportBot.Domain.Tests;

public class ComplaintTransitionsTests
{
    private static readonly ComplaintActor Admin = new(ComplaintActorRole.Admin, null, "admin");
    private static readonly ComplaintActor AgentA = new(ComplaintActorRole.Agent, "a1", "Ayşe");
    private static readonly ComplaintActor AgentB = new(ComplaintActorRole.Agent, "b2", "Burak");

    private static ComplaintInfo C(string status, string? assigned = null) => new()
    {
        OrderId = "1030", CustomerId = "1001", Complaint = "Ürün kırık geldi", Status = status, AssignedAgentId = assigned
    };

    private const string Pending = WellKnown.ComplaintStatuses.Pending;
    private const string InProgress = WellKnown.ComplaintStatuses.InProgress;
    private const string Resolved = WellKnown.ComplaintStatuses.Resolved;
    private const string Rejected = WellKnown.ComplaintStatuses.Rejected;

    [Fact]
    public void Claim_ByAgent_FromPending_GoesToInProgress()
    {
        var r = ComplaintTransitions.Check(ComplaintAction.Claim, C(Pending), AgentA, null, null);
        r.Allowed.Should().BeTrue();
        r.TargetStatus.Should().Be(InProgress);
    }

    [Fact]
    public void Claim_ByAdmin_Forbidden()
        => ComplaintTransitions.Check(ComplaintAction.Claim, C(Pending), Admin, null, null)
            .Error.Should().Be(ComplaintTransitionErrors.Forbidden);

    [Fact]
    public void Claim_WhenAlreadyInProgress_InvalidTransition()
        => ComplaintTransitions.Check(ComplaintAction.Claim, C(InProgress, "b2"), AgentA, null, null)
            .Error.Should().Be(ComplaintTransitionErrors.InvalidTransition);

    [Theory]
    [InlineData(WellKnown.ComplaintStatuses.Pending)]
    [InlineData(WellKnown.ComplaintStatuses.InProgress)]
    public void Assign_ByAdmin_FromOpen_GoesToInProgress(string status)
    {
        var r = ComplaintTransitions.Check(ComplaintAction.Assign, C(status, "b2"), Admin, "a1", null);
        r.Allowed.Should().BeTrue();
        r.TargetStatus.Should().Be(InProgress);
    }

    [Fact]
    public void Assign_ByAgent_Forbidden()
        => ComplaintTransitions.Check(ComplaintAction.Assign, C(Pending), AgentA, "a1", null)
            .Error.Should().Be(ComplaintTransitionErrors.Forbidden);

    [Fact]
    public void Assign_WithoutAssignee_Required()
        => ComplaintTransitions.Check(ComplaintAction.Assign, C(Pending), Admin, " ", null)
            .Error.Should().Be(ComplaintTransitionErrors.AssigneeRequired);

    [Fact]
    public void Assign_Closed_InvalidTransition()
        => ComplaintTransitions.Check(ComplaintAction.Assign, C(Resolved, "a1"), Admin, "b2", null)
            .Error.Should().Be(ComplaintTransitionErrors.InvalidTransition);

    [Theory]
    [InlineData(ComplaintAction.Resolve, WellKnown.ComplaintStatuses.Resolved)]
    [InlineData(ComplaintAction.Reject, WellKnown.ComplaintStatuses.Rejected)]
    public void Close_ByAssignedAgent_WithText(ComplaintAction action, string target)
    {
        var r = ComplaintTransitions.Check(action, C(InProgress, "a1"), AgentA, null, "Yeni ürün gönderildi.");
        r.Allowed.Should().BeTrue();
        r.TargetStatus.Should().Be(target);
    }

    [Fact]
    public void Resolve_ByAdmin_Allowed()
        => ComplaintTransitions.Check(ComplaintAction.Resolve, C(InProgress, "a1"), Admin, null, "Tamam").Allowed.Should().BeTrue();

    [Fact]
    public void Resolve_ByOtherAgent_Forbidden()
        => ComplaintTransitions.Check(ComplaintAction.Resolve, C(InProgress, "a1"), AgentB, null, "Tamam")
            .Error.Should().Be(ComplaintTransitionErrors.Forbidden);

    [Fact]
    public void Resolve_FromPending_InvalidTransition()
        => ComplaintTransitions.Check(ComplaintAction.Resolve, C(Pending), Admin, null, "Tamam")
            .Error.Should().Be(ComplaintTransitionErrors.InvalidTransition);

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Resolve_WithoutText_TextRequired(string? text)
        => ComplaintTransitions.Check(ComplaintAction.Resolve, C(InProgress, "a1"), AgentA, null, text)
            .Error.Should().Be(ComplaintTransitionErrors.TextRequired);

    [Fact]
    public void Resolve_TooLongText_TooLong()
        => ComplaintTransitions.Check(ComplaintAction.Resolve, C(InProgress, "a1"), AgentA, null, new string('x', 2001))
            .Error.Should().Be(ComplaintTransitionErrors.TooLong);

    [Theory]
    [InlineData(WellKnown.ComplaintStatuses.Resolved)]
    [InlineData(WellKnown.ComplaintStatuses.Rejected)]
    public void Reopen_Closed_ByAssignedAgent_GoesToInProgress(string status)
    {
        var r = ComplaintTransitions.Check(ComplaintAction.Reopen, C(status, "a1"), AgentA, null, "Müşteri tekrar yazdı");
        r.Allowed.Should().BeTrue();
        r.TargetStatus.Should().Be(InProgress);
    }

    [Fact]
    public void Reopen_ByOtherAgent_Forbidden()
        => ComplaintTransitions.Check(ComplaintAction.Reopen, C(Resolved, "a1"), AgentB, null, "neden")
            .Error.Should().Be(ComplaintTransitionErrors.Forbidden);

    [Fact]
    public void Reopen_Open_InvalidTransition()
        => ComplaintTransitions.Check(ComplaintAction.Reopen, C(InProgress, "a1"), Admin, null, "neden")
            .Error.Should().Be(ComplaintTransitionErrors.InvalidTransition);

    [Fact]
    public void Reopen_WithoutReason_TextRequired()
        => ComplaintTransitions.Check(ComplaintAction.Reopen, C(Rejected, "a1"), Admin, null, "")
            .Error.Should().Be(ComplaintTransitionErrors.TextRequired);

    [Fact]
    public void Note_KeepsStatus_ForAssignedAgent()
    {
        var r = ComplaintTransitions.Check(ComplaintAction.Note, C(Resolved, "a1"), AgentA, null, "kargoya sorduk");
        r.Allowed.Should().BeTrue();
        r.TargetStatus.Should().Be(Resolved);
    }

    [Fact]
    public void Note_ByUnassignedAgent_Forbidden()
        => ComplaintTransitions.Check(ComplaintAction.Note, C(Pending), AgentA, null, "not")
            .Error.Should().Be(ComplaintTransitionErrors.Forbidden);

    [Fact]
    public void AvailableActions_ReflectStatusAndRole()
    {
        ComplaintTransitions.AvailableActions(C(Pending), AgentA).Should().Equal(ComplaintAction.Claim);
        ComplaintTransitions.AvailableActions(C(Pending), Admin).Should().Equal(ComplaintAction.Assign, ComplaintAction.Note);
        ComplaintTransitions.AvailableActions(C(InProgress, "a1"), AgentA)
            .Should().Equal(ComplaintAction.Resolve, ComplaintAction.Reject, ComplaintAction.Note);
        ComplaintTransitions.AvailableActions(C(InProgress, "a1"), AgentB).Should().BeEmpty();
        ComplaintTransitions.AvailableActions(C(Resolved, "a1"), Admin).Should().Equal(ComplaintAction.Reopen, ComplaintAction.Note);
    }

    [Fact]
    public void ClosedAndOpenHelpers()
    {
        WellKnown.ComplaintStatuses.IsClosed(Resolved).Should().BeTrue();
        WellKnown.ComplaintStatuses.IsClosed(Rejected).Should().BeTrue();
        WellKnown.ComplaintStatuses.IsOpen(Pending).Should().BeTrue();
        WellKnown.ComplaintStatuses.IsOpen(InProgress).Should().BeTrue();
        WellKnown.ComplaintStatuses.IsClosed(null).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/CustomerSupportBot.Domain.Tests -- --filter-class CustomerSupportBot.Domain.Tests.ComplaintTransitionsTests`
Expected: derleme hatası — `ComplaintActor`, `ComplaintTransitions`, `ComplaintStatuses.Rejected` yok.

- [ ] **Step 3: Write minimal implementation**

`ComplaintInfo.cs` — sınıfın sonuna ekle:

```csharp
    /// <summary>Oluşturulma (UTC). SLA buradan sayılır. Migration öncesi kayıtlarda migration anı.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Son durum/atama değişikliği (UTC).</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Üstlenen temsilci (<c>HumanAgent.Id</c>); atanmamışsa null.</summary>
    public string? AssignedAgentId { get; set; }

    /// <summary>Çözüldü/Reddedildi için müşteriye görünen açıklama. Yeniden açılınca korunur.</summary>
    public string? Resolution { get; set; }

    /// <summary>Şikayeti doğuran onay kaydı; fotoğraflar bu onaya bağlıdır.</summary>
    public string? ApprovalId { get; set; }

    /// <summary>Her geçişte +1. Koşullu güncelleme ve e-posta defteri anahtarı için.</summary>
    public int Version { get; set; }
```

`WellKnown.cs` — `ComplaintStatuses` bloğunu şununla değiştir:

```csharp
    public static class ComplaintStatuses
    {
        public const string Pending = "Beklemede";
        public const string InProgress = "İncelemede";
        public const string Resolved = "Çözüldü";
        public const string Rejected = "Reddedildi";

        public static bool IsClosed(string? status) => status is Resolved or Rejected;

        public static bool IsOpen(string? status) => status is Pending or InProgress;
    }
```

`src/CustomerSupportBot.Domain/Model/ComplaintEvent.cs`:

```csharp
namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Şikayet zaman çizelgesinin bir satırı. <see cref="Note"/> İÇ nottur: müşteriye, bota ve KVKK
/// dışa aktarımına gitmez. <see cref="Actor"/> personel kullanıcı adı ya da <c>system</c>.
/// </summary>
public sealed record ComplaintEvent(
    DateTime At,
    string Actor,
    string Kind,
    string? FromStatus,
    string? ToStatus,
    string? AssignedAgentId,
    string? Note);

public static class ComplaintEventKinds
{
    public const string Created = "created";
    public const string Claimed = "claimed";
    public const string Assigned = "assigned";
    public const string Resolved = "resolved";
    public const string Rejected = "rejected";
    public const string Reopened = "reopened";
    public const string Note = "note";
}
```

`src/CustomerSupportBot.Domain/Services/ComplaintTransitions.cs`:

```csharp
// Domain/Services/ComplaintTransitions.cs
// Şikayet durum geçişleri — saf kurallar. Sıra: durum → yetki → girdi.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Domain.Services;

public enum ComplaintAction { Claim, Assign, Resolve, Reject, Reopen, Note }

public enum ComplaintActorRole { Admin, Agent }

/// <param name="AgentId">Temsilcinin <c>HumanAgent.Id</c>'si; admin için null.</param>
/// <param name="Name">Olay kaydına yazılan kullanıcı adı.</param>
public sealed record ComplaintActor(ComplaintActorRole Role, string? AgentId, string Name)
{
    public bool IsAdmin => Role == ComplaintActorRole.Admin;
}

public static class ComplaintTransitionErrors
{
    public const string InvalidTransition = "invalid_transition";
    public const string Forbidden = "forbidden";
    public const string TextRequired = "text_required";
    public const string TooLong = "too_long";
    public const string AssigneeRequired = "assignee_required";
}

public sealed record ComplaintTransitionCheck(bool Allowed, string? TargetStatus, string? Error)
{
    public static ComplaintTransitionCheck Ok(string target) => new(true, target, null);
    public static ComplaintTransitionCheck Fail(string error) => new(false, null, error);
}

public static class ComplaintTransitions
{
    public const int MaxTextLength = 2000;

    private static readonly ComplaintAction[] AllActions =
        [ComplaintAction.Claim, ComplaintAction.Assign, ComplaintAction.Resolve, ComplaintAction.Reject, ComplaintAction.Reopen, ComplaintAction.Note];

    public static string ActionName(ComplaintAction action) => action switch
    {
        ComplaintAction.Claim => "claim",
        ComplaintAction.Assign => "assign",
        ComplaintAction.Resolve => "resolve",
        ComplaintAction.Reject => "reject",
        ComplaintAction.Reopen => "reopen",
        _ => "note"
    };

    /// <param name="assigneeAgentId">Yalnızca <see cref="ComplaintAction.Assign"/> için.</param>
    /// <param name="text">Resolve/Reject: müşteriye görünen açıklama; Reopen/Note: not.</param>
    public static ComplaintTransitionCheck Check(
        ComplaintAction action, ComplaintInfo complaint, ComplaintActor actor, string? assigneeAgentId, string? text)
    {
        var target = TargetOf(action, complaint.Status);
        if (target is null) return ComplaintTransitionCheck.Fail(ComplaintTransitionErrors.InvalidTransition);
        if (!Permitted(action, complaint, actor)) return ComplaintTransitionCheck.Fail(ComplaintTransitionErrors.Forbidden);

        switch (action)
        {
            case ComplaintAction.Assign when string.IsNullOrWhiteSpace(assigneeAgentId):
                return ComplaintTransitionCheck.Fail(ComplaintTransitionErrors.AssigneeRequired);
            case ComplaintAction.Resolve or ComplaintAction.Reject or ComplaintAction.Reopen or ComplaintAction.Note:
                if (string.IsNullOrWhiteSpace(text)) return ComplaintTransitionCheck.Fail(ComplaintTransitionErrors.TextRequired);
                if (text.Trim().Length > MaxTextLength) return ComplaintTransitionCheck.Fail(ComplaintTransitionErrors.TooLong);
                break;
        }
        return ComplaintTransitionCheck.Ok(target);
    }

    /// <summary>Durum ve yetkiye göre sunulabilecek eylemler (girdi denetimi hariç) — panel düğmeleri.</summary>
    public static IReadOnlyList<ComplaintAction> AvailableActions(ComplaintInfo complaint, ComplaintActor actor) =>
        AllActions.Where(a => TargetOf(a, complaint.Status) is not null && Permitted(a, complaint, actor)).ToList();

    private static string? TargetOf(ComplaintAction action, string status) => action switch
    {
        ComplaintAction.Claim when status == WellKnown.ComplaintStatuses.Pending => WellKnown.ComplaintStatuses.InProgress,
        ComplaintAction.Assign when WellKnown.ComplaintStatuses.IsOpen(status) => WellKnown.ComplaintStatuses.InProgress,
        ComplaintAction.Resolve when status == WellKnown.ComplaintStatuses.InProgress => WellKnown.ComplaintStatuses.Resolved,
        ComplaintAction.Reject when status == WellKnown.ComplaintStatuses.InProgress => WellKnown.ComplaintStatuses.Rejected,
        ComplaintAction.Reopen when WellKnown.ComplaintStatuses.IsClosed(status) => WellKnown.ComplaintStatuses.InProgress,
        ComplaintAction.Note => status,
        _ => null
    };

    private static bool Permitted(ComplaintAction action, ComplaintInfo complaint, ComplaintActor actor)
    {
        var isAssignee = !actor.IsAdmin && actor.AgentId is not null
                         && string.Equals(actor.AgentId, complaint.AssignedAgentId, StringComparison.Ordinal);
        return action switch
        {
            ComplaintAction.Claim => !actor.IsAdmin && actor.AgentId is not null,
            ComplaintAction.Assign => actor.IsAdmin,
            _ => actor.IsAdmin || isAssignee
        };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --project tests/CustomerSupportBot.Domain.Tests -- --filter-class CustomerSupportBot.Domain.Tests.ComplaintTransitionsTests`
Expected: PASS (tümü).

- [ ] **Step 5: Run the domain suite and build the solution**

Run: `dotnet build CustomerSupport.slnx -c Debug 2>&1 | tail -3 && dotnet test --project tests/CustomerSupportBot.Domain.Tests`
Expected: derleme 0 hata; Domain testleri yeşil.

- [ ] **Step 6: Commit**

```bash
git add src/CustomerSupportBot.Domain tests/CustomerSupportBot.Domain.Tests/ComplaintTransitionsTests.cs
git commit -m "feat(complaints): lifecycle model and transition rules

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Kalıcılık — şema, migration, koşullu geçiş

**Files:**
- Modify: `src/CustomerSupportBot.Application/Ports/Outbound/Persistence/IComplaintRepository.cs`
- Modify: `src/CustomerSupportBot.Adapters.Persistence/EfCore/Entities/Catalog/ComplaintEntity.cs`
- Create: `src/CustomerSupportBot.Adapters.Persistence/EfCore/Entities/Catalog/ComplaintEventEntity.cs`
- Modify: `src/CustomerSupportBot.Adapters.Persistence/EfCore/Configurations/Catalog/ComplaintConfiguration.cs`
- Create: `src/CustomerSupportBot.Adapters.Persistence/EfCore/Configurations/Catalog/ComplaintEventConfiguration.cs`
- Modify: `src/CustomerSupportBot.Adapters.Persistence/EfCore/CustomerSupportDbContext.cs:72`
- Create (üretilir): `src/CustomerSupportBot.Adapters.Persistence/Migrations/<zaman>_AddComplaintLifecycle.cs`
- Modify: `src/CustomerSupportBot.Adapters.Persistence/Postgres/ComplaintRepository.cs`
- Test: `tests/CustomerSupportBot.Adapters.Persistence.Tests/PostgresComplaintLifecycleTests.cs`

**Interfaces:**
- Consumes: Task 1'deki `ComplaintInfo` alanları, `ComplaintEvent`, `ComplaintEventKinds`, `WellKnown.ComplaintStatuses.IsOpen/IsClosed`.
- Produces (port, `CustomerSupportBot.Application.Ports.Outbound.Persistence`):

```csharp
public sealed record ComplaintQuery(
    string? Status = null,           // null = tümü; ComplaintQuery.OpenStatus = Beklemede + İncelemede
    string? AssignedAgentId = null,  // yalnızca bu temsilciye atananlar
    bool UnassignedOnly = false,
    string? VisibleToAgentId = null, // temsilci kapsamı: atanmamış + bu temsilciye atanan
    int Page = 1,
    int PageSize = 20)
{
    public const string OpenStatus = "open";
}
public sealed record ComplaintListItem(string ComplaintId, ComplaintInfo Complaint);
public sealed record ComplaintPage(IReadOnlyList<ComplaintListItem> Items, int Total);
public sealed record ComplaintCounts(int Pending, int InProgress, int AssignedToAgent);
/// <param name="Resolution">null = mevcut açıklamayı koru.</param>
public sealed record ComplaintTransition(
    string ComplaintId, string ExpectedStatus, int ExpectedVersion, string NewStatus,
    string? AssignedAgentId, string? Resolution, ComplaintEvent Event);
public enum ComplaintTransitionChange { Updated, Conflict, NotFound }
public sealed record ComplaintTransitionOutcome(ComplaintTransitionChange Change, ComplaintInfo? Complaint);

// IComplaintRepository'ye eklenenler:
string Create(ComplaintInfo complaint);   // değişmez imza; artık CreatedAt/ApprovalId + "created" olayı yazar
Task<ComplaintPage> ListAsync(ComplaintQuery query, CancellationToken ct = default);
Task<ComplaintCounts> CountOpenAsync(string? agentId, CancellationToken ct = default);
Task<IReadOnlyList<ComplaintListItem>> GetOpenAsync(int max = 500, CancellationToken ct = default);
Task<IReadOnlyList<ComplaintEvent>> GetEventsAsync(string complaintId, CancellationToken ct = default);
Task<ComplaintTransitionOutcome> TryTransitionAsync(ComplaintTransition transition, CancellationToken ct = default);
Task<bool> AddEventAsync(string complaintId, ComplaintEvent evt, CancellationToken ct = default);
```

Sıralama kuralı (spec "açıklar en eskiden yeniye, kapalılar yeniden eskiye"): yalnız kapalı durum filtresinde `UpdatedAt desc`; diğer tüm filtrelerde önce açıklar (`CreatedAt asc`), sonra kapalılar (`UpdatedAt desc`).

- [ ] **Step 1: Write the failing test**

`tests/CustomerSupportBot.Adapters.Persistence.Tests/PostgresComplaintLifecycleTests.cs`:

```csharp
// Şikayet yaşam döngüsü — koşullu geçiş, olay satırı, liste/sayaç (gerçek Postgres).

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresComplaintLifecycleTests(PostgresCatalogFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime T0 = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);
    private IComplaintRepository Repo => fixture.ComplaintRepo;

    private string NewComplaint(string? approvalId = null, DateTime? createdAt = null) => Repo.Create(new ComplaintInfo
    {
        OrderId = "1030", CustomerId = "1027", Complaint = "Ürün hasarlı geldi, kutu ezik",
        Status = WellKnown.ComplaintStatuses.Pending, ApprovalId = approvalId, CreatedAt = createdAt ?? T0
    });

    private static ComplaintTransition Claim(string id, string agent, int version = 0) => new(
        id, WellKnown.ComplaintStatuses.Pending, version, WellKnown.ComplaintStatuses.InProgress, agent, null,
        new ComplaintEvent(T0.AddMinutes(5), agent, ComplaintEventKinds.Claimed,
            WellKnown.ComplaintStatuses.Pending, WellKnown.ComplaintStatuses.InProgress, agent, null));

    [Fact]
    public async Task Create_StoresTimestampsApproval_AndCreatedEvent()
    {
        var id = NewComplaint("apr-1");

        var c = Repo.Get(id)!;
        c.CreatedAt.Should().Be(T0);
        c.UpdatedAt.Should().Be(T0);
        c.ApprovalId.Should().Be("apr-1");
        c.Version.Should().Be(0);
        var events = await Repo.GetEventsAsync(id, Ct);
        events.Should().ContainSingle().Which.Kind.Should().Be(ComplaintEventKinds.Created);
    }

    [Fact]
    public async Task Claim_Then_Resolve_UpdatesRow_AndWritesEvents()
    {
        var id = NewComplaint();

        var claimed = await Repo.TryTransitionAsync(Claim(id, "a1"), Ct);
        claimed.Change.Should().Be(ComplaintTransitionChange.Updated);
        claimed.Complaint!.Status.Should().Be(WellKnown.ComplaintStatuses.InProgress);
        claimed.Complaint.AssignedAgentId.Should().Be("a1");
        claimed.Complaint.Version.Should().Be(1);

        var resolved = await Repo.TryTransitionAsync(new ComplaintTransition(
            id, WellKnown.ComplaintStatuses.InProgress, 1, WellKnown.ComplaintStatuses.Resolved, "a1", "Yeni ürün gönderildi.",
            new ComplaintEvent(T0.AddHours(1), "ayse", ComplaintEventKinds.Resolved,
                WellKnown.ComplaintStatuses.InProgress, WellKnown.ComplaintStatuses.Resolved, "a1", "iç not")), Ct);

        resolved.Change.Should().Be(ComplaintTransitionChange.Updated);
        resolved.Complaint!.Resolution.Should().Be("Yeni ürün gönderildi.");
        resolved.Complaint.UpdatedAt.Should().Be(T0.AddHours(1));
        (await Repo.GetEventsAsync(id, Ct)).Select(e => e.Kind)
            .Should().Equal(ComplaintEventKinds.Created, ComplaintEventKinds.Claimed, ComplaintEventKinds.Resolved);
    }

    [Fact]
    public async Task Reopen_KeepsResolution_WhenNull()
    {
        var id = NewComplaint();
        await Repo.TryTransitionAsync(Claim(id, "a1"), Ct);
        await Repo.TryTransitionAsync(new ComplaintTransition(id, WellKnown.ComplaintStatuses.InProgress, 1,
            WellKnown.ComplaintStatuses.Rejected, "a1", "Hasar kargodan.", new ComplaintEvent(T0.AddHours(1), "ayse",
                ComplaintEventKinds.Rejected, WellKnown.ComplaintStatuses.InProgress, WellKnown.ComplaintStatuses.Rejected, "a1", null)), Ct);

        var reopened = await Repo.TryTransitionAsync(new ComplaintTransition(id, WellKnown.ComplaintStatuses.Rejected, 2,
            WellKnown.ComplaintStatuses.InProgress, "a1", null, new ComplaintEvent(T0.AddHours(2), "admin",
                ComplaintEventKinds.Reopened, WellKnown.ComplaintStatuses.Rejected, WellKnown.ComplaintStatuses.InProgress, "a1", "itiraz")), Ct);

        reopened.Complaint!.Status.Should().Be(WellKnown.ComplaintStatuses.InProgress);
        reopened.Complaint.Resolution.Should().Be("Hasar kargodan.");
    }

    [Fact]
    public async Task StaleVersion_IsConflict_AndWritesNoEvent()
    {
        var id = NewComplaint();
        await Repo.TryTransitionAsync(Claim(id, "a1"), Ct);

        var stale = await Repo.TryTransitionAsync(Claim(id, "b2"), Ct);

        stale.Change.Should().Be(ComplaintTransitionChange.Conflict);
        stale.Complaint!.AssignedAgentId.Should().Be("a1");
        (await Repo.GetEventsAsync(id, Ct)).Should().HaveCount(2);
    }

    [Fact]
    public async Task ConcurrentClaims_OnlyOneWins()
    {
        var id = NewComplaint();

        var results = await Task.WhenAll(
            Repo.TryTransitionAsync(Claim(id, "a1"), Ct),
            Repo.TryTransitionAsync(Claim(id, "b2"), Ct));

        results.Count(r => r.Change == ComplaintTransitionChange.Updated).Should().Be(1);
        results.Count(r => r.Change == ComplaintTransitionChange.Conflict).Should().Be(1);
        (await Repo.GetEventsAsync(id, Ct)).Count(e => e.Kind == ComplaintEventKinds.Claimed).Should().Be(1);
    }

    [Fact]
    public async Task Unknown_IsNotFound()
        => (await Repo.TryTransitionAsync(Claim("99999999", "a1"), Ct)).Change.Should().Be(ComplaintTransitionChange.NotFound);

    [Fact]
    public async Task AddEvent_NoteOnly_DoesNotChangeVersion()
    {
        var id = NewComplaint();

        (await Repo.AddEventAsync(id, new ComplaintEvent(T0.AddMinutes(1), "admin", ComplaintEventKinds.Note, null, null, null, "arandı"), Ct))
            .Should().BeTrue();

        Repo.Get(id)!.Version.Should().Be(0);
        (await Repo.GetEventsAsync(id, Ct)).Last().Note.Should().Be("arandı");
        (await Repo.AddEventAsync("99999999", new ComplaintEvent(T0, "admin", ComplaintEventKinds.Note, null, null, null, "x"), Ct))
            .Should().BeFalse();
    }

    [Fact]
    public async Task List_AgentScope_HidesOthersAssigned_AndCountsMatch()
    {
        var mine = NewComplaint(createdAt: T0.AddYears(5));
        var others = NewComplaint(createdAt: T0.AddYears(5).AddMinutes(1));
        var free = NewComplaint(createdAt: T0.AddYears(5).AddMinutes(2));
        await Repo.TryTransitionAsync(Claim(mine, "agent-me"), Ct);
        await Repo.TryTransitionAsync(Claim(others, "agent-other"), Ct);

        var page = await Repo.ListAsync(new ComplaintQuery(Status: ComplaintQuery.OpenStatus, VisibleToAgentId: "agent-me", PageSize: 500), Ct);
        var ids = page.Items.Select(i => i.ComplaintId).ToList();
        ids.Should().Contain([mine, free]).And.NotContain(others);

        var counts = await Repo.CountOpenAsync("agent-me", Ct);
        counts.AssignedToAgent.Should().Be(1);
        counts.Pending.Should().BeGreaterThanOrEqualTo(1);
        counts.InProgress.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task List_OpenFirstOldestFirst_ClosedOnlyNewestFirst()
    {
        var older = NewComplaint(createdAt: T0.AddYears(-3));
        var newer = NewComplaint(createdAt: T0.AddYears(-3).AddDays(1));

        var open = await Repo.ListAsync(new ComplaintQuery(Status: WellKnown.ComplaintStatuses.Pending, PageSize: 500), Ct);
        var ids = open.Items.Select(i => i.ComplaintId).ToList();
        ids.IndexOf(older).Should().BeLessThan(ids.IndexOf(newer));
        open.Total.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task LegacyRow_WithoutEvents_ListsAndDetails()
    {
        await using (var ctx = fixture.DbFactory.CreateDbContext())
        {
            ctx.Complaints.Add(new ComplaintEntity
            {
                Code = 7_000_001, OrderId = 1030, CustomerId = 1027, Complaint = "eski kayıt", Status = WellKnown.ComplaintStatuses.Pending
            });
            await ctx.SaveChangesAsync(Ct);
        }

        var c = Repo.Get("7000001")!;
        c.CreatedAt.Should().NotBe(default, "veritabanı varsayılanı now()");
        (await Repo.GetEventsAsync("7000001", Ct)).Should().BeEmpty();
        (await Repo.GetOpenAsync(5000, Ct)).Should().Contain(i => i.ComplaintId == "7000001");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/CustomerSupportBot.Adapters.Persistence.Tests -- --filter-class CustomerSupportBot.Adapters.Persistence.Tests.PostgresComplaintLifecycleTests`
Expected: derleme hatası — `ComplaintTransition`, `TryTransitionAsync` vb. yok.

- [ ] **Step 3: Port, varlıklar ve eşleme**

`IComplaintRepository.cs` — yukarıdaki "Produces" kayıtlarını dosyanın başına (namespace altına) ekle ve arayüze yeni üyeleri ekle; mevcut dört üye aynen kalır. Kayıtlar `using CustomerSupportBot.Domain.Model;` kullanır.

`ComplaintEntity.cs`:

```csharp
namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;

public sealed class ComplaintEntity
{
    public long Code { get; set; }
    public long OrderId { get; set; }
    public long CustomerId { get; set; }
    public string Complaint { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? AssignedAgentId { get; set; }
    public string? Resolution { get; set; }
    public string? ApprovalId { get; set; }
    public int Version { get; set; }
}
```

`ComplaintEventEntity.cs`:

```csharp
namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;

public sealed class ComplaintEventEntity
{
    public long Id { get; set; }
    public long ComplaintCode { get; set; }
    public DateTime At { get; set; }
    public string Actor { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? AssignedAgentId { get; set; }
    public string? Note { get; set; }
}
```

`ComplaintConfiguration.cs` — `Status` özelliğinden sonra ekle:

```csharp
        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(e => e.AssignedAgentId)
            .HasColumnName("assigned_agent_id")
            .HasMaxLength(64);

        builder.Property(e => e.Resolution)
            .HasColumnName("resolution")
            .HasMaxLength(2000);

        builder.Property(e => e.ApprovalId)
            .HasColumnName("approval_id")
            .HasMaxLength(64);

        builder.Property(e => e.Version)
            .HasColumnName("version")
            .HasDefaultValue(0)
            .IsRequired();

        builder.HasIndex(e => new { e.Status, e.CreatedAt })
            .HasDatabaseName("ix_complaints_status_created_at");
```

`ComplaintEventConfiguration.cs`:

```csharp
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Catalog;

internal sealed class ComplaintEventConfiguration : IEntityTypeConfiguration<ComplaintEventEntity>
{
    public void Configure(EntityTypeBuilder<ComplaintEventEntity> builder)
    {
        builder.ToTable("complaint_events", Schemas.Catalog);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").UseIdentityByDefaultColumn();
        builder.Property(e => e.ComplaintCode).HasColumnName("complaint_code").IsRequired();
        builder.Property(e => e.At).HasColumnName("at").HasColumnType("timestamptz").IsRequired();
        builder.Property(e => e.Actor).HasColumnName("actor").HasMaxLength(128).IsRequired();
        builder.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(32).IsRequired();
        builder.Property(e => e.FromStatus).HasColumnName("from_status").HasMaxLength(64);
        builder.Property(e => e.ToStatus).HasColumnName("to_status").HasMaxLength(64);
        builder.Property(e => e.AssignedAgentId).HasColumnName("assigned_agent_id").HasMaxLength(64);
        builder.Property(e => e.Note).HasColumnName("note").HasMaxLength(2000);

        builder.HasOne<ComplaintEntity>()
            .WithMany()
            .HasForeignKey(e => e.ComplaintCode)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.ComplaintCode, e.At })
            .HasDatabaseName("ix_complaint_events_code_at");
    }
}
```

`CustomerSupportDbContext.cs` — satır 72'nin altına:

```csharp
    public DbSet<ComplaintEventEntity> ComplaintEvents => Set<ComplaintEventEntity>();
```

(Konfigürasyonlar `ApplyConfigurationsFromAssembly` ile alınıyorsa ek kayıt gerekmez; alınmıyorsa `OnModelCreating`'de `ComplaintConfiguration`'ın uygulandığı satırın altına `modelBuilder.ApplyConfiguration(new ComplaintEventConfiguration());` ekle — dosyada `ComplaintConfiguration` ara.)

- [ ] **Step 4: Migration üret**

Run: `dotnet ef migrations add AddComplaintLifecycle --project src/CustomerSupportBot.Adapters.Persistence --output-dir Migrations`
Expected: `Migrations/<zaman>_AddComplaintLifecycle.cs` — `AddColumn` × 6 (`created_at`/`updated_at` `defaultValueSql: "now()"`, `version` `defaultValue: 0`), `CreateTable complaint_events`, iki indeks. Mevcut satırlar için `updated_at` = `created_at` olsun diye `Up` sonuna şunu ekle (üretilen dosyaya elle — bu dosya `dotnet ef`'in ürettiği tek istisna; Down'a karşılığı gerekmez):

```csharp
            migrationBuilder.Sql("UPDATE catalog.complaints SET updated_at = created_at;");
```

- [ ] **Step 5: Depoyu uygula**

`ComplaintRepository.cs` — `Create` gövdesindeki `ctx.Complaints.Add(...)` ve `SaveChanges` kısmını şununla değiştir:

```csharp
        var at = DateTime.SpecifyKind(complaint.CreatedAt == default ? DateTime.UtcNow : complaint.CreatedAt, DateTimeKind.Utc);
        ctx.Complaints.Add(new ComplaintEntity
        {
            Code = seq,
            OrderId = long.Parse(complaint.OrderId),
            CustomerId = long.Parse(complaint.CustomerId),
            Complaint = complaint.Complaint,
            Status = complaint.Status,
            CreatedAt = at,
            UpdatedAt = at,
            ApprovalId = complaint.ApprovalId
        });
        ctx.ComplaintEvents.Add(new ComplaintEventEntity
        {
            ComplaintCode = seq, At = at, Actor = WellKnown.Defaults.System, Kind = ComplaintEventKinds.Created,
            ToStatus = complaint.Status
        });
        ctx.SaveChanges();
        return seq.ToString();
```

(`WellKnown.Defaults.System` mevcut sabittir — `SlaPortService`'te kullanılıyor.)

`MapToModel`'i genişlet:

```csharp
    private static ComplaintInfo MapToModel(ComplaintEntity e) => new()
    {
        OrderId = e.OrderId.ToString(),
        CustomerId = e.CustomerId.ToString(),
        Complaint = e.Complaint,
        Status = e.Status,
        CreatedAt = DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc),
        UpdatedAt = DateTime.SpecifyKind(e.UpdatedAt, DateTimeKind.Utc),
        AssignedAgentId = e.AssignedAgentId,
        Resolution = e.Resolution,
        ApprovalId = e.ApprovalId,
        Version = e.Version
    };
```

Sınıfa yeni yöntemleri ekle (`using Microsoft.EntityFrameworkCore;` ve `using CustomerSupportBot.Domain.Model;` dosyada var; yoksa ekle):

```csharp
    public async Task<ComplaintPage> ListAsync(ComplaintQuery query, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var q = Filter(ctx.Complaints.AsNoTracking(), query);
        var total = await q.CountAsync(ct);

        // Açıklar önce ve en eskiden yeniye; kapalılar sonra ve en yeniden eskiye. Koşullu anahtarlar
        // CASE WHEN'e çevrilir: her grupta diğer grubun anahtarı NULL olduğundan sıralamayı bozmaz.
        var closedOnly = WellKnown.ComplaintStatuses.IsClosed(query.Status);
        var ordered = closedOnly
            ? q.OrderByDescending(c => c.UpdatedAt).ThenByDescending(c => c.Code)
            : q.OrderBy(c => c.Status == WellKnown.ComplaintStatuses.Resolved || c.Status == WellKnown.ComplaintStatuses.Rejected)
               .ThenBy(c => c.Status == WellKnown.ComplaintStatuses.Resolved || c.Status == WellKnown.ComplaintStatuses.Rejected
                   ? (DateTime?)null : c.CreatedAt)
               .ThenByDescending(c => c.Status == WellKnown.ComplaintStatuses.Resolved || c.Status == WellKnown.ComplaintStatuses.Rejected
                   ? c.UpdatedAt : (DateTime?)null)
               .ThenBy(c => c.Code);

        var size = Math.Clamp(query.PageSize, 1, 500);
        var page = Math.Max(query.Page, 1);
        var rows = await ordered.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new ComplaintPage(rows.Select(e => new ComplaintListItem(e.Code.ToString(), MapToModel(e))).ToList(), total);
    }

    private static IQueryable<ComplaintEntity> Filter(IQueryable<ComplaintEntity> q, ComplaintQuery query)
    {
        if (query.Status == ComplaintQuery.OpenStatus)
            q = q.Where(c => c.Status == WellKnown.ComplaintStatuses.Pending || c.Status == WellKnown.ComplaintStatuses.InProgress);
        else if (!string.IsNullOrWhiteSpace(query.Status))
            q = q.Where(c => c.Status == query.Status);
        if (query.UnassignedOnly) q = q.Where(c => c.AssignedAgentId == null);
        if (query.AssignedAgentId is { } assigned) q = q.Where(c => c.AssignedAgentId == assigned);
        if (query.VisibleToAgentId is { } me) q = q.Where(c => c.AssignedAgentId == null || c.AssignedAgentId == me);
        return q;
    }

    public async Task<ComplaintCounts> CountOpenAsync(string? agentId, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var open = ctx.Complaints.AsNoTracking();
        var pending = await open.CountAsync(c => c.Status == WellKnown.ComplaintStatuses.Pending, ct);
        var inProgress = await open.CountAsync(c => c.Status == WellKnown.ComplaintStatuses.InProgress, ct);
        var mine = agentId is null ? 0
            : await open.CountAsync(c => c.Status == WellKnown.ComplaintStatuses.InProgress && c.AssignedAgentId == agentId, ct);
        return new ComplaintCounts(pending, inProgress, mine);
    }

    public async Task<IReadOnlyList<ComplaintListItem>> GetOpenAsync(int max = 500, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await ctx.Complaints.AsNoTracking()
            .Where(c => c.Status == WellKnown.ComplaintStatuses.Pending || c.Status == WellKnown.ComplaintStatuses.InProgress)
            .OrderBy(c => c.CreatedAt)
            .Take(Math.Clamp(max, 1, 5000))
            .ToListAsync(ct);
        return rows.Select(e => new ComplaintListItem(e.Code.ToString(), MapToModel(e))).ToList();
    }

    public async Task<IReadOnlyList<ComplaintEvent>> GetEventsAsync(string complaintId, CancellationToken ct = default)
    {
        if (!long.TryParse(complaintId, out var code)) return [];
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await ctx.ComplaintEvents.AsNoTracking()
            .Where(e => e.ComplaintCode == code)
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .ToListAsync(ct);
        return rows.Select(e => new ComplaintEvent(DateTime.SpecifyKind(e.At, DateTimeKind.Utc), e.Actor, e.Kind,
            e.FromStatus, e.ToStatus, e.AssignedAgentId, e.Note)).ToList();
    }

    /// <summary>
    /// Koşullu geçiş: <c>status</c> VE <c>version</c> beklenen değerdeyse günceller ve olay satırını AYNI
    /// transaction'da yazar. Etkilenen satır 0 ise (başkası önce davrandı) hiçbir şey yazılmaz → Conflict.
    /// Execution strategy içinde çalışır: NpgsqlRetryingExecutionStrategy elle açılan transaction'ı başka türlü reddeder.
    /// </summary>
    public async Task<ComplaintTransitionOutcome> TryTransitionAsync(ComplaintTransition t, CancellationToken ct = default)
    {
        if (!long.TryParse(t.ComplaintId, out var code))
            return new ComplaintTransitionOutcome(ComplaintTransitionChange.NotFound, null);

        var at = DateTime.SpecifyKind(t.Event.At, DateTimeKind.Utc);
        await using var probe = await _dbFactory.CreateDbContextAsync(ct);
        var strategy = probe.Database.CreateExecutionStrategy();

        var updated = await strategy.ExecuteAsync(async () =>
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            var rows = await ctx.Complaints
                .Where(c => c.Code == code && c.Status == t.ExpectedStatus && c.Version == t.ExpectedVersion)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, t.NewStatus)
                    .SetProperty(c => c.AssignedAgentId, t.AssignedAgentId)
                    .SetProperty(c => c.Resolution, c => t.Resolution ?? c.Resolution)
                    .SetProperty(c => c.UpdatedAt, at)
                    .SetProperty(c => c.Version, c => c.Version + 1), ct);
            if (rows != 1) return false;

            ctx.ComplaintEvents.Add(ToEntity(code, t.Event));
            await ctx.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        });

        var current = Get(t.ComplaintId);
        if (current is null) return new ComplaintTransitionOutcome(ComplaintTransitionChange.NotFound, null);
        return new ComplaintTransitionOutcome(
            updated ? ComplaintTransitionChange.Updated : ComplaintTransitionChange.Conflict, current);
    }

    public async Task<bool> AddEventAsync(string complaintId, ComplaintEvent evt, CancellationToken ct = default)
    {
        if (!long.TryParse(complaintId, out var code)) return false;
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        if (!await ctx.Complaints.AnyAsync(c => c.Code == code, ct)) return false;
        ctx.ComplaintEvents.Add(ToEntity(code, evt));
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    private static ComplaintEventEntity ToEntity(long code, ComplaintEvent e) => new()
    {
        ComplaintCode = code,
        At = DateTime.SpecifyKind(e.At, DateTimeKind.Utc),
        Actor = e.Actor.Length > 128 ? e.Actor[..128] : e.Actor,
        Kind = e.Kind,
        FromStatus = e.FromStatus,
        ToStatus = e.ToStatus,
        AssignedAgentId = e.AssignedAgentId,
        Note = e.Note
    };
```

Not: `ListAsync`'teki koşullu sıralama anahtarları çeviri hatası verirse (`could not be translated`) "tümü" görünümünü iki sorguya böl (önce açıklar `CreatedAt asc`, sonra kapalılar `UpdatedAt desc`; sayfa sınırını iki sorgunun sayılarından hesapla) ve ledger'a `Ruling:` yaz.

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test --project tests/CustomerSupportBot.Adapters.Persistence.Tests -- --filter-class CustomerSupportBot.Adapters.Persistence.Tests.PostgresComplaintLifecycleTests`
Expected: PASS (10 test).

- [ ] **Step 7: Run persistence suite (migration herkesi etkiler)**

Run: `dotnet test --project tests/CustomerSupportBot.Adapters.Persistence.Tests`
Expected: hepsi yeşil (`CatalogRepositoryIntegrationTests` şikayet testleri dahil).

- [ ] **Step 8: Commit**

```bash
git add src/CustomerSupportBot.Application/Ports/Outbound/Persistence/IComplaintRepository.cs src/CustomerSupportBot.Adapters.Persistence tests/CustomerSupportBot.Adapters.Persistence.Tests/PostgresComplaintLifecycleTests.cs
git commit -m "feat(complaints): persist lifecycle with conditional transitions and event history

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Uygulama servisi ve e-posta

**Files:**
- Modify: `src/CustomerSupportBot.Application/Ports/Outbound/EmailOptions.cs` (NotificationOptions)
- Create: `src/CustomerSupportBot.Application/Services/Notifications/ComplaintStatusEmailComposer.cs`
- Create: `src/CustomerSupportBot.Application/Services/Notifications/ComplaintStatusEmailService.cs`
- Create: `src/CustomerSupportBot.Application/Ports/Inbound/IComplaintManagementPort.cs`
- Create: `src/CustomerSupportBot.Application/Services/Complaints/ComplaintManagementService.cs`
- Modify: `src/CustomerSupportBot.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs:151-152`
- Modify: `src/CustomerSupportBot.Api/appsettings.json` (`Email:Notifications`)
- Test: `tests/CustomerSupportBot.Application.Tests/ComplaintManagementServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 (`ComplaintTransitions`, `ComplaintActor`, `ComplaintAction`, `ComplaintEvent`), Task 2 (port kayıtları ve yöntemleri).
- Produces:

```csharp
// Ports/Inbound/IComplaintManagementPort.cs
public enum ComplaintCommandStatus { Updated, NotFound, InvalidTransition, Forbidden, Invalid, Conflict }
public sealed record ComplaintCommandResult(ComplaintCommandStatus Status, ComplaintInfo? Complaint = null, string? Error = null);
/// <param name="Text">Resolve/Reject: müşteriye görünen açıklama; Reopen/Note: not.</param>
/// <param name="InternalNote">Yalnız Resolve/Reject: olay satırına yazılan iç not.</param>
public sealed record ComplaintCommand(ComplaintAction Action, string? AssigneeAgentId = null, string? Text = null,
    string? InternalNote = null, int? ExpectedVersion = null);
public sealed record ComplaintDetail(string ComplaintId, ComplaintInfo Complaint, OrderInfo? Order,
    IReadOnlyList<string> AttachmentIds, IReadOnlyList<ComplaintEvent> Events);
public interface IComplaintManagementPort
{
    Task<ComplaintPage> ListAsync(ComplaintQuery query, CancellationToken ct = default);
    Task<ComplaintCounts> CountsAsync(string? agentId, CancellationToken ct = default);
    Task<ComplaintDetail?> GetAsync(string complaintId, CancellationToken ct = default);
    Task<ComplaintCommandResult> ExecuteAsync(string complaintId, ComplaintActor actor, ComplaintCommand command, CancellationToken ct = default);
}
// ComplaintStatusEmailService
public const string InReview = "in_review"; public const string Closed = "closed";
Task NotifyAsync(string complaintId, ComplaintInfo complaint, string kind, CancellationToken ct = default);
```

- [ ] **Step 1: Write the failing test**

`tests/CustomerSupportBot.Application.Tests/ComplaintManagementServiceTests.cs`:

```csharp
// Şikayet yönetimi: kural → depo → bildirim. Depo sahte; e-posta defteri gerçek (InMemory).

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Complaints;
using CustomerSupportBot.Application.Services.Notifications;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ComplaintManagementServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly ComplaintActor Admin = new(ComplaintActorRole.Admin, null, "admin");
    private static readonly ComplaintActor AgentA = new(ComplaintActorRole.Agent, "a1", "ayse");

    /// <summary>Sahte depo geçişleri <see cref="Current"/>'ı günceller — ardışık komutlar gerçek bir kayıt gibi ilerler.</summary>
    private sealed class Harness
    {
        public ComplaintManagementService Service { get; set; } = null!;
        public IComplaintRepository Repo { get; } = Substitute.For<IComplaintRepository>();
        public List<EmailMessage> Sent { get; } = [];
        public IEmailSender Email { get; } = Substitute.For<IEmailSender>();
        public ComplaintInfo Current { get; set; } = null!;
    }

    private static Harness Build(string status = WellKnown.ComplaintStatuses.Pending, string? assigned = null,
        bool agentActive = true, string? approvalId = null)
    {
        var h = new Harness
        {
            Current = new ComplaintInfo
            {
                OrderId = "1030", CustomerId = "1001", Complaint = "Kutu <ezik> geldi", Status = status,
                AssignedAgentId = assigned, Version = 3, ApprovalId = approvalId,
                CreatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        };
        var repo = h.Repo;
        repo.Get("1006").Returns(_ => h.Current);
        repo.TryTransitionAsync(Arg.Any<ComplaintTransition>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var t = ci.Arg<ComplaintTransition>();
            h.Current = new ComplaintInfo
            {
                OrderId = h.Current.OrderId, CustomerId = h.Current.CustomerId, Complaint = h.Current.Complaint,
                Status = t.NewStatus, AssignedAgentId = t.AssignedAgentId, Resolution = t.Resolution ?? h.Current.Resolution,
                Version = h.Current.Version + 1, CreatedAt = h.Current.CreatedAt, ApprovalId = h.Current.ApprovalId
            };
            return new ComplaintTransitionOutcome(ComplaintTransitionChange.Updated, h.Current);
        });
        repo.AddEventAsync(Arg.Any<string>(), Arg.Any<ComplaintEvent>(), Arg.Any<CancellationToken>()).Returns(true);

        var agents = Substitute.For<IHumanAgentRegistry>();
        agents.Get("a1").Returns(new HumanAgent { Id = "a1", DisplayName = "Ayşe", IsActive = agentActive });
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetEmailAsync(1001, Arg.Any<CancellationToken>()).Returns("musteri@example.com");
        customers.GetFullNameAsync(1001, Arg.Any<CancellationToken>()).Returns("Can Demir");
        h.Email.IsEnabled.Returns(true);
        h.Email.SendAsync(Arg.Do<EmailMessage>(h.Sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var notifier = new ComplaintStatusEmailService(h.Email, new InMemoryNotificationLedger(), customers,
            Options.Create(new EmailOptions { Enabled = true, FromAddress = "destek@example.com" }),
            NullLogger<ComplaintStatusEmailService>.Instance);

        var approvals = Substitute.For<IApprovalQueue>();
        approvals.GetAsync("apr-1", Arg.Any<CancellationToken>()).Returns(new ApprovalRequest
        {
            Id = "apr-1",
            Parameters = new Dictionary<string, object?> { ["attachmentIds"] = new List<string> { "att-1", "att-2" } }
        });
        var orders = Substitute.For<IOrderRepository>();
        orders.Get("1030").Returns(new OrderInfo { CustomerId = "1001", Status = "Teslim Edildi", Lines = [new OrderLine("Kahve", 1)] });

        h.Service = new ComplaintManagementService(repo, orders, approvals, agents, notifier,
            NullLogger<ComplaintManagementService>.Instance);
        return h;
    }

    [Fact]
    public async Task Claim_MovesToInProgress_AssignsActor_AndEmailsInReview()
    {
        var h = Build();

        var r = await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Claim), Ct);

        r.Status.Should().Be(ComplaintCommandStatus.Updated);
        await h.Repo.Received(1).TryTransitionAsync(Arg.Is<ComplaintTransition>(t =>
            t.ExpectedStatus == WellKnown.ComplaintStatuses.Pending && t.ExpectedVersion == 3
            && t.NewStatus == WellKnown.ComplaintStatuses.InProgress && t.AssignedAgentId == "a1"
            && t.Event.Kind == ComplaintEventKinds.Claimed && t.Event.Actor == "ayse"), Arg.Any<CancellationToken>());
        var mail = h.Sent.Should().ContainSingle().Subject;
        mail.Subject.Should().Be("Şikayetiniz inceleniyor — #1006");
        mail.TextBody.Should().Contain("Can Demir").And.Contain("1030");
    }

    [Fact]
    public async Task Resolve_SendsClosedEmail_WithEscapedResolution_AndInternalNoteGoesToEventOnly()
    {
        var h = Build(WellKnown.ComplaintStatuses.InProgress, "a1");

        var r = await h.Service.ExecuteAsync("1006", AgentA,
            new ComplaintCommand(ComplaintAction.Resolve, Text: " Yeni <ürün> gönderildi. ", InternalNote: "depo onayladı"), Ct);

        r.Status.Should().Be(ComplaintCommandStatus.Updated);
        await h.Repo.Received(1).TryTransitionAsync(Arg.Is<ComplaintTransition>(t =>
            t.Resolution == "Yeni <ürün> gönderildi." && t.Event.Note == "depo onayladı"), Arg.Any<CancellationToken>());
        var mail = h.Sent.Should().ContainSingle().Subject;
        mail.Subject.Should().Be("Şikayetiniz çözüldü — #1006");
        mail.TextBody.Should().Contain("Yeni <ürün> gönderildi.").And.NotContain("depo onayladı");
        mail.HtmlBody.Should().Contain("Yeni &lt;ürün&gt; gönderildi.");
    }

    [Fact]
    public async Task Reject_UsesRejectedSubject()
    {
        var h = Build(WellKnown.ComplaintStatuses.InProgress, "a1");

        await h.Service.ExecuteAsync("1006", Admin, new ComplaintCommand(ComplaintAction.Reject, Text: "Hasar kargodan."), Ct);

        h.Sent.Should().ContainSingle().Which.Subject.Should().Be("Şikayetiniz sonuçlandı — #1006");
    }

    [Fact]
    public async Task ReopenThenCloseAgain_SendsSecondClosedEmail()
    {
        var h = Build(WellKnown.ComplaintStatuses.InProgress, "a1");

        await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Resolve, Text: "çözüldü"), Ct);
        await h.Service.ExecuteAsync("1006", Admin, new ComplaintCommand(ComplaintAction.Reopen, Text: "müşteri itiraz etti"), Ct);
        await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Resolve, Text: "ikinci çözüm"), Ct);

        h.Sent.Select(m => m.Subject).Should().Equal(
            "Şikayetiniz çözüldü — #1006", "Şikayetiniz inceleniyor — #1006", "Şikayetiniz çözüldü — #1006");
    }

    [Fact]
    public async Task Reassign_InProgress_DoesNotEmail()
    {
        var h = Build(WellKnown.ComplaintStatuses.InProgress, "b2");

        (await h.Service.ExecuteAsync("1006", Admin, new ComplaintCommand(ComplaintAction.Assign, AssigneeAgentId: "a1"), Ct))
            .Status.Should().Be(ComplaintCommandStatus.Updated);

        h.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Assign_InactiveAgent_Invalid()
    {
        var h = Build(agentActive: false);

        var r = await h.Service.ExecuteAsync("1006", Admin, new ComplaintCommand(ComplaintAction.Assign, AssigneeAgentId: "a1"), Ct);

        r.Status.Should().Be(ComplaintCommandStatus.Invalid);
        await h.Repo.DidNotReceive().TryTransitionAsync(Arg.Any<ComplaintTransition>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(WellKnown.ComplaintStatuses.Pending, null, ComplaintCommandStatus.InvalidTransition)]
    [InlineData(WellKnown.ComplaintStatuses.InProgress, "b2", ComplaintCommandStatus.Forbidden)]
    public async Task Rules_MapToStatus(string status, string? assigned, ComplaintCommandStatus expected)
    {
        var h = Build(status, assigned);

        (await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Resolve, Text: "x"), Ct))
            .Status.Should().Be(expected);
    }

    [Fact]
    public async Task MissingText_Invalid_WithMessage()
    {
        var h = Build(WellKnown.ComplaintStatuses.InProgress, "a1");

        var r = await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Resolve, Text: " "), Ct);

        r.Status.Should().Be(ComplaintCommandStatus.Invalid);
        r.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task StaleExpectedVersion_Conflict_WithoutWriting()
    {
        var h = Build();

        var r = await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Claim, ExpectedVersion: 2), Ct);

        r.Status.Should().Be(ComplaintCommandStatus.Conflict);
        r.Complaint!.Version.Should().Be(3);
        await h.Repo.DidNotReceive().TryTransitionAsync(Arg.Any<ComplaintTransition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RepoConflict_IsConflict_AndNoEmail()
    {
        var h = Build();
        h.Repo.TryTransitionAsync(Arg.Any<ComplaintTransition>(), Arg.Any<CancellationToken>())
            .Returns(new ComplaintTransitionOutcome(ComplaintTransitionChange.Conflict, h.Current));

        (await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Claim), Ct))
            .Status.Should().Be(ComplaintCommandStatus.Conflict);
        h.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task EmailFailure_DoesNotUndoTheTransition()
    {
        var h = Build();
        h.Email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("smtp")));

        (await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Claim), Ct))
            .Status.Should().Be(ComplaintCommandStatus.Updated);
    }

    [Fact]
    public async Task Note_WritesEventOnly()
    {
        var h = Build(WellKnown.ComplaintStatuses.InProgress, "a1");

        (await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Note, Text: "kargoya soruldu"), Ct))
            .Status.Should().Be(ComplaintCommandStatus.Updated);

        await h.Repo.Received(1).AddEventAsync("1006",
            Arg.Is<ComplaintEvent>(e => e.Kind == ComplaintEventKinds.Note && e.Note == "kargoya soruldu"), Arg.Any<CancellationToken>());
        await h.Repo.DidNotReceive().TryTransitionAsync(Arg.Any<ComplaintTransition>(), Arg.Any<CancellationToken>());
        h.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_NotFound()
        => (await Build().Service.ExecuteAsync("404", AgentA, new ComplaintCommand(ComplaintAction.Claim), Ct))
            .Status.Should().Be(ComplaintCommandStatus.NotFound);

    [Fact]
    public async Task Detail_IncludesOrder_PhotosFromApproval_AndEvents()
    {
        var h = Build(approvalId: "apr-1");
        h.Repo.GetEventsAsync("1006", Arg.Any<CancellationToken>()).Returns(
            [new ComplaintEvent(DateTime.UtcNow, "system", ComplaintEventKinds.Created, null, "Beklemede", null, null)]);

        var d = await h.Service.GetAsync("1006", Ct);

        d!.Order!.LinesSummary().Should().Be("Kahve x1");
        d.AttachmentIds.Should().Equal("att-1", "att-2");
        d.Events.Should().ContainSingle();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/CustomerSupportBot.Application.Tests -- --filter-class CustomerSupportBot.Application.Tests.ComplaintManagementServiceTests`
Expected: derleme hatası — `ComplaintManagementService`, `ComplaintStatusEmailService` yok.

- [ ] **Step 3: E-posta seçenekleri, oluşturucu ve servis**

`EmailOptions.cs` → `NotificationOptions` sonuna:

```csharp
        /// <summary>Şikayet incelemeye alındığında (ya da yeniden açıldığında) müşteriye e-posta.</summary>
        public bool ComplaintInReview { get; set; } = true;

        /// <summary>Şikayet çözüldüğünde ya da reddedildiğinde (açıklamayla) müşteriye e-posta.</summary>
        public bool ComplaintClosed { get; set; } = true;
```

`appsettings.json` → `Email:Notifications` bloğu:

```json
    "Notifications": {
      "ApprovalResults": true,
      "OrderShipped": true,
      "OrderDelivered": true,
      "ComplaintInReview": true,
      "ComplaintClosed": true
    }
```

`ComplaintStatusEmailComposer.cs`:

```csharp
// Application/Services/Notifications/ComplaintStatusEmailComposer.cs
// Şikayet durum e-postasının metni (Türkçe, düz metin + HTML). İç not ASLA girmez.

using System.Net;
using System.Text;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Notifications;

public static class ComplaintStatusEmailComposer
{
    public static EmailMessage Compose(string complaintId, ComplaintInfo complaint, string kind, string to, string? toName, string? publicBaseUrl)
    {
        var greeting = string.IsNullOrWhiteSpace(toName) ? "Merhaba," : $"Merhaba {toName},";
        var link = string.IsNullOrWhiteSpace(publicBaseUrl) ? null : publicBaseUrl.TrimEnd('/') + "/";
        var (subjectWord, sentence) = kind == ComplaintStatusEmailService.InReview
            ? ("inceleniyor", $"#{complaintId} numaralı şikayetiniz (sipariş #{complaint.OrderId}) bir temsilcimiz tarafından incelemeye alındı.")
            : complaint.Status == WellKnown.ComplaintStatuses.Rejected
                ? ("sonuçlandı", $"#{complaintId} numaralı şikayetiniz (sipariş #{complaint.OrderId}) incelendi ve sonuçlandırıldı.")
                : ("çözüldü", $"#{complaintId} numaralı şikayetiniz (sipariş #{complaint.OrderId}) çözüldü.");
        var resolution = kind == ComplaintStatusEmailService.Closed && !string.IsNullOrWhiteSpace(complaint.Resolution)
            ? complaint.Resolution : null;

        var text = new StringBuilder().AppendLine(greeting).AppendLine().AppendLine(sentence);
        if (resolution is not null) text.AppendLine().AppendLine($"Açıklama: {resolution}");
        if (link is not null) text.AppendLine().AppendLine($"Sorularınız için: {link}");
        text.AppendLine().AppendLine("Bu e-posta otomatik olarak gönderilmiştir.");

        var html = new StringBuilder()
            .Append("<p>").Append(Enc(greeting)).Append("</p>")
            .Append("<p>").Append(Enc(sentence)).Append("</p>");
        if (resolution is not null) html.Append("<p><strong>Açıklama:</strong> ").Append(Enc(resolution)).Append("</p>");
        if (link is not null) html.Append("<p><a href=\"").Append(Enc(link)).Append("\">Destek asistanına sorun</a></p>");
        html.Append("<p style=\"color:#64748b;font-size:12px\">Bu e-posta otomatik olarak gönderilmiştir.</p>");

        return new EmailMessage(to, toName, $"Şikayetiniz {subjectWord} — #{complaintId}", text.ToString(), html.ToString());
    }

    private static string Enc(string s) => WebUtility.HtmlEncode(s);
}
```

`ComplaintStatusEmailService.cs`:

```csharp
// Application/Services/Notifications/ComplaintStatusEmailService.cs
// Şikayet incelemede / kapandı e-postası — OrderStatusEmailService ile aynı güvenceler.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Logging;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Notifications;

/// <summary>
/// Anahtar <c>complaint:{no}:{tür}:{sürüm}</c> — sürüm her geçişte arttığı için yeniden açılıp tekrar kapanan
/// şikayet yeni e-posta alır, aynı geçişin iki pod'da işlenmesi ise tek e-posta gönderir. Gönderim
/// başarısızsa talep geri bırakılır; hata geçişi geri almaz.
/// </summary>
public sealed class ComplaintStatusEmailService(
    IEmailSender sender,
    INotificationLedger ledger,
    ICustomerRepository customers,
    IOptions<EmailOptions> options,
    ILogger<ComplaintStatusEmailService> logger)
{
    public const string InReview = "in_review";
    public const string Closed = "closed";

    private readonly EmailOptions _options = options.Value;

    public async Task NotifyAsync(string complaintId, ComplaintInfo complaint, string kind, CancellationToken ct = default)
    {
        var enabled = kind switch
        {
            InReview => _options.Notifications.ComplaintInReview,
            Closed => _options.Notifications.ComplaintClosed,
            _ => false
        };
        if (!_options.Enabled || !enabled) return;
        if (!long.TryParse(complaint.CustomerId, out var customerId)) return;

        var key = $"complaint:{complaintId}:{kind}:{complaint.Version}";
        if (!await ledger.TryClaimAsync(key, ct)) return;

        try
        {
            var email = await customers.GetEmailAsync(customerId, ct);
            if (string.IsNullOrWhiteSpace(email))
            {
                logger.LogInformation("[Email] Şikayet bildirimi gönderilmedi — müşterinin e-posta adresi yok | complaint={ComplaintId}", complaintId);
                return;
            }

            var name = await customers.GetFullNameAsync(customerId, ct);
            await sender.SendAsync(ComplaintStatusEmailComposer.Compose(complaintId, complaint, kind, email, name, _options.PublicBaseUrl), ct);
            logger.LogInformation("[Email] Şikayet bildirimi gönderildi | complaint={ComplaintId} kind={Kind} to={To}",
                complaintId, kind, PiiMasker.MaskEmail(email));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[Email] Şikayet bildirimi gönderilemedi | complaint={ComplaintId} kind={Kind}", complaintId, kind);
            try { await ledger.ReleaseAsync(key, CancellationToken.None); }
            catch (Exception releaseEx) { logger.LogWarning(releaseEx, "[Email] Defter kaydı geri bırakılamadı | key={Key}", key); }
        }
    }
}
```

- [ ] **Step 4: Port ve servis**

`IComplaintManagementPort.cs` — "Produces" bloğundaki türler; dosya başı:

```csharp
// Ports/Inbound/IComplaintManagementPort.cs
// Panel/API için şikayet yönetimi: liste, sayaç, ayrıntı, durum komutları.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;

namespace CustomerSupportBot.Application.Ports.Inbound;
```

`ComplaintManagementService.cs`:

```csharp
// Application/Services/Complaints/ComplaintManagementService.cs
// Şikayet yaşam döngüsü: kural (ComplaintTransitions) → koşullu depo geçişi → müşteri bildirimi.

using System.Text.Json;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Application.Services.Notifications;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Complaints;

public sealed class ComplaintManagementService(
    IComplaintRepository complaints,
    IOrderRepository orders,
    IApprovalQueue approvals,
    IHumanAgentRegistry agents,
    ComplaintStatusEmailService notifier,
    ILogger<ComplaintManagementService> logger,
    TimeProvider? clock = null) : IComplaintManagementPort
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task<ComplaintPage> ListAsync(ComplaintQuery query, CancellationToken ct = default) =>
        complaints.ListAsync(query, ct);

    public Task<ComplaintCounts> CountsAsync(string? agentId, CancellationToken ct = default) =>
        complaints.CountOpenAsync(agentId, ct);

    public async Task<ComplaintDetail?> GetAsync(string complaintId, CancellationToken ct = default)
    {
        var complaint = complaints.Get(complaintId);
        if (complaint is null) return null;
        var events = await complaints.GetEventsAsync(complaintId, ct);
        var attachmentIds = await AttachmentIdsAsync(complaint.ApprovalId, ct);
        return new ComplaintDetail(complaintId, complaint, orders.Get(complaint.OrderId), attachmentIds, events);
    }

    public async Task<ComplaintCommandResult> ExecuteAsync(
        string complaintId, ComplaintActor actor, ComplaintCommand command, CancellationToken ct = default)
    {
        var complaint = complaints.Get(complaintId);
        if (complaint is null) return new ComplaintCommandResult(ComplaintCommandStatus.NotFound);

        var check = ComplaintTransitions.Check(command.Action, complaint, actor, command.AssigneeAgentId, command.Text);
        if (!check.Allowed)
            return check.Error switch
            {
                ComplaintTransitionErrors.InvalidTransition => new(ComplaintCommandStatus.InvalidTransition, complaint, "Şikayet bu durumdayken bu işlem yapılamaz."),
                ComplaintTransitionErrors.Forbidden => new(ComplaintCommandStatus.Forbidden, complaint, "Bu şikayet size atanmamış."),
                ComplaintTransitionErrors.AssigneeRequired => new(ComplaintCommandStatus.Invalid, complaint, "Bir temsilci seçin."),
                ComplaintTransitionErrors.TooLong => new(ComplaintCommandStatus.Invalid, complaint, $"Metin en fazla {ComplaintTransitions.MaxTextLength} karakter olabilir."),
                _ => new(ComplaintCommandStatus.Invalid, complaint,
                    command.Action is ComplaintAction.Resolve or ComplaintAction.Reject
                        ? "Müşteriye gönderilecek açıklama zorunludur."
                        : "Not zorunludur.")
            };

        if (command.Action == ComplaintAction.Assign && agents.Get(command.AssigneeAgentId!.Trim()) is not { IsActive: true })
            return new ComplaintCommandResult(ComplaintCommandStatus.Invalid, complaint, "Geçerli, aktif bir temsilci seçin.");

        if (command.ExpectedVersion is { } expected && expected != complaint.Version)
            return new ComplaintCommandResult(ComplaintCommandStatus.Conflict, complaint, "Şikayet başka biri tarafından güncellendi.");

        var now = _clock.GetUtcNow().UtcDateTime;
        var text = command.Text?.Trim();

        if (command.Action == ComplaintAction.Note)
        {
            var added = await complaints.AddEventAsync(complaintId,
                new ComplaintEvent(now, actor.Name, ComplaintEventKinds.Note, null, null, null, text), ct);
            return added
                ? new ComplaintCommandResult(ComplaintCommandStatus.Updated, complaint)
                : new ComplaintCommandResult(ComplaintCommandStatus.NotFound);
        }

        var assignee = command.Action switch
        {
            ComplaintAction.Claim => actor.AgentId,
            ComplaintAction.Assign => command.AssigneeAgentId!.Trim(),
            _ => complaint.AssignedAgentId
        };
        var (kind, note, resolution) = command.Action switch
        {
            ComplaintAction.Claim => (ComplaintEventKinds.Claimed, (string?)null, (string?)null),
            ComplaintAction.Assign => (ComplaintEventKinds.Assigned, null, null),
            ComplaintAction.Resolve => (ComplaintEventKinds.Resolved, Blank(command.InternalNote), text),
            ComplaintAction.Reject => (ComplaintEventKinds.Rejected, Blank(command.InternalNote), text),
            _ => (ComplaintEventKinds.Reopened, text, null)
        };

        var outcome = await complaints.TryTransitionAsync(new ComplaintTransition(
            complaintId, complaint.Status, complaint.Version, check.TargetStatus!, assignee, resolution,
            new ComplaintEvent(now, actor.Name, kind, complaint.Status, check.TargetStatus, assignee, note)), ct);

        switch (outcome.Change)
        {
            case ComplaintTransitionChange.NotFound:
                return new ComplaintCommandResult(ComplaintCommandStatus.NotFound);
            case ComplaintTransitionChange.Conflict:
                return new ComplaintCommandResult(ComplaintCommandStatus.Conflict, outcome.Complaint, "Şikayet başka biri tarafından güncellendi.");
        }

        var updated = outcome.Complaint!;
        var emailKind = command.Action switch
        {
            ComplaintAction.Claim or ComplaintAction.Reopen => ComplaintStatusEmailService.InReview,
            ComplaintAction.Assign when complaint.Status == WellKnown.ComplaintStatuses.Pending => ComplaintStatusEmailService.InReview,
            ComplaintAction.Resolve or ComplaintAction.Reject => ComplaintStatusEmailService.Closed,
            _ => null
        };
        if (emailKind is not null) await notifier.NotifyAsync(complaintId, updated, emailKind, ct);

        logger.LogInformation("[Complaint] {Action} complaint={ComplaintId} {From}→{To} by={Actor}",
            ComplaintTransitions.ActionName(command.Action), complaintId, complaint.Status, updated.Status, actor.Name);
        return new ComplaintCommandResult(ComplaintCommandStatus.Updated, updated);
    }

    private async Task<IReadOnlyList<string>> AttachmentIdsAsync(string? approvalId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(approvalId)) return [];
        var approval = await approvals.GetAsync(approvalId, ct);
        if (approval is null
            || !approval.Parameters.TryGetValue(SideEffectApprovalGate.AttachmentIdsParameter, out var raw) || raw is null)
            return [];
        return raw switch
        {
            JsonElement { ValueKind: JsonValueKind.Array } arr => arr.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList(),
            IEnumerable<string> list => list.ToList(),
            _ => []
        };
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
```

`ApplicationServiceCollectionExtensions.cs` — satır 152'nin altına:

```csharp
        services.AddSingleton<Services.Notifications.ComplaintStatusEmailService>();
        services.AddSingleton<IComplaintManagementPort, Services.Complaints.ComplaintManagementService>();
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --project tests/CustomerSupportBot.Application.Tests -- --filter-class CustomerSupportBot.Application.Tests.ComplaintManagementServiceTests`
Expected: PASS.

- [ ] **Step 6: Run the application suite**

Run: `dotnet test --project tests/CustomerSupportBot.Application.Tests`
Expected: hepsi yeşil (`EmailOptionsValidatorTests` dahil).

- [ ] **Step 7: Commit**

```bash
git add src/CustomerSupportBot.Application src/CustomerSupportBot.Api/appsettings.json tests/CustomerSupportBot.Application.Tests/ComplaintManagementServiceTests.cs
git commit -m "feat(complaints): management service with in-review and closed emails

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Onaydan şikayete fotoğraf bağı ve botun durum yanıtı

**Files:**
- Modify: `src/CustomerSupportBot.Application/Ports/Outbound/IComplaintToolsService.cs:10`
- Modify: `src/CustomerSupportBot.Application/Services/Tools/ComplaintToolsService.cs:30-33,87-92,138-146`
- Modify: `src/CustomerSupportBot.Application/Services/Tools/CustomerSupportToolsService.cs:66-67`
- Modify: `src/CustomerSupportBot.Application/Services/Approval/ApprovalExecutionRouter.cs:33-36`
- Modify: `tests/CustomerSupportBot.Application.Tests/ApprovalExecutionRouterTests.cs:144,157`
- Test: `tests/CustomerSupportBot.Application.Tests/ComplaintStatusToolTests.cs`

**Interfaces:**
- Consumes: Task 1 `ComplaintInfo` alanları, `WellKnown.ComplaintStatuses.IsClosed`.
- Produces: `ToolResult ComplaintRegistrationTool(string orderId, string complaintText, string? customerId = null, string? approvalId = null)` (üç arayüz/sınıfta). `approvalId` **AIFunction şemasına girmez**: ajan tarafı aracı `ApprovalGateService.BuildComplaintRegistrationTool` içindeki açık parametreli lambda ile kuruyor; yalnızca onay sonrası yönlendirici geçirir.

- [ ] **Step 1: Write the failing test**

`tests/CustomerSupportBot.Application.Tests/ComplaintStatusToolTests.cs`:

```csharp
// Bot şikayet durumunu söylerken: kapanmışsa açıklama + tarih; iç not ve temsilci asla.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Tools;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Tests;

public class ComplaintStatusToolTests
{
    private static ComplaintToolsService Build(ComplaintInfo complaint, out IComplaintRepository repo)
    {
        repo = Substitute.For<IComplaintRepository>();
        repo.Get("1006").Returns(complaint);
        var orders = Substitute.For<IOrderRepository>();
        orders.Get("1030").Returns(new OrderInfo { CustomerId = "1001", Status = "Teslim Edildi", Lines = [new OrderLine("Kahve", 1)] });
        return new ComplaintToolsService(repo, orders);
    }

    [Fact]
    public void StatusTool_ShowsResolution_NotInternalNote()
    {
        var svc = Build(new ComplaintInfo
        {
            OrderId = "1030", CustomerId = "1001", Complaint = "kırık", Status = WellKnown.ComplaintStatuses.Resolved,
            Resolution = "Yeni ürün gönderildi.", AssignedAgentId = "a1", UpdatedAt = new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc)
        }, out _);

        var r = svc.ComplaintStatusTool("1006", "1001");

        r.Success.Should().BeTrue();
        r.Message.Should().Contain("Durum: Çözüldü").And.Contain("Açıklama: Yeni ürün gönderildi.")
            .And.Contain("Son güncelleme: 12.10.2026").And.NotContain("a1");
    }

    [Fact]
    public void StatusTool_Open_HasNoResolutionLine()
    {
        var svc = Build(new ComplaintInfo
        {
            OrderId = "1030", CustomerId = "1001", Complaint = "kırık", Status = WellKnown.ComplaintStatuses.InProgress,
            Resolution = "eski açıklama"
        }, out _);

        svc.ComplaintStatusTool("1006", "1001").Message.Should().Contain("Durum: İncelemede").And.NotContain("Açıklama");
    }

    [Fact]
    public void Registration_StoresApprovalId()
    {
        var svc = Build(new ComplaintInfo(), out var repo);
        repo.Create(Arg.Any<ComplaintInfo>()).Returns("1007");

        svc.ComplaintRegistrationTool("1030", "Ürün kırık geldi, değişim istiyorum", "1001", approvalId: "apr-9").Success.Should().BeTrue();

        repo.Received(1).Create(Arg.Is<ComplaintInfo>(c => c.ApprovalId == "apr-9"));
    }
}
```

`ApprovalExecutionRouterTests.cs` — satır 144 ve 157'yi şöyle değiştir (yönlendirici artık onay kimliğini geçiriyor):

```csharp
        tools.ComplaintRegistrationTool(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(ToolResult.Ok("kaydedildi"));

        var request = new ApprovalRequest
        {
            ToolName = WellKnown.ToolNames.ComplaintRegistration,
            CustomerId = "1027",
            Parameters = new Dictionary<string, object?>
            {
                ["orderId"] = "1030", ["complaintText"] = "ürün hasarlı geldi", ["customerId"] = "9999"
            }
        };
        await new ApprovalExecutionRouter(tools).ExecuteAsync(request, TestContext.Current.CancellationToken);

        tools.Received(1).ComplaintRegistrationTool("1030", "ürün hasarlı geldi", "1027", request.Id);
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --project tests/CustomerSupportBot.Application.Tests -- --filter-class CustomerSupportBot.Application.Tests.ComplaintStatusToolTests --filter-class CustomerSupportBot.Application.Tests.ApprovalExecutionRouterTests`
Expected: derleme hatası (`approvalId` parametresi yok).

- [ ] **Step 3: Implement**

`IComplaintToolsService.cs` satır 10 ve `CustomerSupportToolsService.cs` 66-67:

```csharp
    ToolResult ComplaintRegistrationTool(string orderId, string complaintText, string? customerId = null, string? approvalId = null);
```

```csharp
    public ToolResult ComplaintRegistrationTool(string orderId, string complaintText, string? customerId = null, string? approvalId = null)
        => _complaint.ComplaintRegistrationTool(orderId, complaintText, customerId, approvalId);
```

`ComplaintToolsService.cs` — imzaya son parametreyi ekle (Description özniteliği **eklenmez**):

```csharp
        [Description("Müşteri kimlik numarası (opsiyonel; boşsa siparişten türetilir)")] string? customerId = null,
        string? approvalId = null)
```

`Create` çağrısına `ApprovalId = approvalId,` ekle. `ComplaintStatusTool`'un `return ToolResult.Ok(...)` kısmını değiştir:

```csharp
        var message = $"Şikayet No: {complaintId}, Sipariş: {complaint.OrderId}, Durum: {complaint.Status}";
        var closed = WellKnown.ComplaintStatuses.IsClosed(complaint.Status);
        if (closed && !string.IsNullOrWhiteSpace(complaint.Resolution)) message += $", Açıklama: {complaint.Resolution}";
        if (complaint.UpdatedAt != default) message += $", Son güncelleme: {complaint.UpdatedAt:dd.MM.yyyy}";

        // İç notlar, olay geçmişi ve atanan temsilci bilerek DIŞARIDA: bu metin müşteriye gider.
        return ToolResult.Ok(
            message: message + ".",
            data: new
            {
                complaintId,
                orderId = complaint.OrderId,
                status = complaint.Status,
                complaint = complaint.Complaint,
                resolution = closed ? complaint.Resolution : null,
                updatedAt = complaint.UpdatedAt == default ? (DateTime?)null : complaint.UpdatedAt
            });
```

`ApprovalExecutionRouter.cs` satır 33-36:

```csharp
            WellKnown.ToolNames.ComplaintRegistration => _tools.ComplaintRegistrationTool(
                GetString(p, "orderId") ?? "",
                GetString(p, "complaintText") ?? "",
                customerId,
                request.Id),
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/CustomerSupportBot.Application.Tests -- --filter-class CustomerSupportBot.Application.Tests.ComplaintStatusToolTests --filter-class CustomerSupportBot.Application.Tests.ApprovalExecutionRouterTests --filter-class CustomerSupportBot.Application.Tests.ComplaintReadOnlyToolsTests --filter-class CustomerSupportBot.Application.Tests.CustomerSupportToolsTests`
Expected: PASS. `ComplaintReadOnlyToolsTests` mesajın tam metnini doğruluyorsa (`"...Durum: Beklemede."`) yeni biçimle uyumlu olmalı: açık şikayette `UpdatedAt` default ise mesaj eskisiyle birebir aynıdır.

- [ ] **Step 5: Run Application + Agents suites**

Run: `dotnet test --project tests/CustomerSupportBot.Application.Tests && dotnet test --project tests/CustomerSupportBot.Adapters.Agents.Tests`
Expected: yeşil (`ApprovalGateServiceToolBuilderTests` AIFunction şemasının değişmediğini doğrular).

- [ ] **Step 6: Commit**

```bash
git add src/CustomerSupportBot.Application tests/CustomerSupportBot.Application.Tests
git commit -m "feat(complaints): link the approval to the complaint; bot reports resolution

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: API uçları

**Files:**
- Create: `src/CustomerSupportBot.Api/Endpoints/ComplaintEndpoints.cs`
- Modify: `src/CustomerSupportBot.Api/Program.cs:204,209` (kayıt)
- Test: `tests/CustomerSupportBot.Api.IntegrationTests/ComplaintEndpointTests.cs`

**Interfaces:**
- Consumes: Task 3 `IComplaintManagementPort` ve kayıtları; Task 1 `ComplaintTransitions.AvailableActions/ActionName`.
- Ayrıca bu görev şunları **ekler** (Task 6 kullanır): `ComplaintSlaOptions` sınıfı, `SlaOptions.Complaints` özelliği ve `SlaPolicyEvaluator.ComplaintTone(ComplaintInfo, ComplaintSlaOptions, DateTime) → "none"|"ok"|"warn"|"breach"` (listedeki `sla` alanı için).
- Produces (JSON, camelCase):
  - Liste: `{ items: [ComplaintView], total, page, pageSize }`
  - `ComplaintView`: `complaintId, orderId, customerId, complaint, status, assignedAgentId, assignedAgentName, resolution, createdAt, updatedAt, version, sla ("ok"|"warn"|"breach"|"none"), actions: string[]`
  - Ayrıntı: `{ complaint: ComplaintView, order: { status, linesSummary } | null, attachmentIds: string[], events: [{ at, actor, kind, fromStatus, toStatus, assignedAgentId, note }] }`
  - Sayaç: `{ pending, inProgress, assignedToMe }`
  - Hata: 400/403/404 `{ error }`; 409 `{ error, currentStatus, version }`

Uç tablosu (admin: `adminScope`, temsilci: `agentScope` — `AdminOrAgent`):

| Uç | Grup |
|---|---|
| `GET /complaints?status=&assigned=&page=&pageSize=` | admin |
| `GET /complaints/counts` | admin |
| `GET /complaints/{code}` | admin |
| `POST /complaints/{code}/assign` `{agentId, expectedVersion?}` | admin |
| `POST /complaints/{code}/{resolve|reject}` `{text, note?, expectedVersion?}` | admin |
| `POST /complaints/{code}/reopen` `{text, expectedVersion?}`, `/notes` `{text}` | admin |
| `GET /agent/complaints?status=&mine=&page=` | agent |
| `GET /agent/complaints/counts`, `GET /agent/complaints/{code}` | agent |
| `POST /agent/complaints/{code}/claim` `{expectedVersion?}` | agent |
| `POST /agent/complaints/{code}/{resolve|reject|reopen|notes}` | agent |

Temsilci uçlarında aktör: `Admin` rolündeyse admin (sınırsız kapsam — `AgentPanelEndpoints`'teki "sınırsız erişim Admin rolünden türetilir" kuralı), değilse `linked_agent_id` claim'i; yoksa 400 `{ error: "Hesabınız bir temsilci kaydına bağlı değil." }`.

- [ ] **Step 1: Write the failing test**

`tests/CustomerSupportBot.Api.IntegrationTests/ComplaintEndpointTests.cs`:

```csharp
// Şikayet uçları: yetki, kapsam ve durum kodu eşlemesi. İş mantığı ComplaintManagementServiceTests'te.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Auth;
using CustomerSupportBot.Domain.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomerSupportBot.Api.IntegrationTests;

public class ComplaintEndpointTests : IClassFixture<ComplaintEndpointTests.Factory>
{
    public sealed class Factory : TestWebApplicationFactory
    {
        public IComplaintManagementPort Port { get; } = Substitute.For<IComplaintManagementPort>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IComplaintManagementPort>();
                services.AddSingleton(Port);
            });
        }
    }

    private readonly Factory _factory;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ComplaintInfo C(string status, string? assigned = null) => new()
    {
        OrderId = "1030", CustomerId = "1001", Complaint = "kırık", Status = status, AssignedAgentId = assigned, Version = 4,
        CreatedAt = DateTime.UtcNow.AddHours(-1), UpdatedAt = DateTime.UtcNow
    };

    public ComplaintEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Port.ClearReceivedCalls();
        _factory.Port.ListAsync(Arg.Any<ComplaintQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ComplaintPage([new ComplaintListItem("1006", C(WellKnown.ComplaintStatuses.Pending))], 1));
        _factory.Port.CountsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new ComplaintCounts(2, 3, 1));
        _factory.Port.GetAsync("1006", Arg.Any<CancellationToken>()).Returns(new ComplaintDetail("1006",
            C(WellKnown.ComplaintStatuses.InProgress, "agent-1"),
            new OrderInfo { CustomerId = "1001", Status = "Teslim Edildi", Lines = [new OrderLine("Kahve", 1)] },
            ["att-1"], [new ComplaintEvent(DateTime.UtcNow, "ayse", ComplaintEventKinds.Note, null, null, null, "iç not")]));
        _factory.Port.GetAsync("404", Arg.Any<CancellationToken>()).Returns((ComplaintDetail?)null);
        _factory.Port.ExecuteAsync(Arg.Any<string>(), Arg.Any<ComplaintActor>(), Arg.Any<ComplaintCommand>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<string>(0) switch
            {
                "404" => new ComplaintCommandResult(ComplaintCommandStatus.NotFound),
                "409" => new ComplaintCommandResult(ComplaintCommandStatus.Conflict, C(WellKnown.ComplaintStatuses.InProgress, "b2"), "güncellendi"),
                "403" => new ComplaintCommandResult(ComplaintCommandStatus.Forbidden, C(WellKnown.ComplaintStatuses.InProgress, "b2"), "Bu şikayet size atanmamış."),
                "400" => new ComplaintCommandResult(ComplaintCommandStatus.Invalid, C(WellKnown.ComplaintStatuses.InProgress), "Açıklama zorunludur."),
                _ => new ComplaintCommandResult(ComplaintCommandStatus.Updated, C(WellKnown.ComplaintStatuses.InProgress, "agent-1"))
            });
    }

    private HttpClient ClientFor(string role, string? linkedAgentId = "agent-1")
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>().GenerateAccessToken(
            new UserInfo(Guid.NewGuid().ToString("N"), "ayse", "", role,
                role == "Agent" ? linkedAgentId : null, true, DateTime.UtcNow, null, role == "Customer" ? "1001" : null),
            DateTime.UtcNow).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Admin_List_ReturnsItemsWithActionsAndSla()
    {
        var json = await ClientFor("Admin").GetFromJsonAsync<JsonElement>("/complaints?status=open", Ct);

        json.GetProperty("total").GetInt32().Should().Be(1);
        var item = json.GetProperty("items")[0];
        item.GetProperty("complaintId").GetString().Should().Be("1006");
        item.GetProperty("actions").EnumerateArray().Select(a => a.GetString()).Should().Equal("assign", "note");
        item.GetProperty("sla").GetString().Should().Be("ok");
        await _factory.Port.Received(1).ListAsync(Arg.Is<ComplaintQuery>(q => q.Status == "open" && q.VisibleToAgentId == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Agent_List_IsScopedToUnassignedAndMine()
    {
        await ClientFor("Agent").GetFromJsonAsync<JsonElement>("/agent/complaints?mine=true", Ct);

        await _factory.Port.Received(1).ListAsync(
            Arg.Is<ComplaintQuery>(q => q.VisibleToAgentId == "agent-1" && q.AssignedAgentId == "agent-1"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Agent_Claim_PassesActor()
    {
        var response = await ClientFor("Agent").PostAsJsonAsync("/agent/complaints/1006/claim", new { expectedVersion = 4 }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Port.Received(1).ExecuteAsync("1006",
            Arg.Is<ComplaintActor>(a => a.Role == ComplaintActorRole.Agent && a.AgentId == "agent-1" && a.Name == "ayse"),
            Arg.Is<ComplaintCommand>(c => c.Action == ComplaintAction.Claim && c.ExpectedVersion == 4), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Agent_CannotResolveSomeoneElses()
        => (await ClientFor("Agent").PostAsJsonAsync("/agent/complaints/403/resolve", new { text = "x" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

    [Fact]
    public async Task StatusCodes()
    {
        var admin = ClientFor("Admin");
        (await admin.PostAsJsonAsync("/complaints/404/resolve", new { text = "x" }, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsJsonAsync("/complaints/400/resolve", new { text = "" }, Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var conflict = await admin.PostAsJsonAsync("/complaints/409/assign", new { agentId = "agent-1" }, Ct);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await conflict.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("currentStatus").GetString().Should().Be(WellKnown.ComplaintStatuses.InProgress);
        body.GetProperty("version").GetInt32().Should().Be(4);
        (await admin.GetAsync("/complaints/404", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Detail_IncludesEventsAndPhotos()
    {
        var json = await ClientFor("Agent").GetFromJsonAsync<JsonElement>("/agent/complaints/1006", Ct);

        json.GetProperty("attachmentIds")[0].GetString().Should().Be("att-1");
        json.GetProperty("events")[0].GetProperty("note").GetString().Should().Be("iç not");
        json.GetProperty("order").GetProperty("linesSummary").GetString().Should().Be("Kahve x1");
        json.GetProperty("complaint").GetProperty("actions").EnumerateArray().Select(a => a.GetString())
            .Should().Equal("resolve", "reject", "note");
    }

    [Fact]
    public async Task Counts()
    {
        var json = await ClientFor("Agent").GetFromJsonAsync<JsonElement>("/agent/complaints/counts", Ct);
        json.GetProperty("assignedToMe").GetInt32().Should().Be(1);
        await _factory.Port.Received(1).CountsAsync("agent-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorization()
    {
        (await ClientFor("Agent").GetAsync("/complaints", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ClientFor("Agent").PostAsJsonAsync("/complaints/1006/assign", new { agentId = "x" }, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ClientFor("Customer").GetAsync("/agent/complaints", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ClientFor("Agent", linkedAgentId: null).PostAsJsonAsync("/agent/complaints/1006/claim", new { }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/CustomerSupportBot.Api.IntegrationTests -- --filter-class CustomerSupportBot.Api.IntegrationTests.ComplaintEndpointTests`
Expected: 404'ler / derleme hatası (uçlar yok).

- [ ] **Step 3: SLA seçenek sınıfı**

`src/CustomerSupportBot.Application/Services/Sla/SlaOptions.cs` — `SlaOptions` sınıfına `public ComplaintSlaOptions Complaints { get; set; } = new();` ekle ve dosyaya:

```csharp
public class ComplaintSlaOptions
{
    /// <summary>Beklemede (üstlenilmeden) bekleme — uyarı (varsayılan 4 saat).</summary>
    public int PickupWarnAfterSeconds { get; set; } = 14400;

    /// <summary>Beklemede bekleme — ihlal (varsayılan 24 saat).</summary>
    public int PickupBreachAfterSeconds { get; set; } = 86400;

    /// <summary>Açık kalma toplamı (oluşturmadan beri) — uyarı (varsayılan 48 saat).</summary>
    public int ResolveWarnAfterSeconds { get; set; } = 172800;

    /// <summary>Açık kalma toplamı — ihlal (varsayılan 72 saat).</summary>
    public int ResolveBreachAfterSeconds { get; set; } = 259200;
}
```

`SlaPolicyEvaluator.cs` — sınıfa:

```csharp
    /// <summary>Panel rengi: "none" (kapalı), "breach", "warn", "ok". Beklemede ise üstlenme, değilse çözüm eşikleri.</summary>
    public static string ComplaintTone(ComplaintInfo c, ComplaintSlaOptions o, DateTime nowUtc)
    {
        if (!WellKnown.ComplaintStatuses.IsOpen(c.Status) || c.CreatedAt == default) return "none";
        var age = (nowUtc - c.CreatedAt).TotalSeconds;
        var pending = c.Status == WellKnown.ComplaintStatuses.Pending;
        if (age >= o.ResolveBreachAfterSeconds || (pending && age >= o.PickupBreachAfterSeconds)) return "breach";
        if (age >= o.ResolveWarnAfterSeconds || (pending && age >= o.PickupWarnAfterSeconds)) return "warn";
        return "ok";
    }
```

- [ ] **Step 4: Uçları yaz**

`src/CustomerSupportBot.Api/Endpoints/ComplaintEndpoints.cs`:

```csharp
// Endpoints/ComplaintEndpoints.cs
// Şikayet yönetimi. Admin: /complaints (adminScope). Temsilci: /agent/complaints (agentScope, AdminOrAgent).
// Kural ve yetki ComplaintManagementService + ComplaintTransitions'ta; burada aktör çıkarımı ve HTTP eşlemesi.

using System.Security.Claims;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Sla;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Endpoints;

public static class ComplaintEndpoints
{
    public sealed record VersionInput(int? ExpectedVersion);
    public sealed record AssignInput(string? AgentId, int? ExpectedVersion);
    public sealed record TextInput(string? Text, string? Note, int? ExpectedVersion);

    private const string NotLinked = "Hesabınız bir temsilci kaydına bağlı değil.";

    public static IEndpointRouteBuilder MapAdminComplaintEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/complaints", async (string? status, string? assigned, int? page, int? pageSize, HttpContext ctx,
            IComplaintManagementPort port, IHumanAgentRegistry agents, IOptionsMonitor<SlaOptions> sla, CancellationToken ct) =>
        {
            var query = new ComplaintQuery(
                Status: string.IsNullOrWhiteSpace(status) || status == "all" ? null : status,
                AssignedAgentId: assigned is null or "unassigned" ? null : assigned,
                UnassignedOnly: assigned == "unassigned",
                Page: page ?? 1, PageSize: pageSize ?? 20);
            return Results.Ok(PageView(await port.ListAsync(query, ct), query, ActorOf(ctx)!, agents, sla.CurrentValue));
        });

        app.MapGet("/complaints/counts", async (IComplaintManagementPort port, CancellationToken ct) =>
            Results.Ok(CountsView(await port.CountsAsync(null, ct))));

        app.MapGet("/complaints/{code}", (string code, HttpContext ctx, IComplaintManagementPort port, IHumanAgentRegistry agents,
            IOptionsMonitor<SlaOptions> sla, CancellationToken ct) => Detail(code, ActorOf(ctx)!, port, agents, sla, ct));

        app.MapPost("/complaints/{code}/assign", (string code, AssignInput? body, HttpContext ctx, IComplaintManagementPort port,
            IHumanAgentRegistry agents, IOptionsMonitor<SlaOptions> sla, CancellationToken ct) =>
            Run(code, ctx, port, agents, sla, new ComplaintCommand(ComplaintAction.Assign, AssigneeAgentId: body?.AgentId,
                ExpectedVersion: body?.ExpectedVersion), ct));

        MapCommonActions(app, "/complaints");
        return app;
    }

    public static IEndpointRouteBuilder MapAgentComplaintEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/agent/complaints", async (string? status, bool? mine, int? page, int? pageSize, HttpContext ctx,
            IComplaintManagementPort port, IHumanAgentRegistry agents, IOptionsMonitor<SlaOptions> sla, CancellationToken ct) =>
        {
            var actor = ActorOf(ctx);
            if (actor is null) return Results.BadRequest(new { error = NotLinked });
            var me = actor.IsAdmin ? null : actor.AgentId;
            var query = new ComplaintQuery(
                Status: string.IsNullOrWhiteSpace(status) || status == "all" ? null : status,
                AssignedAgentId: mine == true ? me : null,
                VisibleToAgentId: me,
                Page: page ?? 1, PageSize: pageSize ?? 20);
            return Results.Ok(PageView(await port.ListAsync(query, ct), query, actor, agents, sla.CurrentValue));
        });

        app.MapGet("/agent/complaints/counts", async (HttpContext ctx, IComplaintManagementPort port, CancellationToken ct) =>
        {
            var actor = ActorOf(ctx);
            if (actor is null) return Results.BadRequest(new { error = NotLinked });
            return Results.Ok(CountsView(await port.CountsAsync(actor.AgentId, ct)));
        });

        app.MapGet("/agent/complaints/{code}", (string code, HttpContext ctx, IComplaintManagementPort port,
            IHumanAgentRegistry agents, IOptionsMonitor<SlaOptions> sla, CancellationToken ct) =>
            ActorOf(ctx) is { } actor ? Detail(code, actor, port, agents, sla, ct) : Task.FromResult(Results.BadRequest(new { error = NotLinked })));

        app.MapPost("/agent/complaints/{code}/claim", (string code, VersionInput? body, HttpContext ctx, IComplaintManagementPort port,
            IHumanAgentRegistry agents, IOptionsMonitor<SlaOptions> sla, CancellationToken ct) =>
            Run(code, ctx, port, agents, sla, new ComplaintCommand(ComplaintAction.Claim, ExpectedVersion: body?.ExpectedVersion), ct));

        MapCommonActions(app, "/agent/complaints");
        return app;
    }

    private static void MapCommonActions(IEndpointRouteBuilder app, string prefix)
    {
        foreach (var (segment, action) in new[]
                 {
                     ("resolve", ComplaintAction.Resolve), ("reject", ComplaintAction.Reject),
                     ("reopen", ComplaintAction.Reopen), ("notes", ComplaintAction.Note)
                 })
        {
            var a = action;
            app.MapPost($"{prefix}/{{code}}/{segment}", (string code, TextInput? body, HttpContext ctx, IComplaintManagementPort port,
                IHumanAgentRegistry agents, IOptionsMonitor<SlaOptions> sla, CancellationToken ct) =>
                Run(code, ctx, port, agents, sla, new ComplaintCommand(a, Text: body?.Text, InternalNote: body?.Note,
                    ExpectedVersion: body?.ExpectedVersion), ct));
        }
    }

    private static async Task<IResult> Run(string code, HttpContext ctx, IComplaintManagementPort port, IHumanAgentRegistry agents,
        IOptionsMonitor<SlaOptions> sla, ComplaintCommand command, CancellationToken ct)
    {
        var actor = ActorOf(ctx);
        if (actor is null) return Results.BadRequest(new { error = NotLinked });
        var r = await port.ExecuteAsync(code, actor, command, ct);
        return r.Status switch
        {
            ComplaintCommandStatus.Updated => Results.Ok(View(code, r.Complaint!, actor, agents, sla.CurrentValue)),
            ComplaintCommandStatus.NotFound => Results.NotFound(new { error = "Şikayet bulunamadı." }),
            ComplaintCommandStatus.Forbidden => Results.Json(new { error = r.Error }, statusCode: StatusCodes.Status403Forbidden),
            ComplaintCommandStatus.Invalid => Results.BadRequest(new { error = r.Error }),
            _ => Results.Conflict(new { error = r.Error, currentStatus = r.Complaint?.Status, version = r.Complaint?.Version })
        };
    }

    private static async Task<IResult> Detail(string code, ComplaintActor actor, IComplaintManagementPort port,
        IHumanAgentRegistry agents, IOptionsMonitor<SlaOptions> sla, CancellationToken ct)
    {
        var d = await port.GetAsync(code, ct);
        if (d is null) return Results.NotFound(new { error = "Şikayet bulunamadı." });
        return Results.Ok(new
        {
            complaint = View(code, d.Complaint, actor, agents, sla.CurrentValue),
            order = d.Order is null ? null : new { status = d.Order.Status, linesSummary = d.Order.LinesSummary() },
            attachmentIds = d.AttachmentIds,
            events = d.Events.Select(e => new
            {
                at = e.At, actor = e.Actor, kind = e.Kind, fromStatus = e.FromStatus, toStatus = e.ToStatus,
                assignedAgentId = e.AssignedAgentId, note = e.Note
            })
        });
    }

    private static object PageView(ComplaintPage page, ComplaintQuery query, ComplaintActor actor, IHumanAgentRegistry agents, SlaOptions sla) => new
    {
        items = page.Items.Select(i => View(i.ComplaintId, i.Complaint, actor, agents, sla)),
        total = page.Total,
        page = Math.Max(query.Page, 1),
        pageSize = Math.Clamp(query.PageSize, 1, 500)
    };

    private static object CountsView(ComplaintCounts c) => new { pending = c.Pending, inProgress = c.InProgress, assignedToMe = c.AssignedToAgent };

    private static object View(string id, ComplaintInfo c, ComplaintActor actor, IHumanAgentRegistry agents, SlaOptions sla) => new
    {
        complaintId = id,
        orderId = c.OrderId,
        customerId = c.CustomerId,
        complaint = c.Complaint,
        status = c.Status,
        assignedAgentId = c.AssignedAgentId,
        assignedAgentName = c.AssignedAgentId is { } a ? agents.Get(a)?.DisplayName : null,
        resolution = c.Resolution,
        createdAt = c.CreatedAt,
        updatedAt = c.UpdatedAt,
        version = c.Version,
        sla = SlaPolicyEvaluator.ComplaintTone(c, sla.Complaints, DateTime.UtcNow),
        actions = ComplaintTransitions.AvailableActions(c, actor).Select(ComplaintTransitions.ActionName)
    };

    /// <summary>Admin rolü → admin aktör; değilse <c>linked_agent_id</c> claim'i; ikisi de yoksa null.</summary>
    private static ComplaintActor? ActorOf(HttpContext ctx)
    {
        var name = ctx.User.FindFirstValue(ClaimTypes.Name) ?? "personel";
        if (ctx.User.IsInRole("Admin")) return new ComplaintActor(ComplaintActorRole.Admin, null, name);
        var agentId = ctx.User.FindFirstValue("linked_agent_id");
        return string.IsNullOrWhiteSpace(agentId) ? null : new ComplaintActor(ComplaintActorRole.Agent, agentId, name);
    }
}
```

`Program.cs` — `adminScope.MapOrderFulfillmentEndpoints();` altına `adminScope.MapAdminComplaintEndpoints();`, `agentScope.MapAgentConversationClosingEndpoints();` altına `agentScope.MapAgentComplaintEndpoints();`.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --project tests/CustomerSupportBot.Api.IntegrationTests -- --filter-class CustomerSupportBot.Api.IntegrationTests.ComplaintEndpointTests`
Expected: PASS. `ClaimTypes.Name` token'da yoksa (`ayse` beklenen aktör adı) `JwtAccessTokenProvider`'ın hangi claim'e kullanıcı adını yazdığına bak (`unique_name`/`name`) ve `ActorOf`'u ona göre düzelt; ledger'a `Ruling:` yaz.

- [ ] **Step 6: Run the API suite**

Run: `dotnet test --project tests/CustomerSupportBot.Api.IntegrationTests`
Expected: yeşil.

- [ ] **Step 7: Commit**

```bash
git add src/CustomerSupportBot.Api src/CustomerSupportBot.Application/Services/Sla tests/CustomerSupportBot.Api.IntegrationTests/ComplaintEndpointTests.cs
git commit -m "feat(complaints): admin and agent endpoints

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: SLA Guardian şikayet kolu

**Files:**
- Modify: `src/CustomerSupportBot.Application/Services/Sla/SlaPolicyEvaluator.cs`
- Modify: `src/CustomerSupportBot.Application/Services/Sla/SlaPortService.cs`
- Modify: `src/CustomerSupportBot.Application/Ports/Inbound/ISlaPort.cs`
- Modify: `src/CustomerSupportBot.Api/appsettings.json` (`Sla`)
- Test: `tests/CustomerSupportBot.Application.Tests/ComplaintSlaTests.cs`

**Interfaces:**
- Consumes: Task 5'te eklenen `ComplaintSlaOptions`, `SlaOptions.Complaints`; Task 2 `IComplaintRepository.GetOpenAsync`.
- Produces:
  - `SlaPolicyEvaluator.KindComplaintPickup = "complaint_pickup"`, `KindComplaintResolve = "complaint_resolve"`
  - `static IReadOnlyList<SlaEvent> EvaluateComplaint(string complaintId, ComplaintInfo c, ComplaintSlaOptions o, ISlaEventSink sink, DateTime now)`
  - `record SlaComplaintStatus(int OpenCount, int PendingCount, int OldestSeconds, int PickupWarnAfterSeconds, int PickupBreachAfterSeconds, int ResolveWarnAfterSeconds, int ResolveBreachAfterSeconds, int BreachCountRecent)`
  - `SlaStatusResult(..., SlaComplaintStatus? Complaints = null)` (yeni son parametre, isteğe bağlı)
  - `SlaPortService(ISlaEventSink, IApprovalQueue, IEscalationSink, IOptionsMonitor<SlaOptions>, IComplaintRepository? complaints = null)`

- [ ] **Step 1: Write the failing test**

`tests/CustomerSupportBot.Application.Tests/ComplaintSlaTests.cs`:

```csharp
// Şikayet SLA: Beklemede'de üstlenme eşiği, açık kalma eşiği; her olay bir kez.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Sla;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Tests;

public class ComplaintSlaTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ComplaintSlaOptions O = new()
    {
        PickupWarnAfterSeconds = 100, PickupBreachAfterSeconds = 200, ResolveWarnAfterSeconds = 1000, ResolveBreachAfterSeconds = 2000
    };

    private static ComplaintInfo C(string status, int ageSeconds) => new()
    {
        OrderId = "1030", CustomerId = "1001", Complaint = "x", Status = status, CreatedAt = Now.AddSeconds(-ageSeconds)
    };

    [Fact]
    public void Pending_PastPickupBreach_EmitsPickupBreach_Once()
    {
        var sink = new InMemorySlaEventSink();

        var first = SlaPolicyEvaluator.EvaluateComplaint("1006", C(WellKnown.ComplaintStatuses.Pending, 250), O, sink, Now);
        first.Should().ContainSingle(e => e.Kind == SlaPolicyEvaluator.KindComplaintPickup && e.Severity == SlaPolicyEvaluator.SeverityBreach);
        foreach (var e in first) sink.RecordAsync(e).GetAwaiter().GetResult();

        SlaPolicyEvaluator.EvaluateComplaint("1006", C(WellKnown.ComplaintStatuses.Pending, 260), O, sink, Now).Should().BeEmpty();
    }

    [Fact]
    public void InProgress_IgnoresPickup_UsesResolveWarn()
    {
        var events = SlaPolicyEvaluator.EvaluateComplaint("1006", C(WellKnown.ComplaintStatuses.InProgress, 1500), O, new InMemorySlaEventSink(), Now);

        events.Should().ContainSingle().Which.Should().Match<SlaEvent>(e =>
            e.Kind == SlaPolicyEvaluator.KindComplaintResolve && e.Severity == SlaPolicyEvaluator.SeverityWarn && e.TargetId == "1006");
    }

    [Fact]
    public void Young_Or_Legacy_NoEvents()
    {
        SlaPolicyEvaluator.EvaluateComplaint("1", C(WellKnown.ComplaintStatuses.Pending, 10), O, new InMemorySlaEventSink(), Now).Should().BeEmpty();
        SlaPolicyEvaluator.EvaluateComplaint("2", new ComplaintInfo { Status = WellKnown.ComplaintStatuses.Pending }, O, new InMemorySlaEventSink(), Now)
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData(WellKnown.ComplaintStatuses.Pending, 150, "warn")]
    [InlineData(WellKnown.ComplaintStatuses.Pending, 250, "breach")]
    [InlineData(WellKnown.ComplaintStatuses.InProgress, 250, "ok")]
    [InlineData(WellKnown.ComplaintStatuses.InProgress, 2500, "breach")]
    [InlineData(WellKnown.ComplaintStatuses.Resolved, 9999, "none")]
    public void Tone(string status, int age, string expected)
        => SlaPolicyEvaluator.ComplaintTone(C(status, age), O, Now).Should().Be(expected);

    [Fact]
    public async Task Scan_RecordsComplaintEvents_AndStatusReportsComplaints()
    {
        var sink = new InMemorySlaEventSink();
        var repo = Substitute.For<IComplaintRepository>();
        repo.GetOpenAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
        [
            new ComplaintListItem("1006", C(WellKnown.ComplaintStatuses.Pending, 3 * 86400)),
            new ComplaintListItem("1007", C(WellKnown.ComplaintStatuses.InProgress, 60))
        ]);
        var approvals = Substitute.For<IApprovalQueue>();
        approvals.GetPendingAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ApprovalRequest>());
        var escalations = Substitute.For<IEscalationSink>();
        escalations.GetOpen().Returns(Array.Empty<EscalationRequest>());
        var opts = new SlaOptions();
        var svc = new SlaPortService(sink, approvals, escalations, new FixedOptionsMonitor<SlaOptions>(opts), repo);

        await svc.ScanOnceAsync(opts, TestContext.Current.CancellationToken);

        sink.GetRecent(50).Should().Contain(e => e.TargetId == "1006" && e.Kind == SlaPolicyEvaluator.KindComplaintPickup);
        var status = await svc.GetStatusAsync(TestContext.Current.CancellationToken);
        status.Complaints!.OpenCount.Should().Be(2);
        status.Complaints.PendingCount.Should().Be(1);
        status.Complaints.OldestSeconds.Should().BeGreaterThanOrEqualTo(3 * 86400 - 5);
    }
}

file sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
```

Dosyanın `using` bloğuna `using Microsoft.Extensions.Options;` ekle. `IEscalationSink.GetOpen()` dönüş türü dizi kabul etmiyorsa (`List<EscalationRequest>` ise) `new List<EscalationRequest>()` döndür.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/CustomerSupportBot.Application.Tests -- --filter-class CustomerSupportBot.Application.Tests.ComplaintSlaTests`
Expected: derleme hatası (`EvaluateComplaint`, `SlaComplaintStatus` yok).

- [ ] **Step 3: Implement**

`SlaPolicyEvaluator.cs`:

```csharp
    public const string KindComplaintPickup = "complaint_pickup";
    public const string KindComplaintResolve = "complaint_resolve";

    /// <summary>
    /// Açık şikayet için uyarı/ihlal olayları. Üstlenme eşikleri yalnız Beklemede'de; açık kalma eşikleri her
    /// açık durumda. Her (tür, şikayet, önem) için bir kez (<see cref="ISlaEventSink.LastEmittedAt"/>). Yeniden
    /// açılan şikayet için aynı tür olay tekrar yazılmaz (spec'te kabul edilen sınır).
    /// </summary>
    public static IReadOnlyList<SlaEvent> EvaluateComplaint(string complaintId, ComplaintInfo c, ComplaintSlaOptions o,
        ISlaEventSink sink, DateTime now)
    {
        var events = new List<SlaEvent>();
        if (!WellKnown.ComplaintStatuses.IsOpen(c.Status) || c.CreatedAt == default) return events;
        var age = (int)Math.Floor((now - c.CreatedAt).TotalSeconds);

        if (c.Status == WellKnown.ComplaintStatuses.Pending)
            Add(KindComplaintPickup, o.PickupWarnAfterSeconds, o.PickupBreachAfterSeconds, "Üstlenilmedi");
        Add(KindComplaintResolve, o.ResolveWarnAfterSeconds, o.ResolveBreachAfterSeconds, "Açık");
        return events;

        void Add(string kind, int warn, int breach, string what)
        {
            var severity = age >= breach ? SeverityBreach : age >= warn ? SeverityWarn : null;
            if (severity is null || sink.LastEmittedAt(kind, complaintId, severity) is not null) return;
            events.Add(new SlaEvent
            {
                Kind = kind, Severity = severity, TargetId = complaintId, AgeSeconds = age,
                Note = $"{what} {age}s — eşik {(severity == SeverityBreach ? breach : warn)}s"
            });
        }
    }
```

`ISlaPort.cs`:

```csharp
public sealed record SlaComplaintStatus(
    int OpenCount,
    int PendingCount,
    int OldestSeconds,
    int PickupWarnAfterSeconds,
    int PickupBreachAfterSeconds,
    int ResolveWarnAfterSeconds,
    int ResolveBreachAfterSeconds,
    int BreachCountRecent);
```

ve `SlaStatusResult`'a son parametre: `SlaComplaintStatus? Complaints = null`.

`SlaPortService.cs`:
- Kurucuya `IComplaintRepository? complaints = null` (son parametre) ve `_complaints` alanı.
- `ScanOnceAsync` sonuna:

```csharp
        if (_complaints is not null)
        {
            foreach (var item in await _complaints.GetOpenAsync(500, ct))
                foreach (var evt in SlaPolicyEvaluator.EvaluateComplaint(item.ComplaintId, item.Complaint, opts.Complaints, _events, now))
                    await _events.RecordAsync(evt);
        }
```

- `GetStatusAsync`'te `return` öncesi:

```csharp
        SlaComplaintStatus? complaintStatus = null;
        if (_complaints is not null)
        {
            var openComplaints = await _complaints.GetOpenAsync(5000, ct);
            var ages = openComplaints.Where(i => i.Complaint.CreatedAt != default)
                .Select(i => (int)Math.Floor((now - i.Complaint.CreatedAt).TotalSeconds)).ToList();
            complaintStatus = new SlaComplaintStatus(
                openComplaints.Count,
                openComplaints.Count(i => i.Complaint.Status == WellKnown.ComplaintStatuses.Pending),
                ages.Count > 0 ? ages.Max() : 0,
                opts.Complaints.PickupWarnAfterSeconds, opts.Complaints.PickupBreachAfterSeconds,
                opts.Complaints.ResolveWarnAfterSeconds, opts.Complaints.ResolveBreachAfterSeconds,
                recent.Count(e => e.Kind is SlaPolicyEvaluator.KindComplaintPickup or SlaPolicyEvaluator.KindComplaintResolve
                               && e.Severity == SlaPolicyEvaluator.SeverityBreach));
        }
```

ve `new SlaStatusResult(...)` çağrısına son argüman olarak `complaintStatus` ekle.

`appsettings.json` → `Sla` bloğuna `Escalations`'tan sonra:

```json
    "Complaints": {
      "PickupWarnAfterSeconds": 14400,
      "PickupBreachAfterSeconds": 86400,
      "ResolveWarnAfterSeconds": 172800,
      "ResolveBreachAfterSeconds": 259200
    }
```

- [ ] **Step 4: Run tests**

Run: `dotnet test --project tests/CustomerSupportBot.Application.Tests -- --filter-class CustomerSupportBot.Application.Tests.ComplaintSlaTests && dotnet test --project tests/CustomerSupportBot.Api.IntegrationTests -- --filter-class CustomerSupportBot.Api.IntegrationTests.SlaGuardianServiceTests`
Expected: PASS (eski SLA testleri dört argümanlı kurucuyla derlenmeye devam eder).

- [ ] **Step 5: Commit**

```bash
git add src/CustomerSupportBot.Application src/CustomerSupportBot.Api/appsettings.json tests/CustomerSupportBot.Application.Tests/ComplaintSlaTests.cs
git commit -m "feat(sla): complaint pickup and resolution thresholds

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: KVKK dışa aktarma

**Files:**
- Modify: `src/CustomerSupportBot.Application/Ports/Inbound/IDataPrivacyPort.cs:68`
- Modify: `src/CustomerSupportBot.Application/Services/Privacy/DataPrivacyService.cs:150-152`
- Test: `tests/CustomerSupportBot.Application.Tests/DataPrivacyServiceTests.cs` (yeni test, mevcut `Build()` kurulumuyla)

**Interfaces:**
- Consumes: Task 2 `GetEventsAsync`; Task 1 alanları.
- Produces: `record ExportedComplaintStep(DateTime At, string? FromStatus, string? ToStatus)`; `ExportedComplaint(string ComplaintId, string OrderId, string Complaint, string Status, string? Resolution = null, DateTime? CreatedAt = null, DateTime? UpdatedAt = null, IReadOnlyList<ExportedComplaintStep>? History = null)`.

- [ ] **Step 1: Write the failing test**

`DataPrivacyServiceTests.cs` sınıfına (özel `Build()` / `Harness` kurulumunu kullanarak) yeni test ekle. `Build()` içindeki sahte şikayet deposuna bir varsayılan da ekle: `complaints.GetEventsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<ComplaintEvent>());`

```csharp
    // KVKK dışa aktarma: şikayet açıklaması ve durum geçmişi var; iç not ve personel adı yok.
    [Fact]
    public async Task Export_IncludesComplaintResolutionAndStatusSteps_NotNotesOrActors()
    {
        var h = Build();
        h.Complaints.GetByCustomer("1001").Returns(
        [
            ("1006", new ComplaintInfo
            {
                OrderId = "1030", CustomerId = "1001", Complaint = "kırık", Status = WellKnown.ComplaintStatuses.Resolved,
                Resolution = "Yeni ürün gönderildi.", CreatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc)
            })
        ]);
        h.Complaints.GetEventsAsync("1006", Arg.Any<CancellationToken>()).Returns(
        [
            new ComplaintEvent(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), "system", ComplaintEventKinds.Created, null, "Beklemede", null, null),
            new ComplaintEvent(new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc), "ayse", ComplaintEventKinds.Note, null, null, null, "GİZLİ NOT"),
            new ComplaintEvent(new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), "ayse", ComplaintEventKinds.Resolved, "İncelemede", "Çözüldü", "a1", "iç")
        ]);

        var export = await h.Service.ExportCustomerDataAsync("1001", TestContext.Current.CancellationToken);

        var c = export.Complaints.Should().ContainSingle().Subject;
        c.Resolution.Should().Be("Yeni ürün gönderildi.");
        c.History!.Select(s => s.ToStatus).Should().Equal("Beklemede", "Çözüldü");
        System.Text.Json.JsonSerializer.Serialize(export).Should().NotContain("GİZLİ NOT").And.NotContain("ayse").And.NotContain("\"a1\"");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/CustomerSupportBot.Application.Tests -- --filter-class CustomerSupportBot.Application.Tests.DataPrivacyServiceTests`
Expected: derleme hatası (`Resolution`, `History` yok).

- [ ] **Step 3: Implement**

`IDataPrivacyPort.cs` satır 68:

```csharp
public sealed record ExportedComplaintStep(DateTime At, string? FromStatus, string? ToStatus);

/// <summary>Müşteriye görünen alanlar + durum adımları. İç notlar ve personel kimlikleri bilerek yok.</summary>
public sealed record ExportedComplaint(
    string ComplaintId, string OrderId, string Complaint, string Status,
    string? Resolution = null, DateTime? CreatedAt = null, DateTime? UpdatedAt = null,
    IReadOnlyList<ExportedComplaintStep>? History = null);
```

`DataPrivacyService.cs` satır 150-152:

```csharp
        var complaints = new List<ExportedComplaint>();
        foreach (var (complaintId, c) in _complaints.GetByCustomer(customerId))
        {
            var steps = (await _complaints.GetEventsAsync(complaintId, ct))
                .Where(e => e.ToStatus is not null && e.Kind != ComplaintEventKinds.Note)
                .Select(e => new ExportedComplaintStep(e.At, e.FromStatus, e.ToStatus))
                .ToList();
            complaints.Add(new ExportedComplaint(complaintId, c.OrderId, c.Complaint, c.Status, c.Resolution,
                c.CreatedAt == default ? null : c.CreatedAt, c.UpdatedAt == default ? null : c.UpdatedAt, steps));
        }
```

(`DataPrivacyDispositionTests` da `IComplaintRepository` sahtesi kuruyorsa aynı `GetEventsAsync` varsayılanını orada da ekle.)

- [ ] **Step 4: Run tests**

Run: `dotnet test --project tests/CustomerSupportBot.Application.Tests -- --filter-class CustomerSupportBot.Application.Tests.DataPrivacyServiceTests --filter-class CustomerSupportBot.Application.Tests.DataPrivacyDispositionTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CustomerSupportBot.Application tests/CustomerSupportBot.Application.Tests
git commit -m "feat(privacy): export complaint resolution and status history

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Panel — Şikayetler sekmesi, rozet ve SLA kartı

**Files:**
- Modify: `src/CustomerSupportBot.Web/Models/AdminModels.cs` (Şikayet modelleri + `SlaStatus.Complaints`)
- Create: `src/CustomerSupportBot.Web/Helpers/ComplaintView.cs`
- Modify: `src/CustomerSupportBot.Web/Services/AdminApiService.cs`
- Modify: `src/CustomerSupportBot.Web/Services/StaffBadgeState.cs`
- Modify: `src/CustomerSupportBot.Web/Helpers/AdminTabs.cs`
- Modify: `src/CustomerSupportBot.Web/Layout/StaffSidebar.razor`
- Create: `src/CustomerSupportBot.Web/Components/ComplaintsPanel.razor`
- Modify: `src/CustomerSupportBot.Web/Pages/Admin.razor`, `Pages/Admin.razor.cs`
- Modify: `src/CustomerSupportBot.Web/Pages/Sla.razor`
- Modify: `src/CustomerSupportBot.Web/wwwroot/css/admin.css`
- Test: `tests/CustomerSupportBot.Web.Tests/ComplaintViewTests.cs`, `tests/CustomerSupportBot.Web.Tests/AdminTabsTests.cs`

**Interfaces:**
- Consumes: Task 5 JSON sözleşmesi; Task 6 `SlaStatusResult.Complaints`.
- Produces (Web):

```csharp
public sealed record ComplaintItem(string ComplaintId, string OrderId, string CustomerId, string Complaint, string Status,
    string? AssignedAgentId, string? AssignedAgentName, string? Resolution, DateTime CreatedAt, DateTime UpdatedAt,
    int Version, string Sla, List<string> Actions);
public sealed record ComplaintPageResponse(List<ComplaintItem> Items, int Total, int Page, int PageSize);
public sealed record ComplaintCountsResponse(int Pending, int InProgress, int AssignedToMe);
public sealed record ComplaintEventItem(DateTime At, string Actor, string Kind, string? FromStatus, string? ToStatus, string? AssignedAgentId, string? Note);
public sealed record ComplaintOrderSummary(string Status, string LinesSummary);
public sealed record ComplaintDetailResponse(ComplaintItem Complaint, ComplaintOrderSummary? Order, List<string> AttachmentIds, List<ComplaintEventItem> Events);
public sealed record SlaComplaintStats(int OpenCount, int PendingCount, double? OldestSeconds, double PickupWarnAfterSeconds,
    double PickupBreachAfterSeconds, double ResolveWarnAfterSeconds, double ResolveBreachAfterSeconds, int BreachCountRecent);
// SlaStatus'a son parametre: SlaComplaintStats? Complaints = null
// AdminApiService
Task<ComplaintPageResponse?> GetComplaintsAsync(string? status, bool mine, int page);
Task<ComplaintCountsResponse?> GetComplaintCountsAsync();
Task<ComplaintDetailResponse?> GetComplaintAsync(string id);
Task<(ComplaintItem? Complaint, string? Error)> ComplaintActionAsync(string id, string action, object body);
// StaffBadgeState
int? OpenComplaints; void SetComplaints(int count);
// ComplaintView
static string ToneClass(string sla); static string KindLabel(string kind); static string ActionLabel(string action); static string Age(DateTime createdAtUtc, DateTime nowUtc); static int BadgeCount(ComplaintCountsResponse c, bool isAgent);
```

- [ ] **Step 1: Write the failing tests**

`tests/CustomerSupportBot.Web.Tests/ComplaintViewTests.cs`:

```csharp
// Şikayet paneli görüntü yardımcıları: SLA rengi, etiketler, yaş, rozet sayısı.

using CustomerSupportBot.Web.Helpers;
using CustomerSupportBot.Web.Models;

namespace CustomerSupportBot.Web.Tests;

public class ComplaintViewTests
{
    [Theory]
    [InlineData("breach", "sla-breach")]
    [InlineData("warn", "sla-warn")]
    [InlineData("ok", "sla-ok")]
    [InlineData("none", "")]
    [InlineData("??", "")]
    public void ToneClass(string sla, string expected) => ComplaintView.ToneClass(sla).Should().Be(expected);

    [Theory]
    [InlineData("claim", "Üstlen")]
    [InlineData("assign", "Ata")]
    [InlineData("resolve", "Çözüldü")]
    [InlineData("reject", "Reddet")]
    [InlineData("reopen", "Yeniden aç")]
    [InlineData("note", "Not ekle")]
    public void ActionLabel(string action, string expected) => ComplaintView.ActionLabel(action).Should().Be(expected);

    [Theory]
    [InlineData("created", "Oluşturuldu")]
    [InlineData("claimed", "Üstlenildi")]
    [InlineData("assigned", "Atandı")]
    [InlineData("resolved", "Çözüldü")]
    [InlineData("rejected", "Reddedildi")]
    [InlineData("reopened", "Yeniden açıldı")]
    [InlineData("note", "Not")]
    public void KindLabel(string kind, string expected) => ComplaintView.KindLabel(kind).Should().Be(expected);

    [Fact]
    public void Age_ReadsInMinutesHoursDays()
    {
        var now = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
        ComplaintView.Age(now.AddMinutes(-5), now).Should().Be("5 dk");
        ComplaintView.Age(now.AddHours(-3), now).Should().Be("3 sa");
        ComplaintView.Age(now.AddDays(-2), now).Should().Be("2 gün");
    }

    [Fact]
    public void BadgeCount_AdminPending_AgentPendingPlusMine()
    {
        var c = new ComplaintCountsResponse(Pending: 4, InProgress: 7, AssignedToMe: 2);
        ComplaintView.BadgeCount(c, isAgent: false).Should().Be(4);
        ComplaintView.BadgeCount(c, isAgent: true).Should().Be(6);
    }
}
```

`AdminTabsTests.cs` — dosyaya:

```csharp
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Complaints_IsOpenForAdminAndAgent(bool isAgent)
        => AdminTabs.Normalize("complaints", isAgent).Should().Be("complaints");
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --project tests/CustomerSupportBot.Web.Tests -- --filter-class CustomerSupportBot.Web.Tests.ComplaintViewTests --filter-class CustomerSupportBot.Web.Tests.AdminTabsTests`
Expected: derleme hatası (`ComplaintView`, `ComplaintCountsResponse` yok).

- [ ] **Step 3: Modeller, yardımcı, sekme listesi, rozet durumu**

`AdminModels.cs` — "Produces" bloğundaki Web kayıtlarını `// ─── SLA` bölümünden önce bir `// ─── Şikayetler` başlığıyla ekle; `SlaStatus`'a son parametre `SlaComplaintStats? Complaints = null`, `SlaComplaintStats` kaydını `SlaEscalationStats`'ın altına ekle.

`src/CustomerSupportBot.Web/Helpers/ComplaintView.cs`:

```csharp
using CustomerSupportBot.Web.Models;

namespace CustomerSupportBot.Web.Helpers;

/// <summary>Şikayet paneli için saf görüntü yardımcıları. Hangi düğmenin görüneceğine sunucu (<c>actions</c>) karar verir.</summary>
public static class ComplaintView
{
    public static readonly IReadOnlyList<(string Value, string Label)> StatusFilters =
    [
        ("open", "Açık"), ("Beklemede", "Beklemede"), ("İncelemede", "İncelemede"),
        ("Çözüldü", "Çözüldü"), ("Reddedildi", "Reddedildi"), ("all", "Tümü")
    ];

    public static string ToneClass(string sla) => sla switch
    {
        "breach" => "sla-breach",
        "warn" => "sla-warn",
        "ok" => "sla-ok",
        _ => ""
    };

    public static string ActionLabel(string action) => action switch
    {
        "claim" => "Üstlen",
        "assign" => "Ata",
        "resolve" => "Çözüldü",
        "reject" => "Reddet",
        "reopen" => "Yeniden aç",
        "note" => "Not ekle",
        _ => action
    };

    public static string KindLabel(string kind) => kind switch
    {
        "created" => "Oluşturuldu",
        "claimed" => "Üstlenildi",
        "assigned" => "Atandı",
        "resolved" => "Çözüldü",
        "rejected" => "Reddedildi",
        "reopened" => "Yeniden açıldı",
        "note" => "Not",
        _ => kind
    };

    public static string Age(DateTime createdAtUtc, DateTime nowUtc)
    {
        var span = nowUtc - createdAtUtc;
        if (span.TotalHours < 1) return $"{Math.Max(0, (int)span.TotalMinutes)} dk";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} sa";
        return $"{(int)span.TotalDays} gün";
    }

    /// <summary>Admin: üstlenilmeyi bekleyenler. Temsilci: bekleyenler + kendisine atanan açıklar.</summary>
    public static int BadgeCount(ComplaintCountsResponse c, bool isAgent) => isAgent ? c.Pending + c.AssignedToMe : c.Pending;

    /// <summary>Metin isteyen eylemler ve alan etiketi.</summary>
    public static string? TextPrompt(string action) => action switch
    {
        "resolve" or "reject" => "Müşteriye gönderilecek açıklama",
        "reopen" => "Yeniden açma gerekçesi (iç not)",
        "note" => "İç not (müşteriye gitmez)",
        _ => null
    };
}
```

`AdminTabs.cs`: `All` listesine `"complaints"` (`"chats"`'tan sonra), `AgentAllowed`'a `"complaints"` ekle.

`StaffBadgeState.cs`:

```csharp
    public int? OpenComplaints { get; private set; }

    public void SetComplaints(int count)
    {
        if (OpenComplaints == count) return;
        OpenComplaints = count;
        OnChange?.Invoke();
    }
```

`StaffSidebar.razor`: `Chats`'tan sonra

```csharp
    private static readonly NavItem Complaints = new("complaints", "Şikayetler", "/admin?tab=complaints", "alert", Tab: "complaints",
        Badge: "complaints", BadgeTone: "warn", BadgeNoun: "bekleyen", AgentVisible: true);
```

`Groups` → `new("İş kuyruğu", [Approvals, Escalations, Chats, Complaints])`; `BadgeFor`'a `"complaints" => Badges.OpenComplaints,`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/CustomerSupportBot.Web.Tests -- --filter-class CustomerSupportBot.Web.Tests.ComplaintViewTests --filter-class CustomerSupportBot.Web.Tests.AdminTabsTests`
Expected: PASS. `AdminTabsTests`'te temsilcinin yalnızca iki sekmeyi açabildiğini sayan bir test varsa beklenen listeyi `complaints` ile güncelle.

- [ ] **Step 5: API istemcisi**

`AdminApiService.cs` — "Siparişler" bölümünden sonra:

```csharp
    // ─── Şikayetler (admin: /complaints, temsilci: /agent/complaints) ───────

    public async Task<ComplaintPageResponse?> GetComplaintsAsync(string? status, bool mine, int page)
    {
        try
        {
            var prefix = await PrefixAsync();
            var url = $"{prefix}/complaints?status={Uri.EscapeDataString(status ?? "open")}&page={page}&pageSize=20"
                      + (mine && prefix == "/agent" ? "&mine=true" : "");
            return await http.GetFromJsonAsync<ComplaintPageResponse>(url);
        }
        catch { return null; }
    }

    public async Task<ComplaintCountsResponse?> GetComplaintCountsAsync()
    {
        try { return await http.GetFromJsonAsync<ComplaintCountsResponse>($"{await PrefixAsync()}/complaints/counts"); }
        catch { return null; }
    }

    public async Task<ComplaintDetailResponse?> GetComplaintAsync(string id)
    {
        try
        {
            using var response = await http.GetAsync($"{await PrefixAsync()}/complaints/{Uri.EscapeDataString(id)}");
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<ComplaintDetailResponse>() : null;
        }
        catch { return null; }
    }

    /// <param name="action">claim | assign | resolve | reject | reopen | notes</param>
    public async Task<(ComplaintItem? Complaint, string? Error)> ComplaintActionAsync(string id, string action, object body)
    {
        try
        {
            using var response = await http.PostAsJsonAsync($"{await PrefixAsync()}/complaints/{Uri.EscapeDataString(id)}/{action}", body);
            if (response.IsSuccessStatusCode) return (await response.Content.ReadFromJsonAsync<ComplaintItem>(), null);
            var error = await response.Content.ReadFromJsonAsync<ComplaintActionError>();
            var message = error?.Error ?? $"İşlem yapılamadı ({(int)response.StatusCode}).";
            return (null, response.StatusCode == System.Net.HttpStatusCode.Conflict
                ? $"{message} Kayıt yenilendi; lütfen tekrar deneyin."
                : message);
        }
        catch (Exception ex)
        {
            return (null, "İşlem yapılamadı: " + ex.Message);
        }
    }

    private sealed record ComplaintActionError(string? Error, string? CurrentStatus, int? Version);
```

- [ ] **Step 6: Panel bileşeni**

`src/CustomerSupportBot.Web/Components/ComplaintsPanel.razor`:

```razor
@* Şikayetler sekmesi: liste (filtre + sayfalama) ve seçili şikayetin ayrıntısı. Düğmeleri sunucu (actions) belirler. *@
@using CustomerSupportBot.Web.Helpers
@using CustomerSupportBot.Web.Models
@inject AdminApiService AdminApi

<div class="complaints-layout">
    <section class="card complaints-list" aria-label="Şikayet listesi">
        <div class="complaints-filters">
            <label>Durum
                <select @bind="_status" @bind:after="ReloadAsync">
                    @foreach (var (value, label) in ComplaintView.StatusFilters)
                    {
                        <option value="@value">@label</option>
                    }
                </select>
            </label>
            @if (IsAgent)
            {
                <label class="inline-check"><input type="checkbox" @bind="_mine" @bind:after="ReloadAsync" /> Yalnız bana atananlar</label>
            }
        </div>

        @if (_page is null)
        {
            <p class="muted">Yükleniyor…</p>
        }
        else if (_page.Items.Count == 0)
        {
            <p class="muted">Bu filtrede şikayet yok.</p>
        }
        else
        {
            <ul class="complaint-rows">
                @foreach (var c in _page.Items)
                {
                    var id = c.ComplaintId;
                    <li>
                        <button type="button" class="complaint-row @(id == _selectedId ? "selected" : "") @ComplaintView.ToneClass(c.Sla)"
                                @onclick="() => SelectAsync(id)">
                            <span class="mono">#@c.ComplaintId</span>
                            <span>Sipariş #@c.OrderId</span>
                            <span class="status-tag">@c.Status</span>
                            <span class="muted small">@ComplaintView.Age(c.CreatedAt, DateTime.UtcNow) · @(c.AssignedAgentName ?? "atanmamış")</span>
                        </button>
                    </li>
                }
            </ul>
            <div class="complaints-pager">
                <button type="button" class="btn-secondary btn-sm" disabled="@(_pageNo <= 1)" @onclick="() => GoAsync(-1)">Önceki</button>
                <span class="muted small">@_pageNo / @Math.Max(1, (int)Math.Ceiling(_page.Total / 20.0)) · @_page.Total kayıt</span>
                <button type="button" class="btn-secondary btn-sm" disabled="@(_pageNo * 20 >= _page.Total)" @onclick="() => GoAsync(1)">Sonraki</button>
            </div>
        }
    </section>

    <section class="card complaint-detail" aria-label="Şikayet ayrıntısı" aria-busy="@_busy">
        @if (_detail is null)
        {
            <p class="muted">Ayrıntısını görmek için bir şikayet seçin.</p>
        }
        else
        {
            var c = _detail.Complaint;
            <header class="order-card-header">
                <h3>Şikayet #@c.ComplaintId</h3>
                <span class="status-tag">@c.Status</span>
            </header>
            <dl class="order-facts">
                <dt>Müşteri</dt><dd>@c.CustomerId</dd>
                <dt>Sipariş</dt><dd>#@c.OrderId @(_detail.Order is { } o ? $"· {o.LinesSummary} · {o.Status}" : "")</dd>
                <dt>Atanan</dt><dd>@(c.AssignedAgentName ?? c.AssignedAgentId ?? "—")</dd>
                <dt>Açıldı</dt><dd>@c.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm") (@ComplaintView.Age(c.CreatedAt, DateTime.UtcNow))</dd>
            </dl>
            <p class="complaint-text">@c.Complaint</p>
            @if (!string.IsNullOrWhiteSpace(c.Resolution))
            {
                <p class="complaint-resolution"><strong>Müşteriye gönderilen açıklama:</strong> @c.Resolution</p>
            }
            @if (_detail.AttachmentIds.Count > 0)
            {
                <ApprovalPhotos Ids="_detail.AttachmentIds" />
            }

            @if (_error is not null)
            {
                <p class="form-error" role="alert">@_error</p>
            }

            @if (c.Actions.Count > 0)
            {
                <div class="complaint-actions">
                    @if (c.Actions.Contains("assign"))
                    {
                        <label>Temsilci
                            <select @bind="_assignee">
                                <option value="">Seçin…</option>
                                @foreach (var a in _agents.Where(a => a.IsActive))
                                {
                                    <option value="@a.Id">@a.DisplayName</option>
                                }
                            </select>
                        </label>
                    }
                    @if (c.Actions.Any(a => ComplaintView.TextPrompt(a) is not null))
                    {
                        <label class="complaint-text-input">@(ComplaintView.TextPrompt(_textFor) ?? "Metin")
                            <textarea maxlength="2000" rows="3" @bind="_text" @bind:event="oninput"></textarea>
                        </label>
                        @if (_textFor is "resolve" or "reject")
                        {
                            <label class="complaint-text-input">İç not (isteğe bağlı)
                                <textarea maxlength="2000" rows="2" @bind="_note"></textarea>
                            </label>
                        }
                    }
                    <div class="order-actions">
                        @foreach (var action in c.Actions)
                        {
                            var act = action;
                            <button type="button" class="@(act is "resolve" or "claim" ? "btn-primary" : "btn-secondary")"
                                    disabled="@(_busy || !CanSubmit(act))"
                                    @onmouseover="() => _textFor = act" @onfocus="() => _textFor = act"
                                    @onclick="() => RunAsync(act)">@ComplaintView.ActionLabel(act)</button>
                        }
                    </div>
                </div>
            }

            <h4>Zaman çizelgesi</h4>
            @if (_detail.Events.Count == 0)
            {
                <p class="muted small">Geçmiş kaydı yok (bu şikayet zaman çizelgesi tutulmaya başlamadan önce açılmış).</p>
            }
            else
            {
                <ol class="complaint-timeline">
                    @foreach (var e in _detail.Events)
                    {
                        <li>
                            <span class="mono small">@e.At.ToLocalTime().ToString("dd.MM HH:mm")</span>
                            <strong>@ComplaintView.KindLabel(e.Kind)</strong>
                            <span class="muted small">@e.Actor</span>
                            @if (!string.IsNullOrWhiteSpace(e.Note)) { <div class="small">@e.Note</div> }
                        </li>
                    }
                </ol>
            }
        }
    </section>
</div>

@code {
    [Parameter] public bool IsAgent { get; set; }
    [Parameter] public EventCallback OnChanged { get; set; }

    private string _status = "open";
    private bool _mine;
    private int _pageNo = 1;
    private ComplaintPageResponse? _page;
    private string? _selectedId;
    private ComplaintDetailResponse? _detail;
    private List<AgentInfo> _agents = [];
    private string _assignee = "";
    private string _text = "";
    private string _note = "";
    private string _textFor = "resolve";
    private string? _error;
    private bool _busy;

    protected override async Task OnInitializedAsync()
    {
        if (!IsAgent) _agents = await AdminApi.GetAgentsAsync();
        await ReloadAsync();
    }

    /// <summary>Admin sayfasının otomatik yenilemesi çağırır: liste ve açık ayrıntı tazelenir, yazılan metin korunur.</summary>
    public async Task RefreshAsync()
    {
        _page = await AdminApi.GetComplaintsAsync(_status, _mine, _pageNo);
        if (_selectedId is not null && !_busy) _detail = await AdminApi.GetComplaintAsync(_selectedId);
        StateHasChanged();
    }

    private async Task ReloadAsync()
    {
        _pageNo = 1;
        _page = await AdminApi.GetComplaintsAsync(_status, _mine, _pageNo);
    }

    private async Task GoAsync(int delta)
    {
        _pageNo = Math.Max(1, _pageNo + delta);
        _page = await AdminApi.GetComplaintsAsync(_status, _mine, _pageNo);
    }

    private async Task SelectAsync(string id)
    {
        _selectedId = id;
        _error = null;
        _text = _note = _assignee = "";
        _detail = await AdminApi.GetComplaintAsync(id);
    }

    private bool CanSubmit(string action) => action switch
    {
        "assign" => !string.IsNullOrWhiteSpace(_assignee),
        "resolve" or "reject" or "reopen" or "note" => !string.IsNullOrWhiteSpace(_text),
        _ => true
    };

    private async Task RunAsync(string action)
    {
        if (_detail is null) return;
        _textFor = action;
        _busy = true;
        _error = null;
        var c = _detail.Complaint;
        object body = action switch
        {
            "assign" => new { agentId = _assignee, expectedVersion = c.Version },
            "resolve" or "reject" => new { text = _text, note = string.IsNullOrWhiteSpace(_note) ? null : _note, expectedVersion = c.Version },
            "reopen" => new { text = _text, expectedVersion = c.Version },
            "note" => new { text = _text },
            _ => new { expectedVersion = c.Version }
        };
        var (_, error) = await AdminApi.ComplaintActionAsync(c.ComplaintId, action == "note" ? "notes" : action, body);
        _busy = false;
        _error = error;
        if (error is null) _text = _note = _assignee = "";
        _detail = await AdminApi.GetComplaintAsync(c.ComplaintId);
        _page = await AdminApi.GetComplaintsAsync(_status, _mine, _pageNo);
        await OnChanged.InvokeAsync();
    }
}
```

- [ ] **Step 7: Admin sayfası entegrasyonu**

`Admin.razor` — sipariş sekmesinin (`panel-orders`) `@if (!_isAgent) { ... }` bloğundan **önce** (temsilci de görür):

```razor
        <!-- TAB: ŞİKAYETLER (yönetici + temsilci) -->
        <section id="panel-complaints" class="tab-content @ActiveCls("complaints")" role="tabpanel" aria-labelledby="tab-complaints">
            @if (_activeTab == "complaints")
            {
                <ComplaintsPanel @ref="_complaintsPanel" IsAgent="_isAgent" OnChanged="RefreshBadgesAsync" />
            }
        </section>
```

`Admin.razor.cs`:
- Alan: `private ComplaintsPanel? _complaintsPanel;` ve `private ComplaintCountsResponse? _complaintCounts;`
- `RefreshBadgesAsync` içinde:

```csharp
            var approvals   = AdminApi.GetPendingApprovalsAsync();
            var escalations = AdminApi.GetOpenEscalationsAsync();
            var chats       = AdminApi.GetActiveChatsAsync();
            var complaints  = AdminApi.GetComplaintCountsAsync();
            await Task.WhenAll(approvals, escalations, chats, complaints);
            _pendingApprovals = approvals.Result.Where(a => !_decidedApprovals.Contains(a.Id)).ToList();
            _openEscalations  = escalations.Result;
            _activeChats      = chats.Result;
            _complaintCounts  = complaints.Result ?? _complaintCounts;
            PublishBadges();
```

- `PublishBadges`:

```csharp
    private void PublishBadges()
    {
        Badges.SetQueues(_pendingApprovals.Count, _openEscalations.Count, _activeChats.Count);
        if (_complaintCounts is { } cc) Badges.SetComplaints(ComplaintView.BadgeCount(cc, _isAgent));
    }
```

- `RefreshActiveTabAsync` switch'ine:

```csharp
                case "complaints":
                    if (_complaintsPanel is not null) await _complaintsPanel.RefreshAsync();
                    break;
```

- `SectionTitle`: `"complaints" => "Şikayetler",`; `SectionDescription`: `"complaints" => _isAgent ? "Üstlenmeyi bekleyen ve size atanan şikayetler. Kapatırken müşteriye gidecek açıklamayı yazın." : "Müşteri şikayetleri: atayın, izleyin; çözüldüğünde ya da reddedildiğinde müşteriye e-posta gider.",`
- Dosya başına `using CustomerSupportBot.Web.Components;` yoksa ekle (ve `Helpers`).

`AutoRefreshInterval` açıklamasındaki istek sayısı yorumunu (satır ~879, "RefreshBadgesAsync (3 istek)") "4 istek" olarak güncelle.

- [ ] **Step 8: SLA sayfası kartı**

`Sla.razor` — Eskalasyonlar kartından sonra:

```razor
        @if (_status?.Complaints is { } cs)
        {
            @QueueCard("Şikayetler", cs.OpenCount, "açık", cs.OldestSeconds, cs.ResolveWarnAfterSeconds, cs.ResolveBreachAfterSeconds,
                       cs.BreachCountRecent, $"{cs.PendingCount} üstlenilmeyi bekliyor · üstlenme sınırı {FmtDuration(cs.PickupBreachAfterSeconds)}. Sınır aşıldığında yalnızca kayıt tutulur.")
        }
```

`KindLabel`'a: `"complaint_pickup" => "Şikayet (üstlenme)", "complaint_resolve" => "Şikayet (çözüm)",`.

- [ ] **Step 9: Stil**

`admin.css` sonuna:

```css
/* ─── Şikayetler ─────────────────────────────────────────── */
.complaints-layout { display: grid; grid-template-columns: minmax(260px, 2fr) 3fr; gap: 16px; align-items: start; }
@media (max-width: 900px) { .complaints-layout { grid-template-columns: 1fr; } }
.complaints-filters { display: flex; gap: 12px; align-items: end; flex-wrap: wrap; margin-bottom: 12px; }
.complaint-rows { list-style: none; margin: 0; padding: 0; display: grid; gap: 6px; }
.complaint-row { width: 100%; display: grid; grid-template-columns: auto 1fr auto; gap: 4px 10px; text-align: left;
  padding: 10px 12px; border: 1px solid var(--color-border); border-left-width: 4px; border-radius: 8px; background: var(--color-surface); cursor: pointer; }
.complaint-row .small { grid-column: 1 / -1; }
.complaint-row.selected { outline: 2px solid var(--color-primary); }
.complaint-row.sla-warn { border-left-color: #f59e0b; }
.complaint-row.sla-breach { border-left-color: var(--color-error-text); }
.complaint-row.sla-ok { border-left-color: #22c55e; }
.complaints-pager { display: flex; justify-content: space-between; align-items: center; margin-top: 10px; }
.complaint-text { white-space: pre-wrap; }
.complaint-resolution { background: var(--color-primary-subtle); padding: 8px 10px; border-radius: 8px; }
.complaint-actions { display: grid; gap: 10px; margin: 12px 0; }
.complaint-text-input textarea { width: 100%; }
.complaint-timeline { list-style: none; padding: 0; margin: 0; display: grid; gap: 8px; }
.complaint-timeline li { border-left: 2px solid var(--color-border); padding-left: 10px; }
```

(`var(--color-border)` / `--color-surface` adları `admin.css`'te farklıysa dosyadaki karşılıklarını kullan — `grep -n "\-\-color-" src/CustomerSupportBot.Web/wwwroot/css/styles.css | head`.)

- [ ] **Step 10: Derle ve Web testlerini çalıştır**

Run: `dotnet build src/CustomerSupportBot.Web -c Debug 2>&1 | tail -3 && dotnet test --project tests/CustomerSupportBot.Web.Tests`
Expected: 0 hata; Web testleri yeşil.

- [ ] **Step 11: Uçtan uca elle doğrulama (tarayıcı)**

1. `docker compose -p aibot -f deploy/docker-compose.yml up -d`, API (`dotnet run --project src/CustomerSupportBot.Api`) ve Web (`dotnet run --project src/CustomerSupportBot.Web`) çalıştır.
2. Müşteri olarak giriş yap, fotoğraflı bir şikayet aç; admin olarak onayla.
3. Admin → Şikayetler: yeni şikayet listede, fotoğraf ayrıntıda görünür, rozet 1.
4. Temsilci olarak giriş yap → Üstlen → Mailpit'te "Şikayetiniz inceleniyor" e-postası.
5. Açıklamayla "Çözüldü" → Mailpit'te "Şikayetiniz çözüldü"; müşteri sohbetinde "şikayetim ne durumda" → bot açıklamayı söyler, iç notu söylemez.
6. Admin → Yeniden aç → e-posta "inceleniyor"; tekrar çöz → ikinci "çözüldü" e-postası.
7. İki tarayıcıda aynı şikayete aynı anda "Üstlen": biri başarılı, diğeri "başka biri tarafından güncellendi" uyarısı.
Sonuçları ledger'a yaz.

- [ ] **Step 12: Commit**

```bash
git add src/CustomerSupportBot.Web tests/CustomerSupportBot.Web.Tests
git commit -m "feat(staff-ui): complaints tab, badge and SLA card

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Dokümantasyon ve tam test turu

**Files:**
- Modify: `README.md` (özellik listesi + API tablosu)
- Modify: `docs/operations.md` (`Sla:Complaints`, `Email:Notifications` yeni bayraklar)
- Create: `docs/CustomerSupportBot.Domain/Services/ComplaintTransitions.md`, `docs/CustomerSupportBot.Application/Services/Complaints/ComplaintManagementService.md`, `docs/CustomerSupportBot.Application/Services/Notifications/ComplaintStatusEmailService.md`, `docs/CustomerSupportBot.Api/Endpoints/ComplaintEndpoints.md`, `docs/CustomerSupportBot.Web/Components/ComplaintsPanel.md`
- Modify: `docs/CustomerSupportBot.Adapters.Persistence/Postgres/ComplaintRepository.md` (varsa; yoksa oluştur), ilgili klasör README index satırları
- Modify: `docs/superpowers/specs/2026-10-12-complaint-lifecycle-design.md` (yeniden açma yetkisi: "Admin ya da atanan temsilci")

- [ ] **Step 1: README**

Özellik listesine (Hazır Yanıtlar maddesinin altına):

```markdown
- **Şikayet Yönetimi** — Şikayetler `Beklemede → İncelemede → Çözüldü / Reddedildi` akışıyla yönetilir; kapanmış şikayet yeniden açılabilir. Temsilci atanmamış şikayeti üstlenir, yönetici atar; kapatırken müşteriye görünen açıklama zorunludur, iç notlar yalnızca zaman çizelgesinde kalır. Eşzamanlı iki işlemden yalnızca biri kazanır (koşullu güncelleme). Müşteriye incelemeye alınınca ve kapanınca e-posta gider (`Email:Notifications:ComplaintInReview`, `ComplaintClosed`); bot sorulunca güncel durumu ve açıklamayı söyler. Fotoğraflar onay kaydı üzerinden şikayette görünür. Geciken şikayetler SLA ekranına düşer (`Sla:Complaints`).
```

API tablosuna şikayet uçlarını (Task 5 tablosu) ekle.

- [ ] **Step 2: Sınıf dokümanları**

Her yeni sınıf için mevcut kalıbı izle (ör. `docs/CustomerSupportBot.Application/Services/Notifications/OrderStatusEmailService.md` ve `docs/CustomerSupportBot.Api/Endpoints/OrderFulfillmentEndpoints.md`'yi aç, aynı başlık yapısını kullan): amaç, bağımlılıklar, davranış (geçiş tablosu / anahtar biçimi / HTTP eşlemesi), sınırlar (yeniden açılan şikayette SLA olayı tekrar yazılmaz; migration öncesi kayıtların geçmişi boş), testler. Klasör README'lerine birer index satırı.

- [ ] **Step 3: Spec düzeltmesi**

Spec'teki geçiş tablosunda `reopen` satırının "Kim" sütununu "Admin ya da atanan temsilci" yap (Task 1 kararı).

- [ ] **Step 4: Tüm çözümün testleri**

Run: `dotnet build CustomerSupport.slnx -c Release 2>&1 | tail -3 && dotnet test --solution CustomerSupport.slnx -c Release --no-build 2>&1 | tail -15`
Expected: 0 derleme hatası/uyarısı yeni kodda; tüm testler yeşil. Kırmızı test varsa adıyla rapora yaz.

- [ ] **Step 5: Commit**

```bash
git add README.md docs
git commit -m "docs: complaint lifecycle

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
