// Adapters.Agents/ExceptionTranslator.cs
// Microsoft.Agents framework exception'larını domain exception'larına çevirir.

using CustomerSupportBot.Domain.Exceptions;

namespace CustomerSupportBot.Adapters.Agents;

/// <summary>
/// Agents framework exception'larını domain exception'larına çevirir.
/// </summary>
internal static class ExceptionTranslator
{
    public static DomainException Translate(Exception ex, string? context = null)
    {
        return ex switch
        {
            InvalidOperationException { Message: var m } when m.Contains("Workflow") =>
                new ExternalServiceException("AgentWorkflow",
                    context ?? $"Ajan workflow hatası: {m}", ex),

            TaskCanceledException { InnerException: TimeoutException } =>
                new ExternalServiceException("AgentWorkflow",
                    context ?? "Ajan workflow zaman aşımına uğradı.", ex),

            OperationCanceledException =>
                new ExternalServiceException("AgentWorkflow",
                    context ?? "Ajan workflow isteği iptal edildi.", ex),

            HttpRequestException =>
                new ExternalServiceException("AgentWorkflow",
                    context ?? "Ajan workflow'u sırasında bağlantı hatası.", ex),

            _ => new ExternalServiceException("AgentWorkflow",
                    context ?? $"Ajan workflow hatası: {ex.Message}", ex)
        };
    }
}
