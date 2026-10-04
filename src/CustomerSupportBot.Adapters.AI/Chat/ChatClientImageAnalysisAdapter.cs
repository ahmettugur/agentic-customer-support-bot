// Adapters.AI/Chat/ChatClientImageAnalysisAdapter.cs
// IImageAnalysisPort implementasyonu — fotoğrafı mevcut IChatClient'a görüntü içeriği olarak verir.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.AI.Chat;

/// <summary>
/// Sohbet modeline (görsel anlayan bir model olmalı — ör. gpt-4o/gpt-4.1/gpt-5 ailesi) talimat +
/// görüntü gönderip kısa bir açıklama alır. Talimat <c>Prompts/services/image-analysis.md</c>'de.
/// Görüntü bu noktaya gelmeden meta verisi silinmiştir (bkz. <c>ImageSanitizer</c>).
/// </summary>
public sealed class ChatClientImageAnalysisAdapter : IImageAnalysisPort
{
    public const string PromptKey = "services/image-analysis";

    private readonly IChatClient _client;
    private readonly IPromptRepository _prompts;

    public ChatClientImageAnalysisAdapter(IChatClient client, IPromptRepository prompts)
    {
        _client = client;
        _prompts = prompts;
    }

    public async Task<string> DescribeAsync(byte[] image, string contentType, CancellationToken ct = default)
    {
        try
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, _prompts.Get(PromptKey)),
                new(ChatRole.User, [
                    new TextContent("Müşterinin eklediği fotoğraf:"),
                    new DataContent(image, contentType)
                ])
            };
            var response = await _client.GetResponseAsync(messages, cancellationToken: ct);
            return response.Text?.Trim() ?? "";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ct.IsCancellationRequested is false)
        {
            throw ExceptionTranslator.Translate(ex, "ImageAnalysis.DescribeAsync başarısız.");
        }
    }
}
