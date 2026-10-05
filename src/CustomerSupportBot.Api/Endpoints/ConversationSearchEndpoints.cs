// Endpoints/ConversationSearchEndpoints.cs
// Yönetici konuşma araması.
//   GET /conversations/search?q=&customerId=&from=&to=&reason=&tag=&page=   [Admin]
// from/to UTC anları (to hariç); gün sınırlarını panel tarayıcının yerel saatine göre hesaplar.
// Yetki ve hız sınırı çağıran grupta (Program.cs: adminScope).

using CustomerSupportBot.Application.Ports.Inbound;

namespace CustomerSupportBot.Api.Endpoints;

public static class ConversationSearchEndpoints
{
    public static IEndpointRouteBuilder MapConversationSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/conversations/search", async (
            IConversationSearchPort search, CancellationToken ct,
            string? q, string? customerId, DateTimeOffset? from, DateTimeOffset? to, string? reason, string? tag, int? page) =>
        {
            var result = await search.SearchAsync(new ConversationSearchQuery(
                q, customerId, from?.UtcDateTime, to?.UtcDateTime, reason, tag, page ?? 1), ct);
            return result.Error is null ? Results.Ok(result.Page) : Results.BadRequest(new { error = result.Error });
        });
        return app;
    }
}
