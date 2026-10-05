// Application/Services/Notifications/OrderStatusEmailComposer.cs
// Sipariş durum e-postasının metni (Türkçe, düz metin + HTML).

using System.Net;
using System.Text;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Notifications;

/// <summary>Saf biçimlendirici — HTML gövdesine giren her metin (ürün adı, kargo firması, takip no) escape edilir.</summary>
public static class OrderStatusEmailComposer
{
    public static EmailMessage Compose(string orderId, OrderInfo order, string kind, string to, string? toName, string? publicBaseUrl)
    {
        var shipped = kind == OrderStatusEmailService.Shipped;
        var headline = shipped ? "kargoya verildi" : "teslim edildi";
        var greeting = string.IsNullOrWhiteSpace(toName) ? "Merhaba," : $"Merhaba {toName},";
        var link = string.IsNullOrWhiteSpace(publicBaseUrl) ? null : publicBaseUrl.TrimEnd('/') + "/";
        var shipping = new List<string>();
        if (shipped && !string.IsNullOrWhiteSpace(order.Carrier)) shipping.Add($"Kargo firması: {order.Carrier}");
        if (shipped && !string.IsNullOrWhiteSpace(order.TrackingNumber)) shipping.Add($"Takip numarası: {order.TrackingNumber}");

        var text = new StringBuilder()
            .AppendLine(greeting).AppendLine()
            .AppendLine($"#{orderId} numaralı siparişiniz {headline}.")
            .AppendLine($"Ürünler: {order.LinesSummary()}");
        foreach (var line in shipping) text.AppendLine(line);
        if (link is not null) text.AppendLine().AppendLine($"Sorularınız için: {link}");
        text.AppendLine().AppendLine("Bu e-posta otomatik olarak gönderilmiştir.");

        var html = new StringBuilder()
            .Append("<p>").Append(Enc(greeting)).Append("</p>")
            .Append("<p><strong>#").Append(Enc(orderId)).Append("</strong> numaralı siparişiniz ")
            .Append(Enc(headline)).Append(".</p>")
            .Append("<p>Ürünler: ").Append(Enc(order.LinesSummary())).Append("</p>");
        if (shipping.Count > 0)
            html.Append("<p>").Append(string.Join("<br>", shipping.Select(Enc))).Append("</p>");
        if (link is not null) html.Append("<p><a href=\"").Append(Enc(link)).Append("\">Destek asistanına sorun</a></p>");
        html.Append("<p style=\"color:#64748b;font-size:12px\">Bu e-posta otomatik olarak gönderilmiştir.</p>");

        return new EmailMessage(to, toName, $"Siparişiniz {headline} — #{orderId}", text.ToString(), html.ToString());
    }

    private static string Enc(string s) => WebUtility.HtmlEncode(s);
}
