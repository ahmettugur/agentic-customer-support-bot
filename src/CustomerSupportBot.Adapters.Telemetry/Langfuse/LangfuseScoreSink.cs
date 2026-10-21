// Adapters.Telemetry/Langfuse/LangfuseScoreSink.cs
// Müşteri puanını Langfuse'a oturum bazlı NUMERIC skor olarak yazar (POST /api/public/scores).
// Oturum kimliği, LLM span'lerine zaten "session.id" olarak yazılan kimliktir; Langfuse skoru o oturuma bağlar.

using System.Net.Http.Json;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Telemetry.Langfuse;

public sealed class LangfuseScoreSink : IConversationScoreSink
{
    public const string RatingScoreName = "customer_rating";

    /// <summary>Oturumun puan skorunun Langfuse kimliği (veri silmede de aynı kimlik kullanılır).</summary>
    public static string ScoreId(string sessionId) => $"{RatingScoreName}:{sessionId}";

    private readonly HttpClient _http;
    private readonly LangfuseOptions _options;
    private readonly ILogger<LangfuseScoreSink> _logger;

    public LangfuseScoreSink(HttpClient http, LangfuseOptions options, ILogger<LangfuseScoreSink> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task PublishRatingAsync(ConversationRating rating, CancellationToken ct = default)
    {
        // Skor kimliği oturuma sabit: aynı oturumun yeniden puanlanması Langfuse'ta skoru günceller, çoğaltmaz.
        using var request = LangfuseRequests.Create(HttpMethod.Post, _options, "/api/public/scores", JsonContent.Create(new
        {
            id = ScoreId(rating.SessionId),
            sessionId = rating.SessionId,
            name = RatingScoreName,
            value = rating.Stars,
            dataType = "NUMERIC",
            // Langfuse aynı kimlikli skoru birleştirir: alan atlanırsa önceki yorum kalır. Yorum yoksa açıkça null.
            comment = string.IsNullOrWhiteSpace(rating.Feedback) ? null : rating.Feedback
        }));

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            _logger.LogWarning("Langfuse skor gönderimi başarısız: {Status} (session={SessionId})",
                (int)response.StatusCode, rating.SessionId);
    }

    public const string MessageFeedbackScoreName = "message_feedback";

    public static string MessageFeedbackScoreId(string sessionId, string messageHash) =>
        $"{MessageFeedbackScoreName}:{sessionId}:{messageHash}";

    /// <summary>
    /// 👍/👎 oturum skoru olarak yazılır (BOOLEAN 1/0). Yorum (serbest metin, kişisel veri olabilir) Langfuse'a GİTMEZ;
    /// yalnızca sabit neden kodu gider. Kimlik oturum+mesaj özetidir: oyu değiştirmek skoru günceller.
    /// </summary>
    public async Task PublishMessageFeedbackAsync(MessageFeedback feedback, CancellationToken ct = default)
    {
        using var request = LangfuseRequests.Create(HttpMethod.Post, _options, "/api/public/scores", JsonContent.Create(new
        {
            id = MessageFeedbackScoreId(feedback.SessionId, feedback.MessageHash),
            sessionId = feedback.SessionId,
            name = MessageFeedbackScoreName,
            value = feedback.Helpful ? 1 : 0,
            dataType = "BOOLEAN",
            // Langfuse aynı kimlikli skoru birleştirir: neden yoksa eski neden temizlensin diye açıkça null.
            comment = feedback.Helpful ? null : feedback.Reason
        }));

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            _logger.LogWarning("Langfuse mesaj geri bildirimi başarısız: {Status} (session={SessionId})",
                (int)response.StatusCode, feedback.SessionId);
    }

    public async Task RemoveMessageFeedbackAsync(string sessionId, string messageHash, CancellationToken ct = default)
    {
        using var request = LangfuseRequests.Create(HttpMethod.Delete, _options,
            $"/api/public/scores/{Uri.EscapeDataString(MessageFeedbackScoreId(sessionId, messageHash))}");
        using var response = await _http.SendAsync(request, ct);
        // Olmayan skoru silmek hata değildir (Langfuse 202/404 döner); gerçek hata KVKK silmesinde görünür olmalı.
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
    }
}
