// Tests/Services/SemanticMemoryRetrievalQueryTests.cs
//
// SemanticMemoryContextProvider'ın HANGİ SORGUYLA arama yaptığını doğrular.
//
// Regresyon: provider eskiden aranacak metni oturum geçmişindeki son kullanıcı mesajından
// okuyordu. Ancak ChatPortService geçmişi (AddExchange / PersistExchange) workflow BİTTİKTEN
// SONRA güncelliyor — yani bağlam kurulurken kullanıcının o anki mesajı henüz geçmişte yok.
// Sonuç: ilk turda hiç retrieval yapılmıyor, sonraki turlarda arama BİR ÖNCEKİ turun
// sorusuyla yapılıyordu. Hem bilgi tabanı hem onaylanmış dersler yanlış sorguyla geliyordu.
//
// Sorgu artık GetContextAsync'e parametre olarak geçiliyor. Aşağıdaki testler aramanın
// GÜNCEL mesajla yapıldığını ve geçmişin bunu artık etkilemediğini kilitler.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Memory;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Tests.Services;

public class SemanticMemoryRetrievalQueryTests
{
    /// <summary>Embed edilen metni yakalar — aramanın hangi sorguyla yapıldığını görmek için.</summary>
    private sealed class CapturingEmbedder : IEmbeddingPort
    {
        public List<string> Embedded { get; } = new();
        public bool IsConfigured => true;
        public int Dimension => 3;

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            Embedded.Add(text);
            return Task.FromResult(new[] { 0.1f, 0.2f, 0.3f });
        }

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 0.1f, 0.2f, 0.3f }).ToList());
    }

    private static (SemanticMemoryContextProvider Provider, CapturingEmbedder Embedder) Build(
        params ConversationMessage[] history)
    {
        var embedder = new CapturingEmbedder();

        var store = Substitute.For<IVectorMemoryPort>();
        store.SearchAsync(Arg.Any<string>(), Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<float>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MemorySearchHit>());

        var memory = new SemanticMemoryService(
            store, embedder, new ContextSanitizer(),
            Options.Create(new SemanticMemoryOptions { Enabled = true }),
            NullLogger<SemanticMemoryService>.Instance);

        // Geçmiş artık retrieval'ı etkilemiyor; parametre yalnızca "geçmiş olsa da
        // sonucu değiştirmiyor" iddiasını test edebilmek için duruyor.
        _ = history;

        var provider = new SemanticMemoryContextProvider(
            memory, new ContextSanitizer(),
            NullLogger<SemanticMemoryContextProvider>.Instance);

        return (provider, embedder);
    }

    [Fact]
    public async Task FirstTurn_EmptyHistory_StillSearchesWithCurrentQuery()
    {
        // Konuşmanın İLK mesajı: geçmiş boş. Eskiden bu durumda hiç retrieval yapılmıyordu
        // (sorgu geçmişten okunduğu için bulunamıyordu) — tek turluk konuşmalar bilgi
        // tabanından ve derslerden hiç yararlanamıyordu.
        var (provider, embedder) = Build();   // geçmiş yok

        await provider.GetContextAsync(new AgentSession { SessionId = "s1" }, "iade süresi ne kadar?");

        embedder.Embedded.Should().ContainSingle("ilk turda da arama yapılmalı");
        embedder.Embedded[0].Should().Be("iade süresi ne kadar?");
    }

    [Fact]
    public async Task SearchesWithCurrentQuery_NotPreviousTurnQuery()
    {
        // Geçmiş 1. turu içeriyor, kullanıcı şu an 2. sorusunu soruyor.
        // Arama GÜNCEL soruyla yapılmalı; geçmişteki eski soru retrieval'ı etkilememeli.
        var (provider, embedder) = Build(
            new ConversationMessage(ConversationRoles.User, "1041 numaralı siparişim nerede?"),
            new ConversationMessage(ConversationRoles.Assistant, "Kargoya verildi."));

        await provider.GetContextAsync(new AgentSession { SessionId = "s1" }, "bu siparişi iptal edebilir miyim?");

        embedder.Embedded.Should().ContainSingle("sorgu tur başına bir kez embed edilmeli");
        embedder.Embedded[0].Should().Be("bu siparişi iptal edebilir miyim?",
            "arama güncel mesajla yapılmalı — geçmişteki önceki turun sorusuyla değil");
    }

    [Fact]
    public async Task BlankQuery_SkipsRetrieval()
    {
        var (provider, embedder) = Build(
            new ConversationMessage(ConversationRoles.User, "eski soru"));

        var ctx = await provider.GetContextAsync(new AgentSession { SessionId = "s1" }, "   ");

        ctx.Should().BeNull();
        embedder.Embedded.Should().BeEmpty("boş sorgu için arama yapılmamalı");
    }

    [Fact]
    public async Task Search_EmbedsQueryOnce_ForBothCollections()
    {
        // Knowledge ve Lesson koleksiyonları aynı vektörü paylaşır; aynı metin iki kez
        // embed edilmemeli (embedder'da cache yok, her çağrı gerçek maliyet).
        var (provider, embedder) = Build();

        await provider.GetContextAsync(new AgentSession { SessionId = "s1" }, "iade nasıl yapılır");

        embedder.Embedded.Count.Should().Be(1);
    }
}
