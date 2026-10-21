// Api/Endpoints/McpEndpoints.cs
// MCP (Model Context Protocol) sunucusu — Streamable HTTP, salt-okunur.
//
//   /mcp/catalog   Partner token'ı            ürün kataloğu tool'ları (müşteri kimliği gerektirmez)
//   /mcp/customer  A2ASubject token'ı         sipariş/şikayet sorguları — TEK müşteriye kilitli
//
// A2A ile aynı yetki ayrımı: "hangi sistem" (Partner) ile "hangi müşteri" (özne token'ı) ayrı token'larda kalır.
// Müşteri kimliği tool argümanından DEĞİL token'dan gelir ve ambient bağlama kurulur; tool'lar oradan okur.

using System.Security.Claims;
using CustomerSupportBot.Adapters.Agents.Mcp;
using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.A2A;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Application.Services.Mcp;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;

namespace CustomerSupportBot.Api.Endpoints;

public static class McpEndpoints
{
    public const string CatalogPath = "/mcp/catalog";
    public const string CustomerPath = "/mcp/customer";

    public static IEndpointRouteBuilder MapMcpChannel(this IEndpointRouteBuilder app)
    {
        var maxBytes = Math.Max(1024, app.ServiceProvider.GetRequiredService<IOptions<McpOptions>>().Value.MaxRequestBytes);

        // "mcp" hız sınırı: a2a ile AYNI limit, ama ayrı kova (bir kanalın yoğunluğu diğerinin kotasını tüketmesin).
        app.MapMcp(CatalogPath)
            .RequireAuthorization("Partner")
            .RequireRateLimiting("mcp")
            .AddEndpointFilter(new PartnerChannelFilter(PartnerChannels.Mcp))
            .AddEndpointFilter(new McpGuardFilter("catalog", maxBytes, customerScoped: false));

        app.MapMcp(CustomerPath)
            .RequireAuthorization("A2ASubject")
            .RequireRateLimiting("mcp")
            .AddEndpointFilter(new PartnerChannelFilter(PartnerChannels.Mcp))
            .AddEndpointFilter(new McpGuardFilter("customer", maxBytes, customerScoped: true));

        return app;
    }

    /// <summary>Yola göre tool kümesi. Bilinmeyen yol BOŞ küme alır — yanlış yapılandırma veri açmaz.</summary>
    internal static IReadOnlyList<AIFunction> ToolSetFor(PathString path, McpToolCatalog catalog)
    {
        var p = path.Value?.TrimEnd('/');
        if (string.Equals(p, CatalogPath, StringComparison.OrdinalIgnoreCase)) return catalog.Catalog;
        if (string.Equals(p, CustomerPath, StringComparison.OrdinalIgnoreCase)) return catalog.Customer;
        return [];
    }

    private sealed class McpGuardFilter(string endpoint, int maxBytes, bool customerScoped) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var http = context.HttpContext;
            var log = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("CustomerSupportBot.Mcp");

            // Gövde üst sınırı, gövde okunmadan ÖNCE kurulur (yetkisiz istek buraya gelmez; yetkilendirme önce çalışır).
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeLimit)
                sizeLimit.MaxRequestBodySize = maxBytes;

            var subjectId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var partnerId = A2ASubjectIdentity.TryGetPartnerId(subjectId) ?? subjectId;

            if (!customerScoped)
            {
                log.LogInformation("[MCP] {Endpoint} ← partner={Partner}", endpoint, partnerId);
                return await next(context);
            }

            var customerId = http.User.FindFirst("linked_customer_id")?.Value;
            if (string.IsNullOrWhiteSpace(customerId))
            {
                // Dışarıya sessiz 403 (kimlik numaralandırmayı kolaylaştırmaz); gerçek sebep operatör için loglanır.
                log.LogWarning("[MCP] {Endpoint} REDDEDİLDİ · partner={Partner} · sebep=linked_customer_id claim'i yok",
                    endpoint, partnerId);
                return Results.Forbid();
            }

            log.LogInformation("[MCP] {Endpoint} ← partner={Partner} customer={Customer}", endpoint, partnerId, customerId);

            // Kapsam, MCP isteğinin TAMAMINI sarar: stateless modda tool, bu isteğin içinde başlatılan görevde çalışır ve
            // AsyncLocal kapsamı oradan miras alır. İstek bitince kapsam kapanır; sonraki istek başka müşteriyle kurulur.
            var accessor = http.RequestServices.GetRequiredService<IApprovalContextAccessor>();
            using var scope = accessor.SetScope(sessionId: null, traceId: null, userQuery: null, customerId: customerId);
            return await next(context);
        }
    }
}
