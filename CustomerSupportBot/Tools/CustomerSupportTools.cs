// Tools/CustomerSupportTools.cs
// [Description] attribute'u, AIFunctionFactory.Create() ile LLM'e araç şeması sağlar.


using System.Collections.Concurrent;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using CustomerSupportBot.Models;

namespace CustomerSupportBot.Tools;

/// <summary>
/// Müşteri destek araçları. Tüm metotlar statik olarak tanımlanmıştır çünkü
/// FakeDatabase'deki statik verilere erişirler.
/// </summary>
public static class CustomerSupportTools
{
    private static readonly object _stockLock = new();

    // ─── Idempotency cache ───
    // Yan etkili tool'lar (sipariş oluşturma, şikayet kaydı) için son 60 saniyede aynı
    // anahtarla yapılan çağrıları engelle. ChatManager LLM çeşitli sebeplerle bir
    // alt-görevin specialist'ini birden fazla kez tetikleyebilir; bu müşteri tarafında
    // mükerrer kayıtlara yol açar (gerçek hata: CMP-3 + CMP-4 aynı şikayet).
    private static readonly ConcurrentDictionary<string, (DateTime At, ToolResult Result)> _idempotencyCache = new();
    private static readonly TimeSpan _idempotencyWindow = TimeSpan.FromSeconds(60);

    private static string ComputeIdempotencyKey(string toolName, params string?[] parts)
    {
        var raw = string.Join("|", parts.Select(p => p?.Trim().ToLowerInvariant() ?? ""));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return $"{toolName}:{Convert.ToHexString(bytes)[..16]}";
    }

    private static bool TryGetCachedResult(string key, out ToolResult cached)
    {
        if (_idempotencyCache.TryGetValue(key, out var entry) &&
            DateTime.UtcNow - entry.At < _idempotencyWindow)
        {
            cached = entry.Result;
            return true;
        }
        cached = default!;
        return false;
    }

    private static void StoreResult(string key, ToolResult result)
    {
        _idempotencyCache[key] = (DateTime.UtcNow, result);
        // En fazla 200 entry tut, eskileri at — unbounded memory koruma
        if (_idempotencyCache.Count > 200)
        {
            var cutoff = DateTime.UtcNow - _idempotencyWindow;
            foreach (var stale in _idempotencyCache.Where(kv => kv.Value.At < cutoff).Select(kv => kv.Key).ToList())
            {
                _idempotencyCache.TryRemove(stale, out _);
            }
        }
    }

    // ─── ÜRÜN SORGULAMA ARACI ───
    // ToolResult zarfı döndürür.
    [Description("Ürün kataloğundan ürün bilgisi sorgular. Ürün adı veya kısmi adı ile arama yapar. " +
                 "Sonuç yapılandırılmış ToolResult olarak döner (success, confidence, data, error).")]
    public static ToolResult ProductInquiryTool(
        [Description("Sorgulanacak ürünün adı veya kısmi adı")] string productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            return ToolResult.ValidationError(
                "Ürün adı boş olamaz. Hangi ürünü aradığınızı belirtin.",
                WellKnown.ToolParameterNames.ProductName);
        }

        var matchedName = FakeDatabase.FindClosestProduct(productName);
        if (matchedName == null)
        {
            return ToolResult.NotFound(
                WellKnown.ToolErrorCodes.ProductNotFound,
                $"Üzgünüz, '{productName}' ürünümüzün kataloğunda bulunmamaktadır.");
        }

        var product = FakeDatabase.ProductCatalog[matchedName];
        var confidence = matchedName.Equals(productName, StringComparison.OrdinalIgnoreCase)
            ? 1.0 : 0.85; // Fuzzy match → biraz daha düşük güven

        return ToolResult.Ok(
            message: $"{matchedName}: Fiyat = ${product.Price}, Stok = {product.Stock} adet.",
            data: new { name = matchedName, price = product.Price, stock = product.Stock },
            confidence: confidence);
    }

    // ─── SİPARİŞ OLUŞTURMA ARACI ───
    // ToolResult zarfı; yan etkili tool.
    [Description("Yeni sipariş oluşturur. Ürün adı, adet ve müşteri kimlik numarası zorunludur. " +
                 "Sonuç ToolResult olarak döner.")]
    public static ToolResult OrderPlacementTool(
        [Description("Sipariş verilecek ürünün adı")] string productName,
        [Description("Sipariş adedi")] int? quantity,
        [Description("Müşteri kimlik numarası (zorunlu)")] string customerId)
    {
        // Validation — eksik alanları topla
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(customerId)) missing.Add(WellKnown.ToolParameterNames.CustomerId);
        if (string.IsNullOrWhiteSpace(productName)) missing.Add(WellKnown.ToolParameterNames.ProductName);
        if (quantity == null || quantity <= 0) missing.Add(WellKnown.ToolParameterNames.Quantity);

        if (missing.Count > 0)
        {
            return ToolResult.ValidationError(
                $"Sipariş için şu bilgiler gerekli: {string.Join(", ", missing)}.",
                missing.ToArray());
        }

        var matchedName = FakeDatabase.FindClosestProduct(productName);
        if (matchedName == null)
        {
            return ToolResult.NotFound(
                WellKnown.ToolErrorCodes.ProductNotFound,
                $"Üzgünüz, '{productName}' ürünümüzün kataloğunda bulunmamaktadır.");
        }

        // Idempotency: aynı müşteri + aynı ürün + aynı adet 60 saniye içinde → cache'den dön
        var idemKey = ComputeIdempotencyKey(
            "order_placement", customerId, matchedName, quantity!.Value.ToString());
        if (TryGetCachedResult(idemKey, out var cachedOrder))
        {
            return cachedOrder;
        }

        var product = FakeDatabase.ProductCatalog[matchedName];

        lock (_stockLock)
        {
            if (product.Stock < quantity!.Value)
            {
                return ToolResult.Conflict(
                    WellKnown.ToolErrorCodes.StockInsufficient,
                    $"Yalnızca {product.Stock} adet {matchedName} stokta mevcut, " +
                    $"{quantity.Value} adet sipariş verilemez.");
            }
            product.Stock -= quantity.Value;
        }

        var orderId = FakeDatabase.GetNextOrderId();
        FakeDatabase.OrdersDb[orderId] = new OrderInfo
        {
            Product = matchedName,
            Quantity = quantity.Value,
            CustomerId = customerId,
            Status = WellKnown.OrderStatuses.Processing,
            OrderDate = DateTime.Now
        };

        var orderResult = ToolResult.Ok(
            message: $"Sipariş başarıyla oluşturuldu! Sipariş numarası: {orderId}",
            data: new
            {
                orderId,
                product = matchedName,
                quantity = quantity.Value,
                customerId,
                status = WellKnown.OrderStatuses.Processing
            });
        StoreResult(idemKey, orderResult);
        return orderResult;
    }

    // ─── SİPARİŞ DURUM SORGULAMA ARACI ───
    // ToolResult zarfı.
    [Description("Sipariş durumunu sipariş numarasıyla sorgular. Sonuç ToolResult olarak döner.")]
    public static ToolResult OrderStatusTool(
        [Description("Sorgulanacak sipariş numarası (örn: ORD-1)")] string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return ToolResult.ValidationError(
                "Sipariş numarası boş olamaz.", WellKnown.ToolParameterNames.OrderId);
        }

        if (FakeDatabase.OrdersDb.TryGetValue(orderId, out var order))
        {
            return ToolResult.Ok(
                message: $"Sipariş No: {orderId}, Ürün: {order.Product}, " +
                         $"Adet: {order.Quantity}, Durum: {order.Status}.",
                data: new
                {
                    orderId,
                    product = order.Product,
                    quantity = order.Quantity,
                    status = order.Status,
                    customerId = order.CustomerId,
                    orderDate = order.OrderDate
                });
        }

        return ToolResult.NotFound(
            WellKnown.ToolErrorCodes.OrderNotFound,
            $"'{orderId}' numaralı sipariş bulunamadı.");
    }

    // ─── ŞİKAYET KAYIT ARACI ───
    // ToolResult zarfı; yan etkili tool.
    // Customer_id opsiyoneldir: boş/null ise order_id üzerinden ilgili siparişin
    // Customer_id'si otomatik türetilir (kullanıcıya tekrar sormamak için).
    [Description("Müşteri şikayetini sipariş numarasıyla kaydeder. order_id ve description zorunludur; " +
                 "customer_id opsiyoneldir (boşsa order_id üzerinden siparişten otomatik türetilir). " +
                 "Sonuç ToolResult olarak döner.")]
    public static ToolResult ComplaintRegistrationTool(
        [Description("Şikayetin ilişkili olduğu sipariş numarası (zorunlu, ör. 'ORD-1')")] string orderId,
        [Description("Şikayet açıklaması (zorunlu, en az 10 karakter)")] string complaintText,
        [Description("Müşteri kimlik numarası (opsiyonel; boşsa siparişten türetilir)")] string? customerId = null)
    {
        // Zorunlu alanlar: order_id + description. customer_id opsiyonel.
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(orderId)) missing.Add(WellKnown.ToolParameterNames.OrderId);
        if (string.IsNullOrWhiteSpace(complaintText) || complaintText.Length < 10)
            missing.Add($"{WellKnown.ToolParameterNames.ComplaintDescription} (en az 10 karakter)");

        if (missing.Count > 0)
        {
            return ToolResult.ValidationError(
                $"Şikayet kaydı için şu bilgiler gerekli: {string.Join(", ", missing)}.",
                missing.ToArray());
        }

        if (!FakeDatabase.OrdersDb.TryGetValue(orderId, out var order))
        {
            return ToolResult.NotFound(
                WellKnown.ToolErrorCodes.OrderNotFound,
                $"'{orderId}' numaralı sipariş bulunamadı, şikayet kaydı oluşturulamadı.");
        }

        // Customer_id eksikse siparişten türet; verildiyse sipariş sahibiyle tutarlılık kontrolü.
        var effectiveCustomerId = customerId;
        var inferred = false;
        if (string.IsNullOrWhiteSpace(effectiveCustomerId))
        {
            effectiveCustomerId = order.CustomerId;
            inferred = true;
        }
        else if (!string.Equals(effectiveCustomerId, order.CustomerId, StringComparison.Ordinal))
        {
            return ToolResult.Conflict(
                WellKnown.ToolErrorCodes.CustomerIdMismatch,
                $"Sağladığınız müşteri kimliği ({effectiveCustomerId}) '{orderId}' siparişinin " +
                $"sahibiyle eşleşmiyor. Lütfen doğru kimlik ile tekrar deneyin.");
        }

        // Idempotency: aynı müşteri + aynı sipariş + aynı şikayet metni 60 saniye içinde → cache'den dön.
        // Bu, ChatManager'ın bir compound query'de specialist'i birden fazla çağırmasından
        // kaynaklanan mükerrer şikayet kaydını (gözlemlenen CMP-3+CMP-4 hatası) önler.
        var idemKey = ComputeIdempotencyKey(
            "complaint_registration", effectiveCustomerId, orderId, complaintText);
        if (TryGetCachedResult(idemKey, out var cachedComplaint))
        {
            return cachedComplaint;
        }

        var complaintId = FakeDatabase.GetNextComplaintId();
        FakeDatabase.ComplaintsDb[complaintId] = new ComplaintInfo
        {
            OrderId = orderId,
            CustomerId = effectiveCustomerId!,
            Complaint = complaintText,
            Status = WellKnown.ComplaintStatuses.Pending
        };

        var successMessage = $"Şikayet başarıyla kaydedildi! Şikayet numarası: {complaintId}";
        var complaintResult = ToolResult.Ok(
            message: successMessage,
            data: new
            {
                complaintId,
                orderId,
                customerId = effectiveCustomerId,
                customerIdInferred = inferred,
                status = WellKnown.ComplaintStatuses.Pending
            });
        StoreResult(idemKey, complaintResult);
        return complaintResult;
    }

    // ─── EN SON SİPARİŞ SORGULAMA ARACI ───
    // ToolResult zarfı.
    [Description("Müşterinin en son oluşturduğu siparişi döndürür. Sonuç ToolResult olarak döner.")]
    public static ToolResult GetLastOrderTool(
        [Description("Müşteri kimlik numarası (zorunlu)")] string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return ToolResult.ValidationError(
                "Müşteri kimlik numarası boş olamaz.", WellKnown.ToolParameterNames.CustomerId);
        }

        var result = FakeDatabase.GetLastOrder(customerId);
        if (result == null)
        {
            return ToolResult.NotFound(
                WellKnown.ToolErrorCodes.NoOrdersForCustomer,
                $"Henüz {customerId} kimlik numarası ile kayıtlı bir siparişiniz bulunmamaktadır.");
        }

        var (orderId, order) = result.Value;
        return ToolResult.Ok(
            message: $"Sipariş No: {orderId}, Ürün: {order.Product}, Adet: {order.Quantity}, " +
                     $"Durum: {order.Status}, Tarih: {order.OrderDate:g}",
            data: new
            {
                orderId,
                product = order.Product,
                quantity = order.Quantity,
                status = order.Status,
                orderDate = order.OrderDate,
                customerId = order.CustomerId
            });
    }

    // ─── TÜM SİPARİŞLERİ LİSTELEME ARACI ───
    // ToolResult zarfı.
    [Description("Müşterinin tüm siparişlerini listeler. Sonuç ToolResult olarak döner.")]
    public static ToolResult GetAllOrdersTool(
        [Description("Müşteri kimlik numarası (zorunlu)")] string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return ToolResult.ValidationError(
                "Müşteri kimlik numarası boş olamaz.", WellKnown.ToolParameterNames.CustomerId);
        }

        var orders = FakeDatabase.GetAllOrders(customerId).ToList();
        if (orders.Count == 0)
        {
            return ToolResult.NotFound(
                WellKnown.ToolErrorCodes.NoOrdersForCustomer,
                $"Henüz {customerId} kimlik numarası ile kayıtlı bir siparişiniz bulunmamaktadır.");
        }

        var lines = orders.Select((o, i) =>
            $"{i + 1}. Sipariş No: {o.OrderId}, Ürün: {o.Order.Product}, Adet: {o.Order.Quantity}, " +
            $"Durum: {o.Order.Status}, Tarih: {o.Order.OrderDate:g}");

        var summary = string.Join("\n", lines);
        return ToolResult.Ok(
            message: summary,
            data: new
            {
                totalCount = orders.Count,
                customerId,
                orders = orders.Select(o => new
                {
                    orderId = o.OrderId,
                    product = o.Order.Product,
                    quantity = o.Order.Quantity,
                    status = o.Order.Status,
                    orderDate = o.Order.OrderDate
                }).ToList()
            });
    }

    // ─── İNSAN TEMSİLCİ TALEP ARACI ───
    // HITL Live Takeover — kullanıcı explicit "temsilciyle görüşmek istiyorum"
    // Dediğinde HumanHandoffAgent bu tool'u çağırır. Yan etkisi yoktur (FakeDatabase'e
    // Dokunmaz). Sadece bir sinyal üretir; gerçek eskalasyon kaydı CustomerSupportTeam
    // `EmitEscalationsIfAny` pipeline'ında (postToolReflection.status=needs_escalation
    // Tetiğiyle) IEscalationSink'e yazılır. Bu sayede mevcut eskalasyon altyapısı
    // (admin panel, canlı takeover) yeniden kullanılır.
    [Description("Kullanıcıyı bir müşteri temsilcisine yönlendirme talebini kaydeder. " +
                 "Tool yan etkisi yoktur — yalnızca niyeti formalize eder. Kullanıcının " +
                 "açıkça bir insan temsilcisiyle görüşme isteğini belirttiği durumlarda çağrılır.")]
    public static ToolResult HumanHandoffTool(
        [Description("Kullanıcının temsilciyle görüşme isteme sebebi (1-2 cümle; " +
                     "örneğin 'bot yetersiz kaldı' veya 'hassas konu'). Kullanıcı bir " +
                     "sebep belirtmediyse 'Kullanıcı açıkça müşteri temsilcisiyle " +
                     "görüşmek istedi.' gibi genel bir ifade yaz.")] string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return ToolResult.ValidationError(
                "Handoff talebi için sebep alanı doldurulmalı.", WellKnown.ToolParameterNames.Reason);
        }

        return ToolResult.Ok(
            message: "Temsilci yönlendirme talebiniz alındı. Kısa süre içinde " +
                     "bir temsilci sizinle iletişime geçecek.",
            data: new
            {
                handoffRequested = true,
                reason = reason.Trim()
            },
            confidence: 1.0);
    }
}