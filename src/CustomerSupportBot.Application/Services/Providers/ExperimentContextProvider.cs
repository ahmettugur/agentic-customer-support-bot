// Application/Services/Providers/ExperimentContextProvider.cs
// IContextProvider — oturumun atandığı deney kollarının operatör metinlerini (Hint) bağlama ekler.
// Metin yapılandırmadan gelir (güvenilir); müşteri girdisi bu bloğa HİÇ girmez.

using System.Text;
using CustomerSupportBot.Application.Services.Evaluation;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Providers;

public sealed class ExperimentContextProvider(IOptions<ExperimentOptions> options) : IContextProvider
{
    public string Name => "Experiment";

    /// <summary>Özetten (5) önce: deney yönergesi, müşteri profili ve semantik bilgiden bağımsız, kısa ve sabittir.</summary>
    public int Order => 4;

    public Task<string?> GetContextAsync(AgentSession session, string currentQuery, CancellationToken ct = default)
    {
        var o = options.Value;
        if (!o.Enabled) return Task.FromResult<string?>(null);

        var sb = new StringBuilder();
        foreach (var experiment in o.Effective)
        {
            var arm = ExperimentAssigner.Assign(session.SessionId, experiment);
            if (!string.IsNullOrWhiteSpace(arm?.Hint)) sb.AppendLine($"- {arm.Hint.Trim()}");
        }
        return Task.FromResult<string?>(sb.Length == 0 ? null : "## 🧪 Yanıt Yönergesi\n" + sb);
    }
}
