// Endpoints/InboundEmailEndpoints.cs
// Gelen e-posta webhook'ları (kanal sürekliliği).
//   POST /channels/email/inbound            normalize JSON  { from, subject, text, messageId, authenticated, autoSubmitted? }
//   POST /channels/email/inbound/postmark   Postmark inbound JSON (ekler dahil)
//   POST /channels/email/inbound/sendgrid   SendGrid Inbound Parse (multipart/form-data, ekler dahil)
// Kimlik: X-Inbound-Secret başlığı YA DA HTTP Basic parolası (Postmark/SendGrid webhook URL'sine kullanıcı:parola gömülebilir; başlık koyamazlar).
// Sağlayıcı uçlarında "authenticated" yükten DEĞİL, gönderenle hizalı DKIM/SPF sonucundan türetilir (bkz. SenderAuthentication).
// Yanıt: kapalıyken 404 (varlığı duyurulmaz), gizli yanlışsa 401, işlendiyse/yok sayıldıysa 202 — sonuç GÖVDEDE VERİLMEZ
// (aksi halde uç, bir adresin müşteri olup olmadığını sızdıran bir sorgu olurdu). Tur hatasında 500: sağlayıcı yeniden dener.

using System.Security.Cryptography;
using System.Text.Json;
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
                if (Gate(options.Value, request) is { } denied) return denied;
                if (string.IsNullOrWhiteSpace(body.From)) return Results.BadRequest(new { error = "from zorunludur." });

                await service.HandleAsync(
                    new InboundEmail(body.From, body.Subject, body.Text, body.MessageId, body.Authenticated, body.AutoSubmitted), ct);
                return Results.Accepted();
            })
            .AllowAnonymous().RequireRateLimiting("general").WithTags("Channels");

        app.MapPost("/channels/email/inbound/postmark",
            async (JsonElement payload, HttpRequest request, InboundEmailService service,
                   IOptions<InboundEmailOptions> options, CancellationToken ct) =>
            {
                if (Gate(options.Value, request) is { } denied) return denied;
                if (PostmarkInbound.Parse(payload) is not { } mail) return Results.BadRequest(new { error = "Geçersiz Postmark yükü." });

                await service.HandleAsync(mail, ct);
                return Results.Accepted();
            })
            .AllowAnonymous().RequireRateLimiting("general").WithTags("Channels");

        app.MapPost("/channels/email/inbound/sendgrid",
            async (HttpRequest request, InboundEmailService service, IOptions<InboundEmailOptions> options, CancellationToken ct) =>
            {
                if (Gate(options.Value, request) is { } denied) return denied;
                if (!request.HasFormContentType) return Results.BadRequest(new { error = "multipart/form-data bekleniyor." });

                var form = await request.ReadFormAsync(ct);
                var fields = form.ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.Ordinal);
                var files = new List<InboundAttachment>();
                foreach (var f in form.Files)
                {
                    await using var stream = f.OpenReadStream();
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms, ct);
                    files.Add(new InboundAttachment(f.FileName, f.ContentType, ms.ToArray()));
                }

                if (SendGridInbound.Parse(fields, files) is not { } mail) return Results.BadRequest(new { error = "Geçersiz SendGrid yükü." });
                await service.HandleAsync(mail, ct);
                return Results.Accepted();
            })
            .AllowAnonymous().RequireRateLimiting("general").WithTags("Channels");

        return app;
    }

    /// <summary>Kapalıyken 404 (varlığı duyurulmaz), gizli yanlış/eksikse 401; geçerse <c>null</c>.</summary>
    private static IResult? Gate(InboundEmailOptions o, HttpRequest request)
    {
        if (!o.Enabled) return Results.NotFound();
        var provided = request.Headers[SecretHeader].ToString();
        if (string.IsNullOrEmpty(provided)) provided = BasicPassword(request.Headers.Authorization.ToString());
        return SecretMatches(o.Secret, provided) ? null : Results.Unauthorized();
    }

    private static string BasicPassword(string authorization)
    {
        if (!authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return "";
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authorization[6..].Trim()));
            var colon = decoded.IndexOf(':');
            return colon < 0 ? "" : decoded[(colon + 1)..];
        }
        catch (FormatException) { return ""; }
    }

    /// <summary>Sabit zamanlı karşılaştırma; gizli yapılandırılmamışsa HİÇBİR istek geçmez.</summary>
    private static bool SecretMatches(string? expected, string provided)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided));
    }
}
