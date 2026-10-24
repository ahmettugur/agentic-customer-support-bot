// Domain/Model/OrderPolicy.cs
// İptal ve iade kuralları TEK yerde. Araçlar, depo ve "iade edebilir miyim?" sorusu aynı kararı verir; LLM kuralı
// tahmin etmez, kararı ve gerekçesini buradan okur. Tarihler ELLE biçimlenir (Blazor WASM'de tr-TR ICU yok).

using System.Globalization;

namespace CustomerSupportBot.Domain.Model;

public enum OrderAction { Cancel, Return }

/// <param name="Allowed">İşlem şu an yapılabilir mi?</param>
/// <param name="Code"><see cref="OrderPolicy.Eligible"/> ya da <see cref="WellKnown.ToolErrorCodes"/> değerlerinden biri.</param>
/// <param name="Message">Müşteriye aynen söylenebilecek, kesin gerekçeli cümle.</param>
public sealed record PolicyDecision(bool Allowed, string Code, string Message, DateTime? DeadlineUtc = null, int? DaysLeft = null);

public static class OrderPolicy
{
    public const string Eligible = "ELIGIBLE";

    /// <summary>İade süresi: TESLİMDEN itibaren gün (KnowledgeBase/iade-politikasi.md).</summary>
    public const int ReturnWindowDays = 14;

    /// <summary>
    /// Sürenin başladığı an: teslim tarihi; yoksa (eski kayıtlar) sipariş tarihi — bilinen en erken, dolayısıyla en tutucu tarih.
    /// </summary>
    public static DateTime ReturnWindowStart(DateTime? deliveredAt, DateTime orderDate) => deliveredAt ?? orderDate;

    public static bool IsWithinReturnWindow(DateTime windowStartUtc, DateTime nowUtc) =>
        (nowUtc - windowStartUtc).TotalDays <= ReturnWindowDays;

    public static PolicyDecision Evaluate(OrderAction action, OrderInfo order, DateTime nowUtc) =>
        action == OrderAction.Cancel ? EvaluateCancel(order) : EvaluateReturn(order, nowUtc);

    public static PolicyDecision EvaluateCancel(OrderInfo order)
    {
        if (order.Status == WellKnown.OrderStatuses.Cancelled)
            return new(false, WellKnown.ToolErrorCodes.OrderAlreadyCancelled, "Bu sipariş zaten iptal edilmiş.");

        if (order.Status is WellKnown.OrderStatuses.Processing or WellKnown.OrderStatuses.Shipped)
            return new(true, Eligible, $"Sipariş '{order.Status}' durumunda; iptal edilebilir.");

        return new(false, WellKnown.ToolErrorCodes.OrderNotCancellable,
            $"Sipariş '{order.Status}' durumunda olduğu için iptal edilemez; yalnızca 'İşleniyor' veya 'Kargolandı' siparişler iptal edilebilir." +
            (order.Status == WellKnown.OrderStatuses.Delivered ? " Teslim edilmiş siparişler için iade talebi oluşturulabilir." : ""));
    }

    public static PolicyDecision EvaluateReturn(OrderInfo order, DateTime nowUtc)
    {
        if (order.Status is WellKnown.OrderStatuses.ReturnRequested or WellKnown.OrderStatuses.ReturnApproved)
            return new(false, WellKnown.ToolErrorCodes.ReturnAlreadyRequested, "Bu sipariş için zaten bir iade talebi var.");

        if (order.Status != WellKnown.OrderStatuses.Delivered)
            return new(false, WellKnown.ToolErrorCodes.ReturnNotEligible,
                $"Sipariş '{order.Status}' durumunda; yalnızca teslim edilmiş siparişler iade edilebilir.");

        var deadline = ReturnWindowStart(order.DeliveredAt, order.OrderDate).AddDays(ReturnWindowDays);
        if (!IsWithinReturnWindow(ReturnWindowStart(order.DeliveredAt, order.OrderDate), nowUtc))
            return new(false, WellKnown.ToolErrorCodes.ReturnNotEligible,
                $"Teslimden itibaren {ReturnWindowDays} günlük iade süresi {Date(deadline)} tarihinde doldu.", deadline, 0);

        var daysLeft = Math.Max(0, (int)Math.Floor((deadline - nowUtc).TotalDays));
        return new(true, Eligible, $"Sipariş iade edilebilir. Son iade tarihi {Date(deadline)} ({daysLeft} gün kaldı).", deadline, daysLeft);
    }

    private static string Date(DateTime d) => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
}
