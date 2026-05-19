using CustomerSupportBot.Application.Ports.Driven;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services;

/// <summary>
/// Kayıtlı tüm IContextProvider'ları sıralı çalıştırır ve
/// sonuçlarını birleştirerek tek bir bağlam metni üretir.
/// </summary>
public class ContextPipeline
{
    private readonly IEnumerable<IContextProvider> _providers;
    private readonly ILogger<ContextPipeline> _logger;

    public ContextPipeline(
        IEnumerable<IContextProvider> providers,
        ILogger<ContextPipeline> logger)
    {
        _providers = providers.OrderBy(p => p.Order);
        _logger = logger;
    }

    public async Task<string> BuildContextAsync(AgentSession session)
    {
        var parts = new List<string>();

        foreach (var provider in _providers)
        {
            try
            {
                var context = await provider.GetContextAsync(session);
                if (!string.IsNullOrWhiteSpace(context))
                {
                    parts.Add(context);
                    _logger.LogDebug("Context provider '{Name}' bağlam üretti ({Length} karakter)",
                        provider.Name, context.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Context provider '{Name}' hata verdi, atlanıyor", provider.Name);
            }
        }

        return parts.Count > 0
            ? string.Join("\n\n", parts)
            : "";
    }
}
