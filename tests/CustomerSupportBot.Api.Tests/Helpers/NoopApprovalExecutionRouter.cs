using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Domain.Model;
// Tests/Helpers/NoopApprovalExecutionRouter.cs
// Test-only IApprovalExecutionRouter implementasyonu — onay kararı sonrası gerçek iş
// tetikleme mantığından (ICustomerSupportToolsService bağımlılığı) izole HITL testleri için.

namespace CustomerSupportBot.Api.Tests.Helpers;

public sealed class NoopApprovalExecutionRouter : IApprovalExecutionRouter
{
    public Task<ApprovalExecutionOutcome> ExecuteAsync(ApprovalRequest request, CancellationToken ct = default) =>
        Task.FromResult(new ApprovalExecutionOutcome(true, "test-executed"));
}
