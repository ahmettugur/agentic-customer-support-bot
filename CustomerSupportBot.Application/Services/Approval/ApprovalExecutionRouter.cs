// Services/Approval/ApprovalExecutionRouter.cs
// ToolName -> ICustomerSupportToolsService yönlendirmesi. ApprovalRequest.Parameters,
// ApprovalGateService tarafında Dictionary<string,object?> olarak yazılır ama Postgres'ten
// hydrate edildiğinde (JSON round-trip) değerler JsonElement olarak gelir — GetString/GetLines
// her iki kaynağı da (canlı obje veya JsonElement) doğru okur.

using System.Text.Json;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Approval;

public sealed class ApprovalExecutionRouter : IApprovalExecutionRouter
{
    private readonly ICustomerSupportToolsService _tools;

    public ApprovalExecutionRouter(ICustomerSupportToolsService tools)
    {
        _tools = tools;
    }

    public Task<ApprovalExecutionOutcome> ExecuteAsync(ApprovalRequest request, CancellationToken ct = default)
    {
        var p = request.Parameters;

        ToolResult result = request.ToolName switch
        {
            WellKnown.ToolNames.OrderPlacement => _tools.OrderPlacementTool(
                GetLines(p, "lines"),
                GetString(p, "customerId") ?? ""),

            WellKnown.ToolNames.ComplaintRegistration => _tools.ComplaintRegistrationTool(
                GetString(p, "orderId") ?? "",
                GetString(p, "complaintText") ?? "",
                GetString(p, "customerId")),

            // customerId, Parameters sözlüğü yerine ApprovalRequest.CustomerId'den okunur —
            // bu, HITL kaydını oluşturan JWT-doğrulanmış kimliğin AYNI kanonik alanı (bkz.
            // ApprovalGateService.ExecuteWithApprovalGateAsync); Parameters'a ayrıca yazılmasına
            // gerek yok.
            WellKnown.ToolNames.OrderCancel => _tools.OrderCancelTool(
                GetString(p, "orderId") ?? "",
                GetString(p, "reason") ?? "",
                request.CustomerId ?? ""),

            WellKnown.ToolNames.ReturnRequest => _tools.ReturnRequestTool(
                GetString(p, "orderId") ?? "",
                GetString(p, "reason") ?? "",
                request.CustomerId ?? ""),

            _ => ToolResult.SystemError(
                "UNKNOWN_APPROVAL_TOOL",
                $"Bilinmeyen onay tool'u, yürütülemedi: {request.ToolName}")
        };

        return Task.FromResult(new ApprovalExecutionOutcome(result.Success, result.Message));
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null) return null;

        return value switch
        {
            JsonElement { ValueKind: JsonValueKind.Null } => null,
            JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
            JsonElement je => je.ToString(),
            string s => s,
            _ => value.ToString()
        };
    }

    /// <summary>
    /// Sipariş satırlarını okur. İki kaynak da desteklenir:
    /// canlı çağrıda değer bir <see cref="OrderLineRequest"/> dizisidir, Postgres'ten
    /// hydrate edilen onayda JSON round-trip sonrası bir <see cref="JsonElement"/> dizisidir.
    ///
    /// <para>
    /// Sözlük anahtarları <c>ApprovalGateService</c>'in yazdığı camelCase adlardır; JSON
    /// tarafında ise <see cref="OrderLineRequest"/>'in PascalCase özellik adları serileşir.
    /// Bu yüzden alan okuması büyük/küçük harfe duyarsızdır — biçim değişirse satırlar
    /// sessizce boş dönmemeli.
    /// </para>
    ///
    /// <para>
    /// Boş liste dönerse tool <c>ValidationError</c> ile reddeder; sessizce boş bir sipariş
    /// oluşmaz.
    /// </para>
    /// </summary>
    private static IReadOnlyList<OrderLineRequest> GetLines(
        IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null) return [];

        // Canlı çağrı — henüz JSON'a dönmemiş.
        if (value is IEnumerable<OrderLineRequest> live) return live.ToList();

        if (value is not JsonElement { ValueKind: JsonValueKind.Array } array) return [];

        var lines = new List<OrderLineRequest>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;

            var name = ReadProperty(element, "productName");
            var qty = ReadProperty(element, "quantity");

            if (name is not { ValueKind: JsonValueKind.String }) continue;

            var quantity = qty?.ValueKind switch
            {
                JsonValueKind.Number when qty.Value.TryGetInt32(out var n) => n,
                JsonValueKind.String when int.TryParse(qty.Value.GetString(), out var n) => n,
                _ => 0
            };

            lines.Add(new OrderLineRequest(name.Value.GetString() ?? "", quantity));
        }

        return lines;
    }

    private static JsonElement? ReadProperty(JsonElement element, string name)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                return prop.Value;
        }
        return null;
    }
}
