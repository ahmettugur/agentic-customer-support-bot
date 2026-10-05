// Endpoints/DataPrivacyEndpoints.cs
// Kişisel veri (KVKK): dışa aktarma ve silme.
//   GET    /customer/data/export                 → kendi verim (JSON dosyası)      [Customer]
//   DELETE /customer/data?confirm=true            → kendi verimi sil               [Customer]
//   GET    /customers/{customerId}/data/export    → bir müşterinin verisi          [Admin]
//   DELETE /customers/{customerId}/data?confirm=true → bir müşterinin verisini sil [Admin]

using System.Security.Claims;
using CustomerSupportBot.Application.Ports.Inbound;

namespace CustomerSupportBot.Api.Endpoints;

public static class DataPrivacyEndpoints
{
    /// <summary>Müşterinin kendi verisi — kimlik her zaman token'dan, asla yoldan/gövdeden.</summary>
    public static IEndpointRouteBuilder MapCustomerDataPrivacyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/customer/data/export", async (HttpContext http, IDataPrivacyPort privacy) =>
            CustomerIdOf(http) is { } id ? await ExportAsync(id, privacy, http.RequestAborted) : NoLinkedCustomer())
            .RequireAuthorization("Customer").RequireRateLimiting("general");

        app.MapDelete("/customer/data", async (HttpContext http, IDataPrivacyPort privacy, ILoggerFactory logs, bool? confirm) =>
            CustomerIdOf(http) is { } id
                ? await EraseAsync(id, confirm, privacy, logs, requestedBy: "customer", http.RequestAborted)
                : NoLinkedCustomer())
            .RequireAuthorization("Customer").RequireRateLimiting("general");
        return app;
    }

    /// <summary>Yönetici uçları — çağıran grup "Admin" yetkisini ve hız sınırını uygular (Program.cs).</summary>
    public static IEndpointRouteBuilder MapAdminDataPrivacyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/customers/{customerId}/data/export", async (string customerId, HttpContext http, IDataPrivacyPort privacy) =>
            await ExportAsync(customerId, privacy, http.RequestAborted));

        app.MapDelete("/customers/{customerId}/data",
            async (string customerId, HttpContext http, IDataPrivacyPort privacy, ILoggerFactory logs, bool? confirm) =>
                await EraseAsync(customerId, confirm, privacy, logs,
                    requestedBy: "admin:" + (http.User.FindFirst(ClaimTypes.Name)?.Value ?? "?"), http.RequestAborted));
        return app;
    }

    private static async Task<IResult> ExportAsync(string customerId, IDataPrivacyPort privacy, CancellationToken ct)
    {
        var export = await privacy.ExportCustomerDataAsync(customerId, ct);
        // Dosya olarak indirilir; ara önbelleklerde tutulmaz (kişisel veri).
        return new DownloadResult(Results.Json(export), $"kisisel-verilerim-{customerId}-{DateTime.UtcNow:yyyyMMdd}.json");
    }

    /// <summary>
    /// Silme geri alınamaz: <c>confirm=true</c> olmadan hiçbir şey silinmez. Kısmi hatada 500 ve başarısız
    /// depoların adları döner; işlem güvenle tekrarlanabilir.
    /// </summary>
    private static async Task<IResult> EraseAsync(
        string customerId, bool? confirm, IDataPrivacyPort privacy, ILoggerFactory logs, string requestedBy, CancellationToken ct)
    {
        if (confirm != true)
            return Results.Json(new
            {
                error = "confirmation_required",
                message = "Silme geri alınamaz; onaylamak için confirm=true gönderin."
            }, statusCode: StatusCodes.Status400BadRequest);

        var logger = logs.CreateLogger("DataPrivacyEndpoints");
        try
        {
            var result = await privacy.EraseCustomerDataAsync(customerId, ct);
            // Denetim izi: kim, kimin verisini sildi. İçerik loglanmaz.
            logger.LogWarning("[Privacy] Kişisel veri silindi | customer={CustomerId} by={RequestedBy} sessions={Sessions}",
                customerId, requestedBy, result.SessionsErased);
            return Results.Ok(result);
        }
        catch (DataErasureException ex)
        {
            logger.LogError(ex, "[Privacy] Silme kısmen başarısız | customer={CustomerId} by={RequestedBy}", customerId, requestedBy);
            return Results.Json(new
            {
                error = "erasure_incomplete",
                message = "Verilerin bir kısmı silinemedi; işlemi tekrar deneyin.",
                failedStores = ex.FailedStores
            }, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static string? CustomerIdOf(HttpContext http) =>
        http.User.FindFirst("linked_customer_id")?.Value is { Length: > 0 } id ? id : null;

    private static IResult NoLinkedCustomer() =>
        Results.Json(new { error = "no_linked_customer", message = "Hesabınız bir müşteri kaydına bağlı değil." },
            statusCode: StatusCodes.Status400BadRequest);

    /// <summary>JSON yanıtı ek olarak indirilecek dosya biçiminde döner.</summary>
    private sealed class DownloadResult(IResult inner, string fileName) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
            httpContext.Response.Headers.CacheControl = "no-store";
            return inner.ExecuteAsync(httpContext);
        }
    }
}
