// Services/RevisionService.cs
// Ileri — Self-critique eşiklerini aşan yanıtlar için revizyon servisi.
// ResponseAgent'ın ilk draftı yetersizse bu servis ikinci bir LLM geçişiyle
// Düzeltilmiş metni üretir. Tek-geçişli güvenlik ağı; sonsuz döngü yok.

using CustomerSupportBot.Models;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Services;

public class RevisionService
{
    private readonly IChatClient _chatClient;
    private readonly PromptService _prompts;

    public RevisionService(IChatClient chatClient, PromptService prompts)
    {
        _chatClient = chatClient;
        _prompts = prompts;
    }

    /// <summary>
    /// Verilen critique sonuçlarına göre revizyon gerekip gerekmediğine karar verir.
    /// Eşikler: revisionNeeded=true VEYA completeness<0.6 VEYA hallucinationRisk>0.3
    /// VEYA addressesUserQuery=false.
    /// </summary>
    public static bool ShouldRevise(ResponseCritique? critique)
    {
        if (critique == null) return false;
        if (critique.RevisionNeeded) return true;
        if (!critique.AddressesUserQuery) return true;
        if (critique.Completeness < 0.6) return true;
        if (critique.HallucinationRisk > 0.3) return true;
        return false;
    }

    /// <summary>
    /// Verilen ilk taslağı ve critique'i kullanarak iyileştirilmiş yanıt üretir.
    /// </summary>
    public async Task<string> ReviseAsync(
        string originalQuery,
        string firstDraft,
        ResponseCritique critique,
        CancellationToken ct = default)
    {
        var issues = critique.IssuesFound.Count > 0
            ? string.Join("; ", critique.IssuesFound)
            : "eksik bilgi / ton uyumsuzluğu";

        var systemPrompt = _prompts.Get("services/revision-system");
        var userPrompt = _prompts.Render("services/revision-user", new Dictionary<string, string?>
        {
            ["ORIGINAL_QUERY"]        = originalQuery,
            ["FIRST_DRAFT"]           = firstDraft,
            ["ADDRESSES_USER_QUERY"]  = critique.AddressesUserQuery.ToString(),
            ["COMPLETENESS"]          = critique.Completeness.ToString("F2"),
            ["HALLUCINATION_RISK"]    = critique.HallucinationRisk.ToString("F2"),
            ["TONE"]                  = critique.Tone,
            ["ISSUES"]                = issues,
            ["REVISION_NOTES"]        = critique.RevisionNotes
        });

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt)
        };

        try
        {
            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
            var revised = response.Text?.Trim() ?? firstDraft;
            return string.IsNullOrWhiteSpace(revised) ? firstDraft : revised;
        }
        catch
        {
            // Revizyon başarısız olursa orijinal taslağı kullan
            return firstDraft;
        }
    }
}
