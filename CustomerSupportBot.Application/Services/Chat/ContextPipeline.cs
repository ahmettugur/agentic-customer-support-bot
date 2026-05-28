using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Chat;

/// <summary>
/// Kayıtlı tüm IContextProvider'ları sıralı çalıştırır ve
/// sonuçlarını birleştirerek tek bir bağlam metni üretir.
/// </summary>
public class ContextPipeline : IContextPipeline
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
        // Providers bağımsızdır — hepsi paralel çalıştırılır; hata veren atlanır.
        var providerList = _providers.ToList(); // Order zaten ctor'da uygulandı
        var tasks = providerList.Select(async p =>
        {
            try
            {
                var ctx = await p.GetContextAsync(session);
                if (!string.IsNullOrWhiteSpace(ctx))
                    _logger.LogDebug("Context provider '{Name}' bağlam üretti ({Length} karakter)",
                        p.Name, ctx.Length);
                return (Order: p.Order, Context: ctx);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Context provider '{Name}' hata verdi, atlanıyor", p.Name);
                return (Order: p.Order, Context: (string?)null);
            }
        });

        var results = await Task.WhenAll(tasks);

        var parts = results
            .OrderBy(r => r.Order)
            .Select(r => r.Context)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToList();

        return parts.Count > 0
            ? string.Join("\n\n", parts)
            : "";
    }
}
