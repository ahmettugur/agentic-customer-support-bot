// Application/Services/Notifications/ApprovalEmailComposer.cs
// Onay sonucu e-postasının metni (Türkçe, düz metin + HTML).

using System.Net;
using System.Text;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Notifications;

/// <summary>Saf biçimlendirici — HTML gövdesine giren her kullanıcı/sonuç metni escape edilir.</summary>
public static class ApprovalEmailComposer
{
    public static EmailMessage Compose(ApprovalRequest r, string to, string? toName, string? publicBaseUrl)
    {
        var action = ActionName(r.ToolName);
        var (headline, subjectOutcome) = Outcome(r);
        var detail = r.Status == ApprovalStatus.Approved ? r.ExecutionResult : r.DecisionReason ?? r.ExecutionResult;
        var link = string.IsNullOrWhiteSpace(publicBaseUrl) ? null : publicBaseUrl.TrimEnd('/') + "/";
        var greeting = string.IsNullOrWhiteSpace(toName) ? "Merhaba," : $"Merhaba {toName},";

        var text = new StringBuilder()
            .AppendLine(greeting).AppendLine()
            .AppendLine($"{action} talebinizle ilgili güncelleme: {headline}");
        if (!string.IsNullOrWhiteSpace(detail)) text.AppendLine().AppendLine(detail);
        if (link is not null) text.AppendLine().AppendLine($"Ayrıntılar için: {link}");
        text.AppendLine().AppendLine("Bu e-posta otomatik olarak gönderilmiştir.");

        var html = new StringBuilder()
            .Append("<p>").Append(Enc(greeting)).Append("</p>")
            .Append("<p><strong>").Append(Enc(action)).Append("</strong> talebinizle ilgili güncelleme: ")
            .Append(Enc(headline)).Append("</p>");
        if (!string.IsNullOrWhiteSpace(detail)) html.Append("<p>").Append(Enc(detail)).Append("</p>");
        if (link is not null) html.Append("<p><a href=\"").Append(Enc(link)).Append("\">Ayrıntıları görüntüleyin</a></p>");
        html.Append("<p style=\"color:#64748b;font-size:12px\">Bu e-posta otomatik olarak gönderilmiştir.</p>");

        return new EmailMessage(to, toName, $"Talebiniz {subjectOutcome} — {action}", text.ToString(), html.ToString());
    }

    private static (string Headline, string SubjectOutcome) Outcome(ApprovalRequest r) => r.Status switch
    {
        ApprovalStatus.Approved when r.ExecutionStatus == ApprovalExecutionStatus.Failed =>
            ("talebiniz onaylandı ancak işlem tamamlanamadı.", "onaylandı ancak tamamlanamadı"),
        ApprovalStatus.Approved => ("talebiniz onaylandı ve işleme alındı.", "onaylandı"),
        ApprovalStatus.Rejected => ("talebiniz reddedildi.", "reddedildi"),
        ApprovalStatus.Expired => ("talebiniz zamanında yanıtlanamadığı için zaman aşımına uğradı.", "zaman aşımına uğradı"),
        _ => ("talebiniz güncellendi.", "güncellendi")
    };

    private static string ActionName(string toolName) => toolName switch
    {
        WellKnown.ToolNames.OrderPlacement => "Sipariş",
        WellKnown.ToolNames.OrderCancel => "Sipariş iptali",
        WellKnown.ToolNames.ReturnRequest => "İade",
        WellKnown.ToolNames.ComplaintRegistration => "Şikayet",
        _ => "İşlem"
    };

    private static string Enc(string s) => WebUtility.HtmlEncode(s);
}
