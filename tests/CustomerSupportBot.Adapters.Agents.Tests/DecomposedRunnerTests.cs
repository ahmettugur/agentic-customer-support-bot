// Tests/DecomposedRunnerTests.cs
//
// Compound sorgu orkestrasyonu: alt görevlerin gruplanması, paralel/sıralı çalıştırılması,
// sonuçların sub.Order sırasında toplanması ve sıralı grupların gerçek token akışı.
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
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Services.Escalation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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

    private sealed class RecordingRunner : IWorkflowRunner
    {
        public List<RecordedCall> Calls { get; } = new();

        public Task<string> RunAsync(string query, List<ConversationMessage>? history,
            AgentSession? session, ReasoningResult? reasoning, CancellationToken ct)
        {
            lock (Calls)
                Calls.Add(new RecordedCall(query, history?.ToList() ?? [], reasoning));
            return Task.FromResult($"yanıt<{query}>");
        }

        public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(string query,
            List<ConversationMessage>? history, AgentSession? session, ReasoningResult? reasoning,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            var response = await RunAsync(query, history, session, reasoning, ct);
            yield return new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload(response));
        }
    }

    private sealed record RecordedCall(
        string Query,
        List<ConversationMessage> History,
        ReasoningResult? Reasoning);

    private sealed class ErrorStreamingRunner : IWorkflowRunner
    {
        public int Calls { get; private set; }

        public Task<string> RunAsync(string query, List<ConversationMessage>? history,
            AgentSession? session, ReasoningResult? reasoning, CancellationToken ct)
            => Task.FromResult("unused");

        public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(string query,
            List<ConversationMessage>? history, AgentSession? session, ReasoningResult? reasoning,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            Calls++;
            yield return new StreamEvent(StreamEventTypes.Error, new { message = "alt görev hatası" });
            yield return new StreamEvent(StreamEventTypes.ResponseComplete,
                new ResponseCompletePayload("bu event tüketilmemeli"));
            await Task.CompletedTask;
        }
    }

    private sealed class ThrowingRunner : IWorkflowRunner
    {
        public Task<string> RunAsync(string query, List<ConversationMessage>? history,
            AgentSession? session, ReasoningResult? reasoning, CancellationToken ct)
            => Task.FromException<string>(new InvalidOperationException("koşucu patladı"));

        public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(string query,
            List<ConversationMessage>? history, AgentSession? session, ReasoningResult? reasoning,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield return new StreamEvent(StreamEventTypes.ResponseStart, null);
            throw new InvalidOperationException("stream patladı");
        }
    }

    private static DecomposedRunner Build(IWorkflowRunner runner, ParallelExecutionOptions? opts = null)
        => new(runner,
               opts ?? new ParallelExecutionOptions(),
               Substitute.For<IUiHintEmitter>(),
               Substitute.For<IApprovalContextAccessor>(),
               BuildFinalizer());

    /// <summary>
    /// Gerçek <see cref="TurnFinalizer"/> — ama bellek/profil bağımlılıkları <c>null</c>.
    /// Bileşik turun tur bazlı yan etkileri (episodic + profil) böylece no-op olur; bu
    /// testlerin konusu alt görev orkestrasyonu, yan etkiler değil.
    /// </summary>
    private static TurnFinalizer BuildFinalizer()
    {
        var approvalOpts = Options.Create(new ApprovalOptions { Enabled = false });
        var sink = Substitute.For<IEscalationSink>();

        var approvalGate = new ApprovalGateService(
            Substitute.For<IApprovalQueue>(),
            approvalOpts,
            sink,
            Substitute.For<IApprovalContextAccessor>(),
            Substitute.For<ICustomerSupportToolsService>(),
            new EscalationPolicyService(sink, approvalOpts));

        return new TurnFinalizer(
            Substitute.For<IReasoningTraceStore>(),
            approvalGate,
            NullLoggerFactory.Instance,
            semanticMemory: null,
            profileService: null);
    }

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

    [Fact]
    public async Task IndependentSubTasks_DoNotReceiveCompoundQueryOrSiblingResults()
    {
        var runner = new RecordingRunner();
        var history = new List<ConversationMessage>
        {
            new(ConversationRoles.User, "önceki kullanıcı mesajı"),
            new(ConversationRoles.Assistant, "önceki yanıt")
        };
        var reasoning = Reasoning(
            Sub(1, "ürünleri göster", WellKnown.AgentNames.Product),
            Sub(2, "siparişi sorgula", WellKnown.AgentNames.Order));

        await Build(runner).RunDecomposedAsync(
            "gizli compound ana sorgu", history, null, reasoning, CancellationToken.None);

        runner.Calls.Should().HaveCount(2);
        runner.Calls.Should().OnlyContain(call =>
            call.History.Select(message => message.Text)
                .SequenceEqual(new[] { "önceki kullanıcı mesajı", "önceki yanıt" }));
        runner.Calls.Should().OnlyContain(call =>
            call.Reasoning!.ConstrainedTargetAgent == reasoning.SubTasks.Single(
                sub => SubTaskOrchestrator.FormatSubTaskQuery(sub) == call.Query).TargetAgent);
    }

    [Fact]
    public async Task DependentSubTask_ReceivesOnlyDeclaredDependencyResult()
    {
        var runner = new RecordingRunner();
        var first = Sub(1, "ürünleri göster", WellKnown.AgentNames.Product);
        var second = Sub(2, "siparişi sorgula", WellKnown.AgentNames.Order);
        second.Dependencies = [1];
        var third = Sub(3, "şikayet aç", WellKnown.AgentNames.Complaint);

        await Build(runner).RunDecomposedAsync(
            "compound", null, null, Reasoning(first, second, third), CancellationToken.None);

        var secondCall = runner.Calls.Single(call => call.Query.Contains("siparişi", StringComparison.Ordinal));
        secondCall.History.Should().HaveCount(2);
        secondCall.History[0].Text.Should().Contain("ürünleri göster");
        secondCall.History[1].Text.Should().Contain("yanıt<");

        var thirdCall = runner.Calls.Single(call => call.Query.Contains("şikayet", StringComparison.Ordinal));
        thirdCall.History.Should().BeEmpty("üçüncü görev hiçbir kardeşe bağımlı değil");
    }

    [Fact]
    public async Task SequentialSubTaskError_StopsWithoutDoneOrResponseComplete()
    {
        var runner = new ErrorStreamingRunner();
        var events = await CollectAsync(Build(runner), Reasoning(
            Sub(1, "siparişi sorgula", WellKnown.AgentNames.Order),
            Sub(2, "şikayet aç", WellKnown.AgentNames.Complaint)));

        runner.Calls.Should().Be(1, "ilk hata sonrası sonraki alt görev başlamamalı");
        events.Should().Contain(e => e.Type == StreamEventTypes.Error);
        events.Should().NotContain(e => e.Type == StreamEventTypes.ResponseComplete);
        events.Where(e => e.Type == StreamEventTypes.Agent)
            .Select(e => e.Data?.ToString())
            .Should().Contain(text => text!.Contains("failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidFanOut_StreamReturnsErrorBeforeStartingAnySubTask()
    {
        var runner = new FakeRunner();
        var options = new ParallelExecutionOptions { MaxSubTasks = 2 };
        var reasoning = Reasoning(
            Sub(1, "g1", WellKnown.AgentNames.Product),
            Sub(2, "g2", WellKnown.AgentNames.Order),
            Sub(3, "g3", WellKnown.AgentNames.Complaint));

        var events = await CollectAsync(Build(runner, options), reasoning);

        runner.Calls.Should().BeEmpty();
        events.Should().ContainSingle(e => e.Type == StreamEventTypes.Error);
        events.Should().NotContain(e => e.Type == StreamEventTypes.ResponseStart);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThrownSubTaskException_IsNormalizedWithoutResponseComplete(bool parallel)
    {
        var subs = parallel
            ? new[]
            {
                Sub(1, "ürün 1", WellKnown.AgentNames.Product),
                Sub(2, "ürün 2", WellKnown.AgentNames.Product)
            }
            : new[]
            {
                Sub(1, "sipariş", WellKnown.AgentNames.Order),
                Sub(2, "şikayet", WellKnown.AgentNames.Complaint)
            };

        var events = await CollectAsync(Build(new ThrowingRunner()), Reasoning(subs));

        events.Should().Contain(e => e.Type == StreamEventTypes.Error);
        events.Should().NotContain(e => e.Type == StreamEventTypes.ResponseComplete);
    }
}
