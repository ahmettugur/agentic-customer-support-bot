// Ports/Inbound/CustomerReasoningView.cs
// Müşteriye giden "düşünce süreci" görünümü. ReasoningResult'un TAMAMI iç bir yapıdır (nextAction, rationale, varsayımlar,
// decisionReason, sanity uyarıları, doğrulanmış kimlikler, adım önkoşulları); ağa yalnızca sohbet panelinin gösterdiği alanlar çıkar.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Inbound;

public sealed record CustomerReasoningStep(int Order, string Description, string? Action, string? Grounding, double? Confidence);

public sealed record CustomerSubTaskView(int Order, string TargetAgent, string Description);

/// <summary>
/// <c>reasoning_complete</c> olayının ve /chat yanıtındaki <c>reasoning</c> alanının yükü. Alan adları (camelCase) sohbet panelinin
/// okuduklarıyla birebir aynıdır. Serbest metin alanları çağıranın verdiği temizleyiciden geçer ve sınırlıdır.
/// </summary>
public sealed record CustomerReasoningView(
    string Analysis,
    IReadOnlyList<CustomerReasoningStep> Steps,
    string Intent,
    IReadOnlyList<string> RequiredInfo,
    string Confidence,
    bool IsFallback,
    IReadOnlyList<CustomerSubTaskView> SubTasks)
{
    public const int MaxAnalysisLength = 600;
    public const int MaxTextLength = 300;
    public const int MaxSteps = 8;
    public const int MaxSubTasks = 6;
    public const int MaxRequiredInfo = 6;

    public static CustomerReasoningView From(ReasoningResult r, Func<string, string> clean)
    {
        string Safe(string? text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            try
            {
                var cleaned = clean(text);
                return cleaned.Length <= max ? cleaned : cleaned[..max] + "…";
            }
            catch
            {
                // Temizlenemeyen metin ham halde ASLA gönderilmez; panel boş alanı zaten tolere ediyor.
                return "";
            }
        }

        var steps = r.Steps.Take(MaxSteps)
            .Select(s => new CustomerReasoningStep(s.Order, Safe(s.Description, MaxTextLength),
                NullIfEmpty(Safe(s.Action, 32)), NullIfEmpty(Safe(s.Grounding, 32)), s.Confidence))
            .ToList();

        var subTasks = r.SubTasks.Take(MaxSubTasks)
            .Select(t => new CustomerSubTaskView(t.Order, Safe(t.TargetAgent, 32), Safe(t.Description, MaxTextLength)))
            .ToList();

        var required = r.RequiredInfo.Take(MaxRequiredInfo).Select(x => Safe(x, 64)).Where(x => x.Length > 0).ToList();

        return new CustomerReasoningView(
            Safe(r.Analysis, MaxAnalysisLength), steps, r.Intent ?? "", required, r.Confidence ?? "", r.IsFallback, subTasks);
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
}
