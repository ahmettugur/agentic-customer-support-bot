// Bilgi tabanından gelen makaleler müşteriye "ilgili makaleler" olarak gösterilir. Dürüstlük: bu, MODELİN dayandığı kaynak değil,
// bu tur bağlama ENJEKTE edilen makalelerdir — etiket buna göre ("ilgili"), "kaynak" değil. Ders ve episodik bellek gösterilmez.

using System.Text.Json;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Services.Memory;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class KnowledgeSourcesHintTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
        { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static MemorySearchHit Hit(MemoryKind kind, string text, string? title, string? source = "x.md") =>
        new() { Document = new MemoryDocument { Kind = kind, Text = text, Title = title, Source = source }, Score = 0.9f };

    private static (SemanticMemoryContextProvider Provider, List<JsonElement> Emitted) Build(
        IReadOnlyList<MemorySearchHit> knowledge, IReadOnlyList<MemorySearchHit>? lessons = null, bool withEmitter = true, bool emitReturns = true)
    {
        var store = Substitute.For<IVectorMemoryPort>();
        store.SearchAsync(Arg.Any<string>(), Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<float>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<string>() == "cs_knowledge" ? knowledge : lessons ?? []);
        var embedder = Substitute.For<IEmbeddingPort>();
        embedder.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new float[] { 0.1f });
        var memory = new SemanticMemoryService(store, embedder, new ContextSanitizer(),
            Options.Create(new SemanticMemoryOptions { Enabled = true }), NullLogger<SemanticMemoryService>.Instance);

        var emitted = new List<JsonElement>();
        var hints = Substitute.For<IUiHintEmitter>();
        hints.Emit(Arg.Any<StreamEvent>()).Returns(call =>
        {
            emitted.Add(JsonSerializer.SerializeToElement(call.Arg<StreamEvent>().Data, Web));
            return emitReturns;
        });
        return (new SemanticMemoryContextProvider(memory, new ContextSanitizer(), NullLogger<SemanticMemoryContextProvider>.Instance,
            withEmitter ? hints : null), emitted);
    }

    private static Task<string?> RunAsync(SemanticMemoryContextProvider p) =>
        p.GetContextAsync(new AgentSession { SessionId = "s", State = new SessionState() }, "iade koşulları", Ct);

    private static string[] Titles(JsonElement hint) =>
        hint.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("title").GetString()!).ToArray();

    [Fact]
    public async Task KnowledgeArticlesInTheContext_AreOfferedAsRelatedArticles()
    {
        var (provider, emitted) = Build([Hit(MemoryKind.Knowledge, "İade 14 gündür.", "İade Politikası"), Hit(MemoryKind.Knowledge, "Kargo 3 gün.", "Kargo Süreleri")]);

        await RunAsync(provider);

        var hint = emitted.Should().ContainSingle().Subject;
        hint.GetProperty("kind").GetString().Should().Be("sources");
        Titles(hint).Should().Equal("İade Politikası", "Kargo Süreleri");
    }

    [Fact]
    public async Task SameArticleTwice_IsListedOnce_AndAtMostThreeAreShown()
    {
        var (provider, emitted) = Build(
        [
            Hit(MemoryKind.Knowledge, "a1", "A"), Hit(MemoryKind.Knowledge, "a2", "A"), Hit(MemoryKind.Knowledge, "b", "B"),
            Hit(MemoryKind.Knowledge, "c", "C"), Hit(MemoryKind.Knowledge, "d", "D")
        ]);

        await RunAsync(provider);

        Titles(emitted.Single()).Should().Equal("A", "B", "C");
    }

    [Fact]
    public async Task LessonsAndEpisodes_AreNeverShownAsArticles()
    {
        var (provider, emitted) = Build([], lessons: [Hit(MemoryKind.Lesson, "kısa yanıt ver", "Ders 1")]);

        await RunAsync(provider);

        emitted.Should().BeEmpty();
    }

    [Fact]
    public async Task ArticlesThatDidNotFitTheContextBudget_AreNotShown()
    {
        // Bağlama GİRMEYEN makale "ilgili" diye gösterilmemeli: model onu hiç görmedi.
        var big = new string('x', 5000);
        var (provider, emitted) = Build(Enumerable.Range(0, 12).Select(i => Hit(MemoryKind.Knowledge, big, $"Makale {i}")).ToList());

        var context = await RunAsync(provider);

        var shown = Titles(emitted.Single());
        shown.Should().OnlyContain(t => context!.Contains($"**[{t}]**"));
    }

    [Fact]
    public async Task ArticleWithoutATitle_FallsBackToItsSourceName()
    {
        var (provider, emitted) = Build([Hit(MemoryKind.Knowledge, "metin", title: null, source: "iade-politikasi.md")]);

        await RunAsync(provider);

        Titles(emitted.Single()).Should().Equal("iade-politikasi.md");
    }

    [Fact]
    public async Task NothingRetrieved_EmitsNothing_AndWithoutAnEmitterTheContextIsUnchanged()
    {
        var (empty, emitted) = Build([]);
        await RunAsync(empty);

        var (plain, _) = Build([Hit(MemoryKind.Knowledge, "İade 14 gündür.", "İade Politikası")], withEmitter: false);
        var context = await RunAsync(plain);

        emitted.Should().BeEmpty();
        context.Should().Contain("**[İade Politikası]**");
    }

    [Fact]
    public async Task WhenTheChannelCannotShowHints_TheContextIsUnchanged()
    {
        var (provider, _) = Build([Hit(MemoryKind.Knowledge, "İade 14 gündür.", "İade Politikası")], emitReturns: false);

        (await RunAsync(provider)).Should().Contain("**[İade Politikası]**");
    }
}
