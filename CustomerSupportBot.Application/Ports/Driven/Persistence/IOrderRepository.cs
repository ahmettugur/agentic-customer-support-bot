using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driven.Persistence;

/// <summary>
/// Sipariş yönetimi için secondary port.
/// </summary>
public interface IOrderRepository
{
    /// <summary>Yeni sipariş oluşturur ve oluşturulan sipariş ID'sini döner.</summary>
    string Create(OrderInfo order);

    /// <summary>Sipariş ID ile sorgular. Bulunamazsa null döner.</summary>
    OrderInfo? Get(string orderId);

    /// <summary>Müşterinin tüm siparişleri.</summary>
    IReadOnlyList<(string OrderId, OrderInfo Order)> GetByCustomer(string customerId);

    /// <summary>Müşterinin en son siparişi. Yoksa null döner.</summary>
    (string OrderId, OrderInfo Order)? GetLast(string customerId);
}
