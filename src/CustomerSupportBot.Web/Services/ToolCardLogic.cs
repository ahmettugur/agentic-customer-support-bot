using System.Text.Json;

namespace CustomerSupportBot.Web.Services;

public sealed record OrderLineView(string Product, int Quantity);

public sealed record OrderCardView(
    string OrderId, string Status, DateTimeOffset OrderDate, IReadOnlyList<OrderLineView> Lines, int TotalQuantity,
    string? Carrier, string? TrackingNumber, DateTimeOffset? ShippedAt, DateTimeOffset? DeliveredAt, DateTimeOffset? CancelledAt);

public sealed record OrderCardsHint(int TotalCount, IReadOnlyList<OrderCardView> Orders);

public sealed record ComplaintCardView(
    string ComplaintId, string OrderId, string Status, string Complaint, string? Resolution, DateTimeOffset? UpdatedAt);

public enum CardStepState { Done, Current, Pending, Failed }

public sealed record CardStep(string Label, CardStepState State, string? When);

/// <summary>
/// Sohbetteki sipariş ve şikayet kartlarının saf mantığı. Sunucu kartları veritabanındaki gerçek veriden <c>ui_hint</c> ile yollar
/// (araç metninden değil); burada yalnızca ayrıştırma ve gösterim kuralları vardır. Tarih ELLE biçimlenir: Blazor WASM'de
/// tr-TR ICU verisi yoktur.
/// </summary>
public static class ToolCardLogic
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Durum metinleri sunucudaki WellKnown.OrderStatuses ile birebir aynıdır (Web, Domain'e bağlı değil).
    private const string Processing = "İşleniyor", Shipped = "Kargolandı", Delivered = "Teslim Edildi", Cancelled = "İptal Edildi",
        ReturnRequested = "İade Talep Edildi", ReturnApproved = "İade Onaylandı";

    public static OrderCardsHint? ParseOrderHint(JsonElement hint)
    {
        try
        {
            var parsed = hint.Deserialize<OrderCardsHint>(Json);
            return parsed?.Orders is null ? null : parsed;
        }
        catch (JsonException) { return null; }
    }

    public static IReadOnlyList<ComplaintCardView> ParseComplaintHint(JsonElement hint)
    {
        try
        {
            return hint.TryGetProperty("complaints", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.Deserialize<List<ComplaintCardView>>(Json) ?? []
                : [];
        }
        catch (JsonException) { return []; }
    }

    /// <summary>
    /// Sipariş yolculuğu: Alındı → Kargolandı → Teslim edildi (+ iade adımı). İptalde yalnızca Alındı → İptal edildi.
    /// Bilinmeyen durum, tek bir "mevcut" adıma düşer (kart yine de durumu gösterir; istisna fırlatmaz).
    /// </summary>
    public static IReadOnlyList<CardStep> Steps(OrderCardView order, TimeSpan offset)
    {
        string? D(DateTimeOffset? at) => at is null ? null : Date(at.Value, offset);
        var received = new CardStep("Sipariş alındı", CardStepState.Done, Date(order.OrderDate, offset));

        switch (order.Status)
        {
            case Processing:
                return [received with { State = CardStepState.Current }, new("Kargolandı", CardStepState.Pending, null), new("Teslim edildi", CardStepState.Pending, null)];
            case Shipped:
                return [received, new("Kargolandı", CardStepState.Current, D(order.ShippedAt)), new("Teslim edildi", CardStepState.Pending, null)];
            case Delivered:
                return [received, new("Kargolandı", CardStepState.Done, D(order.ShippedAt)), new("Teslim edildi", CardStepState.Done, D(order.DeliveredAt))];
            case Cancelled:
                return [received, new("İptal edildi", CardStepState.Failed, D(order.CancelledAt))];
            case ReturnRequested or ReturnApproved:
                return
                [
                    received,
                    new("Kargolandı", CardStepState.Done, D(order.ShippedAt)),
                    new("Teslim edildi", CardStepState.Done, D(order.DeliveredAt)),
                    order.Status == ReturnRequested
                        ? new("İade talep edildi", CardStepState.Current, null)
                        : new("İade onaylandı", CardStepState.Done, null)
                ];
            default:
                return [new(order.Status, CardStepState.Current, Date(order.OrderDate, offset))];
        }
    }

    public static string OrderTone(string status) => status switch
    {
        Processing => "info",
        Shipped => "progress",
        Delivered => "success",
        Cancelled => "danger",
        ReturnRequested or ReturnApproved => "warn",
        _ => "neutral"
    };

    public static string ComplaintTone(string status) => status switch
    {
        "Beklemede" => "warn",
        "İncelemede" => "info",
        "Çözüldü" => "success",
        "Reddedildi" => "danger",
        _ => "neutral"
    };

    public static string Date(DateTimeOffset at, TimeSpan offset)
    {
        var local = at.ToOffset(offset);
        return $"{local.Day:00}.{local.Month:00}.{local.Year}";
    }

    /// <summary>"Yurtiçi Kargo · YK123456" / yalnızca biri / hiçbiri (null).</summary>
    public static string? Shipping(OrderCardView order)
    {
        var parts = new[] { order.Carrier, order.TrackingNumber }.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        return parts.Length == 0 ? null : string.Join(" · ", parts);
    }
}
