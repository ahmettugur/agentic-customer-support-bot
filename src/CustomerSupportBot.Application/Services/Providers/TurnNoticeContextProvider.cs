// Application/Services/Providers/TurnNoticeContextProvider.cs
// IContextProvider — bu turda sistemin müşteri adına yaptığı işlemi (tercih kaydı/silme) ajana bildirir.
// Not sistem tarafından üretilir (müşteri metni içermez); ajan işlemi tek cümleyle teyit etmeli, yeniden sormamalı.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Providers;

public sealed class TurnNoticeContextProvider : IContextProvider
{
    public string Name => "TurnNotice";

    /// <summary>En önde: yanıtın tutarlılığı için diğer bağlamdan önce okunmalı.</summary>
    public int Order => 3;

    public Task<string?> GetContextAsync(AgentSession session, string currentQuery, CancellationToken ct = default) =>
        Task.FromResult<string?>(string.IsNullOrWhiteSpace(session.State.TurnNotice)
            ? null
            : "## ℹ️ Bu turda sistem tarafından yapıldı\n- " + session.State.TurnNotice.Trim() + "\n");
}
