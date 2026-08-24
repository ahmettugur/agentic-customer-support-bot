// Api/Models/EndpointModels.cs
// Endpoint'lere özgü HTTP input DTO'ları.

namespace CustomerSupportBot.Api.Models;

// ─── AgentsEndpoints ───
public class RerouteInput
{
    public string? AgentId { get; set; }
    public string? Reason { get; set; }
}

// ─── ImprovementsEndpoints ───
public sealed record ImprovementDecision(string? DecidedBy, string? Reason);

// ─── PersonalizationEndpoints ───
public sealed class AdminNoteInput
{
    public string? Note { get; set; }
}

// ─── AnalyticsEndpoints ───
public sealed record RatingInput(int Stars, string? Feedback);

// ─── ChatEndpoints ───
/// <summary>
/// Chat isteğinin gövdeden bind edilen kısmı — kasıtlı olarak CustomerId İÇERMEZ.
/// Müşteri kimliği hiçbir zaman client body'sinden güvenilir olarak alınmaz, her zaman JWT
/// claim'inden okunur (bkz. ChatEndpoints.ResolveAuthenticatedCustomerId). Bu alanı
/// ChatRequest'te tutmak yerine ayrı bir wire DTO kullanmanın nedeni: aksi halde CustomerId
/// Swagger/OpenAPI şemasında görünür ve istemciye "bunu ben doldurabilirim" izlenimi verirdi —
/// oysa gönderilen değer sunucu tarafında her zaman sessizce göz ardı edilir.
/// </summary>
public sealed record ChatRequestBody(string Query, string? SessionId = null);
