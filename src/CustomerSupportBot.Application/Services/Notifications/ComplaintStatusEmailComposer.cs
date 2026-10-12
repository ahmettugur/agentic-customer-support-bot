// Application/Services/Notifications/ComplaintStatusEmailComposer.cs
// Şikayet durum e-postasının metni (Türkçe, düz metin + HTML). İç not ASLA girmez.

using System.Net;
using System.Text;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Notifications;

public static class ComplaintStatusEmailComposer
{
    public static EmailMessage Compose(string complaintId, ComplaintInfo complaint, string kind, string to, string? toName, string? publicBaseUrl)
    {
        var greeting = string.IsNullOrWhiteSpace(toName) ? "Merhaba," : $"Merhaba {toName},";
        var link = string.IsNullOrWhiteSpace(publicBaseUrl) ? null : publicBaseUrl.TrimEnd('/') + "/";
        var (subjectWord, sentence) = kind == ComplaintStatusEmailService.InReview
            ? ("inceleniyor", $"#{complaintId} numaralı şikayetiniz (sipariş #{complaint.OrderId}) bir temsilcimiz tarafından incelemeye alındı.")
            : complaint.Status == WellKnown.ComplaintStatuses.Rejected
                ? ("sonuçlandı", $"#{complaintId} numaralı şikayetiniz (sipariş #{complaint.OrderId}) incelendi ve sonuçlandırıldı.")
                : ("çözüldü", $"#{complaintId} numaralı şikayetiniz (sipariş #{complaint.OrderId}) çözüldü.");
        var resolution = kind == ComplaintStatusEmailService.Closed && !string.IsNullOrWhiteSpace(complaint.Resolution)
            ? complaint.Resolution : null;

        var text = new StringBuilder().AppendLine(greeting).AppendLine().AppendLine(sentence);
        if (resolution is not null) text.AppendLine().AppendLine($"Açıklama: {resolution}");
        if (link is not null) text.AppendLine().AppendLine($"Sorularınız için: {link}");
        text.AppendLine().AppendLine("Bu e-posta otomatik olarak gönderilmiştir.");

        var html = new StringBuilder()
            .Append("<p>").Append(Enc(greeting)).Append("</p>")
            .Append("<p>").Append(Enc(sentence)).Append("</p>");
        if (resolution is not null) html.Append("<p><strong>Açıklama:</strong> ").Append(Enc(resolution)).Append("</p>");
        if (link is not null) html.Append("<p><a href=\"").Append(Enc(link)).Append("\">Destek asistanına sorun</a></p>");
        html.Append("<p style=\"color:#64748b;font-size:12px\">Bu e-posta otomatik olarak gönderilmiştir.</p>");

        return new EmailMessage(to, toName, $"Şikayetiniz {subjectWord} — #{complaintId}", text.ToString(), html.ToString());
    }

    private static string Enc(string s) => WebUtility.HtmlEncode(s);
}
