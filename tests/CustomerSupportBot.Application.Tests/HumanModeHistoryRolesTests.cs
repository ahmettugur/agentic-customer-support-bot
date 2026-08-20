// İnsan modunda konuşma geçmişinin ROLLERİ.
//
// Geçmiş yalnızca bir kayıt değil; bot oturumu geri devraldığında modele verilen bağlamın
// kendisidir. Roller yanlışsa model konuşmayı yanlış okur.
//
// İki kusur birlikte çalışıyordu: müşteri mesajı yazılırken BOŞ bir asistan mesajı da
// bırakılıyor, temsilcinin cevabı ise KULLANICI rolüyle ekleniyordu. Sonuç, bot devraldığında
// temsilcinin "iadeniz onaylandı" cevabını müşterinin yeni bir mesajı sanması ve ona cevap
// vermeye çalışmasıydı.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Reasoning;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Tests;

public class HumanModeHistoryRolesTests
{
    private static (ChatSessionPortService Service, ISessionManager Sessions) Build()
    {
        var modes = Substitute.For<IChatModeRegistry>();
        modes.GetMode(Arg.Any<string>()).Returns(ChatMode.Human);

        var sessions = Substitute.For<ISessionManager>();

        return (new ChatSessionPortService(
            modes,
            Substitute.For<IChatBridge>(),
            sessions,
            Substitute.For<IEscalationSink>(),
            Substitute.For<IHumanAgentRegistry>(),
            Substitute.For<IReplanService>()), sessions);
    }

    /// <summary>
    /// ASIL BULGU. Temsilci konuşmada botun yerini alır; söylediği şey müşteriden gelen bir
    /// GİRDİ değil, müşteriye verilen bir YANITTIR.
    /// </summary>
    [Fact]
    public async Task AdminReply_IsAppendedAsAssistant_NotAsUser()
    {
        var (service, sessions) = Build();

        var result = await service.SendAdminMessageAsync(
            "s-1", "alice", "iadeniz onaylandı", TestContext.Current.CancellationToken);

        result.ErrorCode.Should().BeNull();

        await sessions.Received(1).AppendAssistantMessageAsync(
            "s-1", Arg.Is<string>(t => t.Contains("iadeniz onaylandı")), Arg.Any<CancellationToken>());

        await sessions.DidNotReceive().AppendUserMessageAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Etiket korunmalı: modelin bunu kendi ürettiği bir yanıt değil, insan temsilcinin sözü
    /// olarak görmesi doğru bağlamı verir.
    /// </summary>
    [Fact]
    public async Task AdminReply_KeepsTheAgentLabel()
    {
        var (service, sessions) = Build();

        await service.SendAdminMessageAsync(
            "s-1", "alice", "merhaba", TestContext.Current.CancellationToken);

        await sessions.Received(1).AppendAssistantMessageAsync(
            "s-1", Arg.Is<string>(t => t.Contains("alice")), Arg.Any<CancellationToken>());
    }

    /// <summary>Bot modunda temsilci mesajı kabul edilmemeli — koruma yerinde kalmalı.</summary>
    [Fact]
    public async Task AdminReply_IsRejected_WhenSessionIsNotInHumanMode()
    {
        var modes = Substitute.For<IChatModeRegistry>();
        modes.GetMode(Arg.Any<string>()).Returns(ChatMode.Bot);
        var sessions = Substitute.For<ISessionManager>();

        var service = new ChatSessionPortService(
            modes, Substitute.For<IChatBridge>(), sessions,
            Substitute.For<IEscalationSink>(), Substitute.For<IHumanAgentRegistry>(),
            Substitute.For<IReplanService>());

        var result = await service.SendAdminMessageAsync(
            "s-1", "alice", "merhaba", TestContext.Current.CancellationToken);

        result.ErrorCode.Should().Be("invalid_state");
        await sessions.DidNotReceive().AppendAssistantMessageAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
