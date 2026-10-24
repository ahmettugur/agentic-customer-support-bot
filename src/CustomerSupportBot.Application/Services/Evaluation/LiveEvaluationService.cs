// Application/Services/Evaluation/LiveEvaluationService.cs
// Canlı değerlendirme: üretim yanıtlarından örnek alıp LLM-hakemle puanlar; puanlar prompt seti sürümüyle etiketli metrik olur.

using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Evaluation;

public sealed class LiveEvaluationOptions
{
    public const string SectionName = "LiveEvaluation";

    /// <summary>Her örnek ek bir LLM çağrısıdır (maliyet): KAPALI başlar.</summary>
    public bool Enabled { get; set; }

    /// <summary>Puanlanacak tur oranı (0–1). Önerilen: 0.05–0.2.</summary>
    public double SampleRate { get; set; } = 0.1;

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Aynı anda en çok bu kadar hakem çağrısı; doluysa örnek atlanır (sohbet yolunu yavaşlatmaz).</summary>
    public int MaxConcurrent { get; set; } = 2;
}

public sealed record JudgeScores(int Helpfulness, int Groundedness, int Tone, string? Issue);

public static class JudgeVerdictParser
{
    public const int MaxIssueLength = 300;

    public static JudgeScores? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        try
        {
            using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!TryScore(root, "helpfulness", out var h) || !TryScore(root, "groundedness", out var g) || !TryScore(root, "tone", out var t))
                return null;

            var issue = root.TryGetProperty("issue", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString()?.Trim() : null;
            if (issue is { Length: > MaxIssueLength }) issue = issue[..MaxIssueLength];
            return new JudgeScores(h, g, t, string.IsNullOrEmpty(issue) ? null : issue);
        }
        catch (JsonException) { return null; }
    }

    private static bool TryScore(JsonElement root, string name, out int score)
    {
        score = 0;
        if (!root.TryGetProperty(name, out var e) || e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out var v)) return false;
        score = Math.Clamp(v, 1, 5);
        return true;
    }
}

public static class LiveEvaluationSampler
{
    /// <summary>Anahtardan türetilen sabit bir sayıyla karar verir: aynı tur her pod'da aynı sonucu alır, test edilebilir.</summary>
    public static bool ShouldSample(double rate, string key)
    {
        if (rate <= 0) return false;
        if (rate >= 1) return true;
        var h = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return BitConverter.ToUInt32(h, 0) / (double)uint.MaxValue < rate;
    }
}

public sealed class LiveEvaluationService
{
    /// <summary><c>Prompts/services/live-judge.md</c>.</summary>
    public const string PromptKey = "services/live-judge";

    private static readonly Meter Meter = new(TelemetryConstants.MeterName, "1.0.0");
    private static readonly Histogram<double> ExperimentHistogram = Meter.CreateHistogram<double>(
        "chat.live_eval.experiment_score", unit: "{score}", description: "Canlı LLM-hakem puanı 1-5 (experiment, arm, dimension).");

    private static readonly Histogram<double> ScoreHistogram = Meter.CreateHistogram<double>(
        "chat.live_eval.score", unit: "{score}", description: "Canlı LLM-hakem puanı 1-5 (dimension, prompt_set).");

    private readonly IGeneralChatClient _llm;
    private readonly IPromptRepository _prompts;
    private readonly IContextSanitizer _sanitizer;
    private readonly LiveEvaluationOptions _options;
    private readonly ILogger<LiveEvaluationService> _logger;
    private readonly SemaphoreSlim _slots;
    private readonly ExperimentOptions _experiments;

    public LiveEvaluationService(
        IGeneralChatClient llm, IPromptRepository prompts, IContextSanitizer sanitizer,
        IOptions<LiveEvaluationOptions> options, ILogger<LiveEvaluationService> logger,
        IOptions<ExperimentOptions>? experiments = null)
    {
        _experiments = experiments?.Value ?? new ExperimentOptions();
        _llm = llm;
        _prompts = prompts;
        _sanitizer = sanitizer;
        _options = options.Value;
        _logger = logger;
        _slots = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrent));
    }

    /// <summary>
    /// Sohbet yolunu BEKLETMEDEN: örneklendiyse ve hakem yuvası boşsa arka planda puanlar. Hata ya da dolu yuva sessizce atlanır.
    /// </summary>
    public void Observe(string sessionId, int turn, string query, string answer)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(answer)) return;
        if (!LiveEvaluationSampler.ShouldSample(_options.SampleRate, $"{sessionId}:{turn}")) return;
        if (!_slots.Wait(0)) return;

        _ = Task.Run(async () =>
        {
            try { await EvaluateAsync(sessionId, turn, query, answer, CancellationToken.None); }
            finally { _slots.Release(); }
        });
    }

    public async Task<JudgeScores?> EvaluateAsync(string sessionId, int turn, string query, string answer, CancellationToken ct)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(answer)) return null;
        if (!LiveEvaluationSampler.ShouldSample(_options.SampleRate, $"{sessionId}:{turn}")) return null;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));

            var user = $"MÜŞTERİ MESAJI:\n{_sanitizer.WrapRetrieved(query, "customer_message")}\n\nBOT YANITI:\n{_sanitizer.WrapRetrieved(answer, "bot_answer")}";
            var raw = await _llm.CompleteAsync(
            [
                new ConversationMessage(ConversationRoles.System, _prompts.Get(PromptKey)),
                new ConversationMessage(ConversationRoles.User, user)
            ], timeout.Token);

            var scores = JudgeVerdictParser.Parse(raw);
            if (scores is null)
            {
                _logger.LogDebug("[LiveEval] Hakem çıktısı ayrıştırılamadı | session={SessionId}", sessionId);
                return null;
            }

            var promptSet = _prompts.SetVersion;
            Record("helpfulness", scores.Helpfulness, promptSet);
            Record("groundedness", scores.Groundedness, promptSet);
            Record("tone", scores.Tone, promptSet);
            RecordExperiments(sessionId, scores);

            if (scores.Helpfulness <= 2 || scores.Groundedness <= 2)
                _logger.LogWarning("[LiveEval] Düşük puanlı yanıt | session={SessionId} turn={Turn} helpfulness={H} groundedness={G} issue={Issue}",
                    sessionId, turn, scores.Helpfulness, scores.Groundedness, scores.Issue);
            return scores;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "[LiveEval] Puanlama başarısız | session={SessionId}", sessionId);
            return null;
        }
    }

    private void RecordExperiments(string sessionId, JudgeScores scores)
    {
        if (!_experiments.Enabled) return;
        foreach (var experiment in _experiments.Effective)
        {
            var arm = ExperimentAssigner.Assign(sessionId, experiment);
            if (arm is null) continue;
            foreach (var (dimension, value) in new[] { ("helpfulness", scores.Helpfulness), ("groundedness", scores.Groundedness), ("tone", scores.Tone) })
                ExperimentHistogram.Record(value, new KeyValuePair<string, object?>("experiment", experiment.Name),
                    new KeyValuePair<string, object?>("arm", arm.Name), new KeyValuePair<string, object?>("dimension", dimension));
        }
    }

    private static void Record(string dimension, int value, string promptSet) =>
        ScoreHistogram.Record(value, new KeyValuePair<string, object?>("dimension", dimension),
            new KeyValuePair<string, object?>("prompt_set", promptSet));
}
