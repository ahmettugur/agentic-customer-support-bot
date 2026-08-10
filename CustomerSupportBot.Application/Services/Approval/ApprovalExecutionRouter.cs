// Services/Approval/ApprovalExecutionRouter.cs
// ToolName -> ICustomerSupportToolsService yönlendirmesi. ApprovalRequest.Parameters,
// ApprovalGateService tarafında Dictionary<string,object?> olarak yazılır ama Postgres'ten
// hydrate edildiğinde (JSON round-trip) değerler JsonElement olarak gelir — GetString/GetInt
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
                GetString(p, "productName") ?? "",
                GetInt(p, "quantity"),
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

    private static int? GetInt(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null) return null;

        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.Number when je.TryGetInt32(out var n) => n,
                JsonValueKind.String when int.TryParse(je.GetString(), out var n) => n,
                _ => null
            };
        }

        if (value is int i) return i;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }
}
