// Core/Model/OrderLineRequest.cs
// order_placement_tool'un LLM'e açılan satır parametresi.

using System.ComponentModel;

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Sipariş oluşturma isteğindeki <b>tek bir istenen satır</b> — LLM'in ürettiği ham girdi.
///
/// <para>
/// <see cref="OrderLine"/>'dan kasıtlı olarak ayrıdır: bu tip <i>talep</i>tir, doğrulanmamıştır
/// (<see cref="ProductName"/> kullanıcının yazdığı serbest metindir, katalogda karşılığı
/// olmayabilir), <see cref="OrderLine"/> ise katalogdan çözülmüş kanonik <i>sonuç</i>tur.
/// İkisini tek tip yapmak, doğrulanmamış bir ürün adının domain'e sızmasına izin verirdi.
/// </para>
///
/// <para>
/// <c>[property: Description]</c> nitelikleri <c>AIFunctionFactory</c>'nin ürettiği JSON
/// şemasına düşer — LLM alanların ne beklediğini buradan öğrenir.
/// </para>
/// </summary>
public sealed record OrderLineRequest(
    [property: Description("Sipariş verilecek ürünün adı")] string ProductName,
    [property: Description("Bu üründen kaç adet isteniyor (en az 1)")] int Quantity);
