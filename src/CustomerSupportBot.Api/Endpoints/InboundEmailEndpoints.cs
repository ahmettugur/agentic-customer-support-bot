// Endpoints/InboundEmailEndpoints.cs
// Gelen e-posta webhook'u (kanal sürekliliği). Bir "inbound parse" sağlayıcısı (Mailgun/SendGrid/Postmark…) ya da küçük bir köprü,
// e-postayı bu normalize biçimde POST eder:
//   POST /channels/email/inbound   X-Inbound-Secret: <InboundEmail:Secret>
//   { from, subject, text, messageId, authenticated, autoSubmitted? }
// Yanıt: kapalıyken 404 (varlığı duyurulmaz), gizli yanlışsa 401, işlendiyse/yok sayıldıysa 202 — sonuç GÖVDEDE VERİLMEZ
// (aksi halde uç, bir adresin müşteri olup olmadığını sızdıran bir sorgu olurdu). Tur hatasında 500: sağlayıcı yeniden dener.

using System.Security.Cryptography;
using System.Text;
using CustomerSupportBot.Application.Services.Channels;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Endpoints;

public static class InboundEmailEndpoints
{
    public const string SecretHeader = "X-Inbound-Secret";

    public sealed record InboundEmailBody(string? From, string? Subject, string? Text, string? MessageId, bool Authenticated, string? AutoSubmitted);

    public static IEndpointRouteBuilder MapInboundEmailEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/channels/email/inbound",
            async (InboundEmailBody body, HttpRequest request, InboundEmailService service,
                   IOptions<InboundEmailOptions> options, CancellationToken ct) =>
            {
                var o = options.Value;
                if (!o.Enabled) return Results.NotFound();
                if (!SecretMatches(o.Secret, request.Headers[SecretHeader].ToString())) return Results.Unauthorized();
                if (string.IsNullOrWhiteSpace(body.From)) return Results.BadRequest(new { error = "from zorunludur." });

                await service.HandleAsync(
                    new InboundEmail(body.From, body.Subject, body.Text, body.MessageId, body.Authenticated, body.AutoSubmitted), ct);
                return Results.Accepted();
            })
            .AllowAnonymous()
            .RequireRateLimiting("general")
            .WithTags("Channels");

        return app;
    }

    /// <summary>Sabit zamanlı karşılaştırma; gizli yapılandırılmamışsa HİÇBİR istek geçmez.</summary>
    private static bool SecretMatches(string? expected, string provided)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided));
    }
}
