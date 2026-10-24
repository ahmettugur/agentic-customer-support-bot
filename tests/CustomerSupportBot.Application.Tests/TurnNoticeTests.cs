// Bu turda sistemin yaptığı işlem (tercih kaydedildi/silindi) ajanın bağlamına girer ki yanıt tutarlı olsun:
// sohbette "tercihiniz silindi" notu varken bot "hangi tercihleri sıfırlayayım?" diye sormasın.

using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Domain.Model;
using SessionState = CustomerSupportBot.Domain.Model.SessionState;

namespace CustomerSupportBot.Application.Tests;

public class TurnNoticeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ANotice_BecomesAContextBlock()
    {
        var session = new AgentSession { SessionId = "s", State = new SessionState { TurnNotice = "Müşterinin kayıtlı tercihleri silindi." } };

        var ctx = await new TurnNoticeContextProvider().GetContextAsync(session, "q", Ct);

        ctx.Should().Contain("Bu turda sistem tarafından yapıldı").And.Contain("silindi");
    }

    [Fact]
    public async Task WithoutANotice_NothingIsAdded()
    {
        var session = new AgentSession { SessionId = "s", State = new SessionState() };

        (await new TurnNoticeContextProvider().GetContextAsync(session, "q", Ct)).Should().BeNull();
    }

    [Fact]
    public void TheNotice_IsNeverPersisted()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new SessionState { TurnNotice = "geçici" });

        json.Should().NotContain("geçici").And.NotContain("TurnNotice");
    }
}
