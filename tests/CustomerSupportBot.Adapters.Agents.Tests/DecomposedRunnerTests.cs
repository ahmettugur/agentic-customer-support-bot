// Tests/DecomposedRunnerTests.cs
//
// Compound sorgu orkestrasyonu: alt görevlerin gruplanması, paralel/sıralı çalıştırılması,
// sonuçların sub.Order sırasında toplanması ve TAMAMLANDIKÇA ilerlemeli yayınlanması.
//
// Bu testler IWorkflowRunner arayüzü çıkarıldığı için mümkün: önceden DecomposedRunner somut
// (sealed) WorkflowRunner'ı alıyordu, o da altı ajan + MAF workflow'u kurmayı gerektirdiğinden
// bu mantık ancak Docker/Testcontainers fixture'ıyla çalıştırılabiliyor, pratikte hiç izole
// test edilmiyordu. Buradaki sahte koşucu tek alt görev koşusunu taklit eder; test edilen şey
// DecomposedRunner'ın kendi orkestrasyonudur.

using CustomerSupportBot.Adapters.Agents;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Reasoning;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Adapters.Agents.Tests;

public class DecomposedRunnerTests
{
    /// <summary>Her alt görev sorgusuna sabit bir yanıt döndüren, çağrı sırasını kaydeden sahte koşucu.</summary>
    private sealed class FakeRunner : IWorkflowRunner
    {
        private readonly Func<string, string> _reply;
        public List<string> Calls { get; } = new();

        public FakeRunner(Func<string, string>? reply = null)
            => _reply = reply ?? (q => $"yanıt<{q}>");

        public Task<string> RunAsync(string query, List<ConversationMessage>? history,
            AgentSession? session, ReasoningResult? reasoning, CancellationToken ct)
        {
            lock (Calls) Calls.Add(query);
            return Task.FromResult(_reply(query));
        }

        public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(string query,
            List<ConversationMessage>? history, AgentSession? session, ReasoningResult? reasoning,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            lock (Calls) Calls.Add(query);
            yield return new StreamEvent(StreamEventTypes.ResponseStart, null);
            yield return new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload(_reply(query)));
            yield return new StreamEvent(StreamEventTypes.ResponseComplete,
                new ResponseCompletePayload(_reply(query)));
            await Task.CompletedTask;
        }
    }

    private static DecomposedRunner Build(IWorkflowRunner runner, ParallelExecutionOptions? opts = null)
        => new(runner,
               opts ?? new ParallelExecutionOptions(),
               Substitute.For<IUiHintEmitter>(),
               Substitute.For<IApprovalContextAccessor>());

    private static ReasoningResult Reasoning(params SubTask[] subs)
        => new() { SubTasks = subs.ToList() };

    private static SubTask Sub(int order, string desc, string agent) =>
        new() { Order = order, Description = desc, TargetAgent = agent, Intent = desc };

    private static async Task<List<StreamEvent>> CollectAsync(DecomposedRunner runner, ReasoningResult reasoning)
    {
        var events = new List<StreamEvent>();
        await foreach (var e in runner.RunDecomposedStreamingAsync(
            "bileşik sorgu", null, new AgentSession { SessionId = "s1" }, reasoning, CancellationToken.None))
        {
            events.Add(e);
        }
        return events;
    }

    private static string ConcatDeltas(IEnumerable<StreamEvent> events) =>
        string.Concat(events
            .Where(e => e.Type == StreamEventTypes.ResponseDelta)
            .Select(e => ((TextDeltaPayload)e.Data!).Text));

    private static string CompleteText(IEnumerable<StreamEvent> events) =>
        ((ResponseCompletePayload)events.Last(e => e.Type == StreamEventTypes.ResponseComplete).Data!).Text;

    // ═══ Kayıpsızlık: akan metin ile nihai metin ayrışmamalı ═══

    /// <summary>
    /// EN KRİTİK DEĞİŞMEZ: ilerlemeli olarak yayınlanan delta'ların birleşimi,
    /// response_complete'in taşıdığı nihai metne BİREBİR eşit olmalı. Ayrışırsa kullanıcı
    /// yanıtın sonunda metnin gözle görülür şekilde sıçradığını görür.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task StreamedDeltas_ConcatenateExactlyTo_FinalText(int subTaskCount)
    {
        var subs = Enumerable.Range(1, subTaskCount)
            .Select(i => Sub(i, $"görev{i}", WellKnown.AgentNames.Product)).ToArray();

        var events = await CollectAsync(Build(new FakeRunner()), Reasoning(subs));

        ConcatDeltas(events).Should().Be(CompleteText(events));
    }

    /// <summary>Nihai metin, SubTaskOrchestrator'ın birleştirme çıktısıyla da aynı olmalı.</summary>
    [Fact]
    public async Task FinalText_MatchesSubTaskOrchestratorAggregate()
    {
        var subs = new[] { Sub(1, "a", WellKnown.AgentNames.Product), Sub(2, "b", WellKnown.AgentNames.Product) };
        var runner = new FakeRunner();

        var events = await CollectAsync(Build(runner), Reasoning(subs));

        var expected = SubTaskOrchestrator.AggregateSubTaskResults(
            subs.Select(sc => SubTaskOrchestrator.FormatSubTaskResult(
                sc, runner.RunAsync(SubTaskOrchestrator.FormatSubTaskQuery(sc), null, null, null, default).Result))
                .ToList());

        CompleteText(events).Should().Be(expected);
    }

    // ═══ İlerlemeli yayın: sonuçlar TAMAMLANDIKÇA gitmeli ═══

    /// <summary>
    /// Asıl kazanç: ilk alt görevin metni, SON alt görev bitmeden önce yayınlanmış olmalı.
    /// Eski davranışta hiçbir metin tüm alt görevler bitene kadar gönderilmiyordu.
    /// </summary>
    [Fact]
    public async Task EmitsFirstResult_BeforeLastSubTaskCompletes()
    {
        var subs = new[] { Sub(1, "ilk", WellKnown.AgentNames.Product), Sub(2, "son", WellKnown.AgentNames.Product) };

        var events = await CollectAsync(Build(new FakeRunner()), Reasoning(subs));

        var firstDeltaIdx = events.FindIndex(e => e.Type == StreamEventTypes.ResponseDelta);
        var lastSubTaskDoneIdx = events.FindLastIndex(e =>
            e.Type == StreamEventTypes.Agent && (e.Data?.ToString() ?? "").Contains("SubTask#2"));

        firstDeltaIdx.Should().BeGreaterThan(-1, "en az bir metin parçası yayınlanmalı");
        firstDeltaIdx.Should().BeLessThan(lastSubTaskDoneIdx,
            "ilk alt görevin sonucu, son alt görev bitmeden yayınlanmalı — yoksa ilerlemeli yayın yok demektir");
    }

    /// <summary>
    /// <c>response_start</c> ilk delta'dan ÖNCE gelmeli — "yanıt metni akmaya başlıyor"
    /// anlamı tek-sorgu ve compound yollarında AYNI olsun diye.
    ///
    /// <para>
    /// Bu eskiden tersineydi: compound yolda response_start tüm metinden sonra gönderiliyordu,
    /// çünkü Blazor o olayda ajan çiplerini mühürlüyor ve erken mühür "SubTask#N" ilerleme
    /// çiplerini yok ediyordu. İşlevsel bir hata değildi ama tuzaktı — sıraya güvenen yeni bir
    /// tüketici (ör. "response_start geldi, spinner'ı gizle") compound'da sessizce yanılırdı.
    /// Çözüm sıralamayı değil, mühürlemeyi taşımak oldu: olay <c>decomposed=true</c> bayrağını
    /// taşıyor ve Blazor bunu görünce mühürlemeyi response_complete'e erteliyor.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ResponseStart_ComesBeforeAnyDelta_SoOrderingIsMeaningful()
    {
        var subs = new[] { Sub(1, "a", WellKnown.AgentNames.Product), Sub(2, "b", WellKnown.AgentNames.Product) };

        var events = await CollectAsync(Build(new FakeRunner()), Reasoning(subs));

        var startIdx = events.FindIndex(e => e.Type == StreamEventTypes.ResponseStart);
        var firstDeltaIdx = events.FindIndex(e => e.Type == StreamEventTypes.ResponseDelta);

        startIdx.Should().BeGreaterThan(-1, "response_start mutlaka gönderilmeli");
        startIdx.Should().BeLessThan(firstDeltaIdx,
            "sıraya güvenen tüketiciler compound'da da doğru çalışabilmeli");
    }

    /// <summary>
    /// Erken response_start'ın Blazor'da çipleri erken mühürlememesi <c>decomposed=true</c>
    /// bayrağına bağlı. Bayrak düşerse alt görev ilerleme çipleri sessizce kaybolur —
    /// bu test o bağı backend tarafında kilitler.
    /// </summary>
    [Fact]
    public async Task ResponseStart_CarriesDecomposedFlag_SoClientDefersChipSealing()
    {
        var subs = new[] { Sub(1, "a", WellKnown.AgentNames.Product) };

        var events = await CollectAsync(Build(new FakeRunner()), Reasoning(subs));

        var start = events.First(e => e.Type == StreamEventTypes.ResponseStart);
        var json = System.Text.Json.JsonSerializer.Serialize(start.Data,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

        json.Should().Contain("\"decomposed\":true",
            "Blazor bu bayrağı okuyup mühürlemeyi erteliyor (Chat.razor → response_start)");
    }

    // ═══ Sıralama: paralel tamamlanma sırası çıktı sırasını BOZMAMALI ═══

    /// <summary>
    /// Paralel grupta alt görevler sırasız tamamlansa bile (burada 1. yavaş, 2. hızlı),
    /// yayınlanan metin sub.Order sırasında olmalı.
    ///
    /// <para>
    /// ⚠️ <b>Bu testin sınırı ölçülerek bulundu:</b> sırayı sağlayan asıl mekanizma
    /// <c>.OrderBy(t =&gt; t.sub.Order)</c> DEĞİL — <c>Task.WhenAll</c> sonuçları zaten
    /// <b>görevlerin oluşturulma sırasında</b> döndürüyor ve görevler <c>group.Items</c>
    /// sırasında oluşturuluyor. Mutasyon testi bunu doğruladı: <c>.OrderBy</c> tamamen
    /// kaldırıldığında hiçbir test kırılmıyor, yani o çağrı derinlemesine savunma
    /// (defense-in-depth), tek koruma değil. Sırayı gerçekten kıran bir mutasyon
    /// (<c>OrderByDescending</c>) ise bu testi ve kayıpsızlık testini birlikte kırıyor.
    /// </para>
    ///
    /// <para>
    /// Dolayısıyla bu test bir <b>gözlemlenebilir sonucu</b> kilitler ("çıktı sırası
    /// tamamlanma sırasına bağlı değildir"), belirli bir implementasyon satırını değil.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ParallelGroup_OutOfOrderCompletion_StillEmitsInSubTaskOrder()
    {
        var slowFirst = new SlowFirstRunner();
        var subs = new[]
        {
            Sub(1, "yavaş", WellKnown.AgentNames.Product),
            Sub(2, "hızlı", WellKnown.AgentNames.Product)
        };

        var events = await CollectAsync(
            Build(slowFirst, new ParallelExecutionOptions { MaxDegreeOfParallelism = 2 }),
            Reasoning(subs));

        var text = CompleteText(events);
        text.IndexOf("**1)", StringComparison.Ordinal).Should().BeLessThan(
            text.IndexOf("**2)", StringComparison.Ordinal),
            "çıktı sırası tamamlanma sırasına değil sub.Order'a bağlı olmalı");
        ConcatDeltas(events).Should().Be(text);
    }

    /// <summary>1. alt görevi kasıtlı yavaşlatan koşucu — paralel tamamlanma sırasını tersine çevirir.</summary>
    private sealed class SlowFirstRunner : IWorkflowRunner
    {
        public async Task<string> RunAsync(string query, List<ConversationMessage>? history,
            AgentSession? session, ReasoningResult? reasoning, CancellationToken ct)
        {
            if (query.Contains("yavaş", StringComparison.Ordinal)) await Task.Delay(60, ct);
            return $"yanıt<{query}>";
        }

        public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(string query,
            List<ConversationMessage>? history, AgentSession? session, ReasoningResult? reasoning,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            yield return new StreamEvent(StreamEventTypes.ResponseDelta,
                new TextDeltaPayload(await RunAsync(query, history, session, reasoning, ct)));
        }
    }

    // ═══ Sıralı dal: alt görevin GERÇEK token akışı canlı iletilmeli ═══

    /// <summary>Bir alt görev için birden çok token yayan koşucu — gerçek LLM akışını taklit eder.</summary>
    private sealed class TokenStreamingRunner : IWorkflowRunner
    {
        private readonly string[] _tokens;
        public TokenStreamingRunner(params string[] tokens) => _tokens = tokens;

        public Task<string> RunAsync(string query, List<ConversationMessage>? history,
            AgentSession? session, ReasoningResult? reasoning, CancellationToken ct)
            => Task.FromResult(string.Concat(_tokens));

        public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(string query,
            List<ConversationMessage>? history, AgentSession? session, ReasoningResult? reasoning,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var t in _tokens)
                yield return new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload(t));
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Asıl kazanç: alt görevin token'ları TEK bir yığın hâlinde değil, geldikleri gibi
    /// ayrı delta'lar olarak iletilmeli. Eskiden hepsi StringBuilder'da yutulup sonda tek
    /// parça olarak yayınlanıyordu.
    /// </summary>
    [Fact]
    public async Task SequentialSubTask_ForwardsRealTokens_AsSeparateDeltas()
    {
        var runner = new TokenStreamingRunner("Mer", "haba ", "dünya");
        var subs = new[] { Sub(1, "selam", WellKnown.AgentNames.Product) };

        var events = await CollectAsync(Build(runner), Reasoning(subs));

        var bodyDeltas = events
            .Where(e => e.Type == StreamEventTypes.ResponseDelta)
            .Select(e => ((TextDeltaPayload)e.Data!).Text)
            .Where(t => !t.StartsWith("**", StringComparison.Ordinal))  // başlık hariç
            .ToList();

        bodyDeltas.Should().HaveCountGreaterThan(1,
            "token'lar geldikleri gibi iletilmeli — tek yığın hâlinde değil");
        string.Concat(bodyDeltas).Should().Be("Merhaba dünya");
        ConcatDeltas(events).Should().Be(CompleteText(events));
    }

    /// <summary>
    /// Canlı akışta ham token'lar baştaki/sondaki boşluğu taşısa bile, akan metin nihai
    /// metne eşit kalmalı — TrimmingDeltaStreamer bunu sağlar.
    /// </summary>
    [Fact]
    public async Task SequentialSubTask_RawTokensWithPadding_StillMatchFinalText()
    {
        var runner = new TokenStreamingRunner("   ", "içerik", "  ");
        var subs = new[] { Sub(1, "test", WellKnown.AgentNames.Product) };

        var events = await CollectAsync(Build(runner), Reasoning(subs));

        ConcatDeltas(events).Should().Be(CompleteText(events),
            "ham token'lardaki boşluk akan metni nihai metinden ayrıştırmamalı");
        CompleteText(events).Should().EndWith("içerik");
    }

    // ═══ Her alt görev gerçekten çalıştırılıyor mu ═══

    [Fact]
    public async Task RunsEverySubTask_Once()
    {
        var runner = new FakeRunner();
        var subs = Enumerable.Range(1, 3).Select(i => Sub(i, $"g{i}", WellKnown.AgentNames.Product)).ToArray();

        await CollectAsync(Build(runner), Reasoning(subs));

        runner.Calls.Should().HaveCount(3);
        runner.Calls.Should().OnlyHaveUniqueItems();
    }
}
