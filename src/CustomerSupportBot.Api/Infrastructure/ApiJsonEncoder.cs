// Infrastructure/ApiJsonEncoder.cs
// HTTP JSON yanıtları ve SSE olayları için ortak JSON kodlayıcı.

using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace CustomerSupportBot.Api.Infrastructure;

/// <summary>
/// Türkçe karakterleri (ı, ş, ğ, ü, ö, ç) kaçırmadan yazar, ama HTML'e duyarlı karakterleri
/// (<c>&lt; &gt; &amp; ' "</c>) KAÇIRIR.
///
/// <para>
/// Eskiden <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> kullanılıyordu; adındaki
/// "Unsafe" bu ayrımdır: o kodlayıcı HTML karakterlerini olduğu gibi bırakır. Yanıtlar LLM
/// çıktısı ve müşteri metni taşıyor; bir istemci bu JSON'u bir gün HTML bağlamına (ör. bir
/// &lt;script&gt; bloğu ya da innerHTML) yerleştirirse kaçırılmamış <c>&lt;/script&gt;</c>
/// enjeksiyona dönüşür. Türkçe okunabilirlik için gereken tek şey Unicode aralıklarına izin
/// vermekti — güvenlik kaçışını kapatmak gerekmiyordu.
/// </para>
/// </summary>
internal static class ApiJsonEncoder
{
    public static readonly JavaScriptEncoder Instance = JavaScriptEncoder.Create(UnicodeRanges.All);
}
