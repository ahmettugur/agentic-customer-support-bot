// Konuşma kapanışı: neden/etiket/not doğrulaması, bırakma akışı, seçenekler.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Services.Conversations;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ConversationClosingServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Sid = "close-s1";

    private sealed record Harness(
        ConversationClosingService Service, IChatSessionPort Chats, InMemoryConversationDispositionStore Store, FakeTimeProvider Clock);

    private static Harness Build(ConversationClosingOptions? options = null, bool live = true)
    {
        var chats = Substitute.For<IChatSessionPort>();
        chats.ReleaseAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns(ci => live
            ? new ChatSessionReleaseResult(ci.ArgAt<string>(0), 2)
            : new ChatSessionReleaseResult(ci.ArgAt<string>(0), 0, "not_found", "Session zaten Bot modda."));
        var store = new InMemoryConversationDispositionStore();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 15, 0, 0, TimeSpan.Zero));
        var service = new ConversationClosingService(chats, store, Options.Create(options ?? new ConversationClosingOptions()), clock);
        return new Harness(service, chats, store, clock);
    }

    private static ConversationClosingInput Input(string? reason = "resolved", string[]? tags = null, string? note = null) =>
        new(reason, tags, note);

    [Fact]
    public async Task Close_ReleasesTheChat_AndRecordsTheDisposition()
    {
        var h = Build();

        var r = await h.Service.CloseAsync(Sid, Input("follow_up_required", ["kargo"], "  Yarın aranacak. "), "jane", "agent-1", Ct);

        r.Status.Should().Be(ConversationClosingStatus.Ok);
        r.EscalationsResolved.Should().Be(2);
        await h.Chats.Received(1).ReleaseAsync(Sid, "agent-1");
        var saved = (await h.Store.ListForSessionAsync(Sid, Ct)).Should().ContainSingle().Subject;
        saved.ReasonCode.Should().Be("follow_up_required");
        saved.Tags.Should().Equal("kargo");
        saved.Note.Should().Be("Yarın aranacak.");
        saved.ClosedBy.Should().Be("jane");
        saved.ClosedAt.Should().Be(h.Clock.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task Tags_AreNormalized_AndDeduplicated()
    {
        var h = Build();

        await h.Service.CloseAsync(Sid, Input(tags: [" Kargo Gecikmesi ", "IADE", "iade", "kargo-gecikmesi", "İade", ""]), "jane", null, Ct);

        (await h.Store.ListForSessionAsync(Sid, Ct)).Single().Tags.Should().Equal("kargo-gecikmesi", "iade");
    }

    [Theory]
    [InlineData(null, "Kapanış nedeni")]          // zorunlu
    [InlineData("  ", "Kapanış nedeni")]
    [InlineData("vazgecti", "Bilinmeyen")]
    public async Task InvalidReason_KeepsTheChatOpen(string? reason, string error)
    {
        var h = Build();

        var r = await h.Service.CloseAsync(Sid, Input(reason), "jane", null, Ct);

        r.Status.Should().Be(ConversationClosingStatus.Invalid);
        r.Error.Should().Contain(error);
        await h.Chats.DidNotReceiveWithAnyArgs().ReleaseAsync(default!, default);
        (await h.Store.ListForSessionAsync(Sid, Ct)).Should().BeEmpty();
    }

    public static TheoryData<string[]?, string?> InvalidExtras => new()
    {
        { Enumerable.Range(0, 11).Select(i => $"etiket{i}").ToArray(), null },   // en fazla 10 etiket
        { [new string('a', 31)], null },                                         // etiket ≤ 30
        { ["kargo!"], null },                                                     // yalnız harf/rakam/-/_
        { null, new string('n', 1001) }                                           // not ≤ 1000
    };

    [Theory]
    [MemberData(nameof(InvalidExtras))]
    public async Task InvalidTagsOrNote_KeepTheChatOpen(string[]? tags, string? note)
    {
        var h = Build();

        var r = await h.Service.CloseAsync(Sid, Input(tags: tags, note: note), "jane", null, Ct);

        r.Status.Should().Be(ConversationClosingStatus.Invalid);
        await h.Chats.DidNotReceiveWithAnyArgs().ReleaseAsync(default!, default);
    }

    [Fact]
    public async Task ChatThatIsNotLive_IsReported_AndNothingIsRecorded()
    {
        var h = Build(live: false);

        var r = await h.Service.CloseAsync(Sid, Input(), "jane", null, Ct);

        r.Status.Should().Be(ConversationClosingStatus.NotLive);
        (await h.Store.ListForSessionAsync(Sid, Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task StoreFailure_AfterRelease_ReportsAWarning_InsteadOfAFailure()
    {
        var chats = Substitute.For<IChatSessionPort>();
        chats.ReleaseAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns(new ChatSessionReleaseResult(Sid, 0));
        var store = Substitute.For<CustomerSupportBot.Application.Ports.Outbound.Persistence.IConversationDispositionStore>();
        store.AddAsync(Arg.Any<ConversationDisposition>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("db kapalı")));
        var service = new ConversationClosingService(chats, store, Options.Create(new ConversationClosingOptions()));

        var r = await service.CloseAsync(Sid, Input(), "jane", null, Ct);

        r.Status.Should().Be(ConversationClosingStatus.Ok, "sohbet kapandı — panel bunu hata sanmamalı");
        r.Error.Should().Contain("kapanış kaydı yazılamadı");
    }

    [Fact]
    public async Task ReasonOptional_EmptyInput_ClosesWithoutARecord()
    {
        var h = Build(new ConversationClosingOptions { RequireReason = false });

        var r = await h.Service.CloseAsync(Sid, Input(reason: null), "jane", null, Ct);

        r.Status.Should().Be(ConversationClosingStatus.Ok);
        r.Disposition.Should().BeNull();
        await h.Chats.Received(1).ReleaseAsync(Sid, null);
        (await h.Store.ListForSessionAsync(Sid, Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Options_ListDefaultReasons_AndTheMostUsedTags()
    {
        var h = Build();
        foreach (var tags in new[] { new[] { "kargo", "iade" }, ["kargo"], ["fatura"] })
            await h.Service.CloseAsync($"s-{Guid.NewGuid():N}", Input(tags: tags), "jane", null, Ct);

        var o = await h.Service.GetOptionsAsync(Ct);

        o.RequireReason.Should().BeTrue();
        o.Reasons.Select(r => r.Code).Should().Equal(
            "resolved", "information_provided", "follow_up_required", "customer_unresponsive", "transferred", "other");
        o.Reasons[0].Label.Should().Be("Çözüldü");
        o.SuggestedTags.Should().StartWith("kargo");
        o.SuggestedTags.Should().BeEquivalentTo("kargo", "iade", "fatura");
    }

    [Fact]
    public void ConfiguredReasons_ReplaceTheDefaults_InsteadOfMergingWithThem()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConversationClosing:Reasons:0:Code"] = "solved",
            ["ConversationClosing:Reasons:0:Label"] = "Çözüldü",
            ["ConversationClosing:Reasons:1:Code"] = "spam",
            ["ConversationClosing:Reasons:1:Label"] = "Spam"
        }).Build();
        var options = new ConversationClosingOptions();
        config.GetSection(ConversationClosingOptions.SectionName).Bind(options);

        options.EffectiveReasons.Select(r => r.Code).Should().Equal("solved", "spam");
    }
}
