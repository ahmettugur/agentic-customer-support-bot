// Application/Services/Notifications/ProactiveEmailComposer.cs
// Müşteri sormadan gönderilen bilgilendirme e-postaları (Türkçe, düz metin + HTML).

using System.Net;
using System.Text;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Notifications;

/// <summary>Saf biçimlendirici — HTML gövdesine giren her metin (ürün adı, kargo firması, takip no) escape edilir.</summary>
public static class ProactiveEmailComposer
{
    public static EmailMessage DelayedShipment(
        string orderId, OrderInfo order, int daysInTransit, string to, string? toName, string? publicBaseUrl)
    {
        var greeting = string.IsNullOrWhiteSpace(toName) ? "Merhaba," : $"Merhaba {toName},";
        var link = string.IsNullOrWhiteSpace(publicBaseUrl) ? null : publicBaseUrl.TrimEnd('/') + "/";
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(order.Carrier)) details.Add($"Kargo firması: {order.Carrier}");
        if (!string.IsNullOrWhiteSpace(order.TrackingNumber)) details.Add($"Takip numarası: {order.TrackingNumber}");

        var text = new StringBuilder()
            .AppendLine(greeting).AppendLine()
            .AppendLine($"#{orderId} numaralı siparişiniz {daysInTransit} gündür kargoda ve henüz teslim edilmedi. Gecikme için üzgünüz.")
            .AppendLine($"Ürünler: {order.LinesSummary()}");
        foreach (var d in details) text.AppendLine(d);
        text.AppendLine().AppendLine("Kargonuzun nerede olduğunu öğrenmek ya da bir temsilciyle görüşmek için destek asistanına yazabilirsiniz.");
        if (link is not null) text.AppendLine(link);
        text.AppendLine().AppendLine("Bu e-posta otomatik olarak gönderilmiştir.");

        var html = new StringBuilder()
            .Append("<p>").Append(Enc(greeting)).Append("</p>")
            .Append("<p><strong>#").Append(Enc(orderId)).Append("</strong> numaralı siparişiniz ")
            .Append(daysInTransit).Append(" gündür kargoda ve henüz teslim edilmedi. Gecikme için üzgünüz.</p>")
            .Append("<p>Ürünler: ").Append(Enc(order.LinesSummary())).Append("</p>");
        if (details.Count > 0) html.Append("<p>").Append(string.Join("<br>", details.Select(Enc))).Append("</p>");
        if (link is not null)
            html.Append("<p><a href=\"").Append(Enc(link)).Append("\">Destek asistanına yazın</a></p>");
        html.Append("<p style=\"color:#64748b;font-size:12px\">Bu e-posta otomatik olarak gönderilmiştir.</p>");

        return new EmailMessage(to, toName, $"Siparişiniz hâlâ yolda — #{orderId}", text.ToString(), html.ToString());
    }

    /// <summary>Şikayetin serbest metni (müşteri girdisi) e-postaya GİRMEZ; yalnızca numara, sipariş ve bekleme süresi.</summary>
    public static EmailMessage StaleComplaint(
        string complaintId, ComplaintInfo complaint, int daysWaiting, string to, string? toName, string? publicBaseUrl)
    {
        var greeting = string.IsNullOrWhiteSpace(toName) ? "Merhaba," : $"Merhaba {toName},";
        var link = string.IsNullOrWhiteSpace(publicBaseUrl) ? null : publicBaseUrl.TrimEnd('/') + "/";
        var sentence = $"#{complaintId} numaralı şikayetiniz (sipariş #{complaint.OrderId}) {daysWaiting} gündür sonuçlanmadı. Ekibimiz konuyla ilgileniyor; beklettiğimiz için üzgünüz.";

        var text = new StringBuilder().AppendLine(greeting).AppendLine().AppendLine(sentence).AppendLine()
            .AppendLine("Bir temsilciyle görüşmek isterseniz destek asistanına yazabilirsiniz.");
        if (link is not null) text.AppendLine(link);
        text.AppendLine().AppendLine("Bu e-posta otomatik olarak gönderilmiştir.");

        var html = new StringBuilder()
            .Append("<p>").Append(Enc(greeting)).Append("</p>")
            .Append("<p>").Append(Enc(sentence)).Append("</p>")
            .Append("<p>Bir temsilciyle görüşmek isterseniz destek asistanına yazabilirsiniz.</p>");
        if (link is not null) html.Append("<p><a href=\"").Append(Enc(link)).Append("\">Destek asistanına yazın</a></p>");
        html.Append("<p style=\"color:#64748b;font-size:12px\">Bu e-posta otomatik olarak gönderilmiştir.</p>");

        return new EmailMessage(to, toName, $"Şikayetiniz hâlâ inceleniyor — #{complaintId}", text.ToString(), html.ToString());
    }

    private static string Enc(string s) => WebUtility.HtmlEncode(s);
}
