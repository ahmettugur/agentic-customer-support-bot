// Endpoints/ChatAttachmentEndpoints.cs
// Sohbete fotoğraf ekleme.
//   POST /chat/attachments          → yükle (multipart: file, sessionId?)   [Customer]
//   GET  /chat/attachments/{id}     → kendi fotoğrafım                      [Customer]
//   DELETE /chat/attachments/{id}   → gönderilmemiş fotoğrafımı kaldır      [Customer]
//   GET  /attachments/{id}          → onay kartındaki fotoğraf              [Admin]       (AdminEndpoints)
//   GET  /agent/attachments/{id}    → onay kartındaki fotoğraf              [AdminOrAgent] (AgentPanelEndpoints)

using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Domain.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Endpoints;

public static class ChatAttachmentEndpoints
{
    /// <summary>
    /// Gövde sınırı: dosya sınırı + multipart çerçevesi için pay. Asıl boyut kontrolü servistedir
    /// (yapılandırılabilir); bu sınır yalnızca devasa gövdelerin belleğe alınmasını engeller.
    /// </summary>
    private const long MaxRequestBodyBytes = 12 * 1024 * 1024;

    public static IEndpointRouteBuilder MapChatAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/chat/attachments", HandleUploadAsync)
            .RequireRateLimiting("chat")
            .RequireAuthorization("Customer")
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodyBytes));
        app.MapGet("/chat/attachments/{id}", HandleGetOwnAsync)
            .RequireRateLimiting("general")
            .RequireAuthorization("Customer");
        app.MapDelete("/chat/attachments/{id}", HandleDeleteUnsentAsync)
            .RequireRateLimiting("general")
            .RequireAuthorization("Customer");
        return app;
    }

    /// <summary>
    /// Form elle okunur (<c>IFormFile</c> parametresi yerine): parametre bağlama antiforgery
    /// meta verisi ister; bu uç tarayıcı çerezini değil Bearer token'ı kullanır.
    /// </summary>
    private static async Task<IResult> HandleUploadAsync(
        HttpContext httpContext,
        IChatAttachmentPort attachments,
        IOptions<AttachmentOptions> options,
        ILoggerFactory loggerFactory)
    {
        var ct = httpContext.RequestAborted;
        if (!httpContext.Request.HasFormContentType)
            return Error(StatusCodes.Status400BadRequest, "invalid_form", "Fotoğraf multipart/form-data olarak gönderilmeli.");

        IFormCollection form;
        try { form = await httpContext.Request.ReadFormAsync(ct); }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
        {
            return Error(StatusCodes.Status400BadRequest, "too_large",
                $"Fotoğraf en fazla {options.Value.MaxBytes / (1024 * 1024)} MB olabilir.");
        }

        var file = form.Files.GetFile("file");
        if (file is null)
            return Error(StatusCodes.Status400BadRequest, "empty", "Fotoğraf seçilmedi.");

        // Boyut servis tarafından da kontrol edilir; burada sınırı aşan dosya belleğe hiç alınmaz.
        if (file.Length > options.Value.MaxBytes)
            return Error(StatusCodes.Status400BadRequest, "too_large",
                $"Fotoğraf en fazla {options.Value.MaxBytes / (1024 * 1024)} MB olabilir.");

        byte[] data;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await stream.CopyToAsync(buffer, ct);
            data = buffer.ToArray();
        }

        var sessionId = form["sessionId"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sessionId)) sessionId = null;

        var customerId = httpContext.User.FindFirst("linked_customer_id")?.Value;
        var result = await attachments.UploadAsync(sessionId, customerId, data, ct);

        if (result.Status != AttachmentUploadStatus.Ok)
        {
            loggerFactory.CreateLogger("ChatAttachmentEndpoints").LogInformation(
                "Fotoğraf reddedildi | status={Status} session={Session}", result.Status, sessionId);
        }

        return result.Status switch
        {
            AttachmentUploadStatus.Ok => Results.Ok(new
            {
                attachmentId = result.AttachmentId,
                sessionId = result.SessionId,
                description = result.Description
            }),
            AttachmentUploadStatus.InvalidSession => Error(StatusCodes.Status400BadRequest,
                SessionIdPolicy.ErrorCode, SessionIdPolicy.ErrorMessage),
            AttachmentUploadStatus.Forbidden => Error(StatusCodes.Status403Forbidden,
                "session_forbidden", result.Error ?? "Bu oturuma erişim yetkiniz yok."),
            _ => Error(StatusCodes.Status400BadRequest, ErrorCode(result.Status), result.Error ?? "Fotoğraf yüklenemedi.")
        };
    }

    private static async Task<IResult> HandleGetOwnAsync(string id, HttpContext httpContext, IChatAttachmentPort attachments)
    {
        var customerId = httpContext.User.FindFirst("linked_customer_id")?.Value;
        if (string.IsNullOrWhiteSpace(customerId)) return Results.NotFound();

        var attachment = await attachments.GetAsync(id, customerId, httpContext.RequestAborted);
        return attachment is null ? Results.NotFound() : ImageResult(httpContext, attachment);
    }

    /// <summary>
    /// Önizlemeden kaldırılan fotoğraf sunucuda da silinir. Gönderilmiş (geçmişte görünen, onaya
    /// bağlanmış olabilecek) fotoğraf silinmez → 404.
    /// </summary>
    private static async Task<IResult> HandleDeleteUnsentAsync(string id, HttpContext httpContext, IChatAttachmentPort attachments)
    {
        var customerId = httpContext.User.FindFirst("linked_customer_id")?.Value;
        if (string.IsNullOrWhiteSpace(customerId)) return Results.NotFound();

        return await attachments.DeleteUnsentAsync(id, customerId, httpContext.RequestAborted)
            ? Results.NoContent()
            : Results.NotFound();
    }

    /// <summary>Personel uçları (admin ve temsilci) — sahiplik kontrolü yok; yetki grup düzeyinde.</summary>
    internal static async Task<IResult> HandleStaffGetAsync(string id, HttpContext httpContext, IChatAttachmentPort attachments)
    {
        var attachment = await attachments.GetAsync(id, customerId: null, httpContext.RequestAborted);
        return attachment is null ? Results.NotFound() : ImageResult(httpContext, attachment);
    }

    /// <summary>
    /// İçerik türü kayıttan gelir (dosya imzasıyla belirlenmiş: yalnızca JPEG/PNG). <c>nosniff</c>
    /// tarayıcının içeriği başka bir türmüş gibi yorumlamasını engeller; yanıt kişisel veri
    /// içerebileceği için ara önbelleklerde tutulmaz.
    /// </summary>
    private static IResult ImageResult(HttpContext httpContext, ChatAttachment attachment)
    {
        httpContext.Response.Headers.XContentTypeOptions = "nosniff";
        httpContext.Response.Headers.CacheControl = "private, max-age=300";
        return Results.File(attachment.Data, attachment.ContentType);
    }

    private static string ErrorCode(AttachmentUploadStatus status) => status switch
    {
        AttachmentUploadStatus.Disabled => "attachments_disabled",
        AttachmentUploadStatus.Empty => "empty",
        AttachmentUploadStatus.TooLarge => "too_large",
        AttachmentUploadStatus.UnsupportedType => "unsupported_type",
        AttachmentUploadStatus.TooManyInSession => "too_many_in_session",
        _ => "upload_failed"
    };

    private static IResult Error(int statusCode, string error, string message) =>
        Results.Json(new { error, message }, statusCode: statusCode);
}
