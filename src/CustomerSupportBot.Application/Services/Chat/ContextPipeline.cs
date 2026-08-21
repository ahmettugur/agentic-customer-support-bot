using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Chat;

/// <summary>
/// Kayıtlı tüm <see cref="IContextProvider"/>'ları çalıştırır ve sonuçlarını tek bir bağlam
/// metnine birleştirir.
///
/// <para>
/// Provider'lar <b>paralel</b> koşar (biri diğerini beklemez), sonuç ise <c>Order</c>'a göre
/// <b>deterministik</b> sırada birleşir — böylece prompt turdan tura kaymaz.
/// </para>
///
/// <para>
/// Üç koruma: provider başına <b>zaman aşımı</b>, hata anında <b>kritik/iyileştirici</b>
/// ayrımı ve toplam <b>bütçe</b> tavanı. Gerekçeleri için bkz.
/// <see cref="ContextPipelineOptions"/> ve <see cref="IContextProvider.IsCritical"/>.
/// </para>
/// </summary>
public class ContextPipeline : IContextPipeline
{
    private readonly List<IContextProvider> _providers;
    private readonly ContextPipelineOptions _options;
    private readonly ILogger<ContextPipeline> _logger;

    public ContextPipeline(
        IEnumerable<IContextProvider> providers,
        IOptions<ContextPipelineOptions> options,
        ILogger<ContextPipeline> logger)
    {
        _providers = providers.OrderBy(p => p.Order).ToList();
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ContextResult> BuildContextAsync(
        AgentSession session, string currentQuery, CancellationToken ct = default)
    {
        var outcomes = await Task.WhenAll(_providers.Select(p => RunProviderAsync(p, session, currentQuery, ct)));

        var parts = new List<ContextPart>();
        var placed = new List<(int Order, string Text)>();
        var used = 0;

        // YERLEŞTİRME SIRASI: önce kritik provider'lar, sonra Order.
        //
        // Yalnızca Order'a bakılsaydı, kritik ama Order'ı büyük bir provider bütçe baskısı
        // altında sessizce düşebilirdi — turun tam da hakkında olduğu veri, geç sıraya
        // konduğu için ilk feda edilen şey olurdu. Şu an hiçbir provider IsCritical=true
        // değil (varsayılan false), yani bu sıralama şu an fiilen düz Order sırasıyla
        // aynı sonucu verir; mekanizma ileride kritik bir provider eklenirse devreye girer.
        //
        // Çıktı sırası bundan AYRI tutulur (aşağıda Order'a göre yeniden sıralanır): prompt'un
        // bölüm düzeni yerleştirme önceliğine göre değişmemeli.
        foreach (var o in outcomes
            .OrderByDescending(o => o.Provider.IsCritical)
            .ThenBy(o => o.Provider.Order))
        {
            if (string.IsNullOrWhiteSpace(o.Text))
            {
                parts.Add(new ContextPart(o.Provider.Name, o.Provider.Order, o.Status, 0));
                continue;
            }

            var text = Truncate(o.Text!, _options.MaxProviderChars, o.Provider.Name);

            if (used + text.Length > _options.MaxTotalChars)
            {
                _logger.LogWarning(
                    "Bağlam bütçesi doldu — '{Name}' dışarıda bırakıldı ({Used}/{Max} karakter)",
                    o.Provider.Name, used, _options.MaxTotalChars);
                parts.Add(new ContextPart(o.Provider.Name, o.Provider.Order, ContextPartStatus.Dropped, 0));
                continue;
            }

            placed.Add((o.Provider.Order, text));
            used += text.Length;

            // DURUM KORUNUR. Buraya eskiden koşulsuz Included yazılıyordu; kritik bir
            // provider düştüğünde Degraded(...) bir UYARI METNİ ürettiği için o metin de
            // "dahil edildi" sayılıyor ve asıl Failed/TimedOut bilgisi trace'ten siliniyordu.
            // Yanlış yanıtların sebebi çoğu zaman eksik bağlamdır; o izi kaybetmek, teşhisi
            // imkânsız kılar.
            parts.Add(new ContextPart(o.Provider.Name, o.Provider.Order, o.Status, text.Length));
        }

        var text2 = string.Join("\n\n", placed.OrderBy(p => p.Order).Select(p => p.Text));
        return new ContextResult(text2, parts);
    }

    private async Task<ProviderOutcome> RunProviderAsync(
        IContextProvider provider, AgentSession session, string query, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(_options.ProviderTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var text = await provider.GetContextAsync(session, query, linked.Token);
            return string.IsNullOrWhiteSpace(text)
                ? new ProviderOutcome(provider, null, ContextPartStatus.Empty)
                : new ProviderOutcome(provider, text, ContextPartStatus.Included);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Çağıran turu iptal etti — bu provider'ın sorunu değil, sessizce boş dön.
            return new ProviderOutcome(provider, null, ContextPartStatus.Empty);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Context provider '{Name}' {Seconds}sn içinde tamamlanamadı",
                provider.Name, _options.ProviderTimeoutSeconds);
            return Degraded(provider, ContextPartStatus.TimedOut);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Context provider '{Name}' hata verdi", provider.Name);
            return Degraded(provider, ContextPartStatus.Failed);
        }
    }

    /// <summary>
    /// Kritik bir provider düştüğünde modele <b>eksikliği söyler</b>. Sessizce atlamak,
    /// altyapı hatasını kullanıcıya yanlış olgu olarak yansıtırdı ("siparişiniz bulunamadı").
    /// İyileştirici provider'larda sessiz atlama doğrudur.
    /// </summary>
    private static ProviderOutcome Degraded(IContextProvider provider, ContextPartStatus status) =>
        provider.IsCritical
            ? new ProviderOutcome(provider,
                $"[UYARI] {provider.Name} bilgisi şu anda okunamıyor. Bu konuda kesin " +
                "konuşma; veriye erişemediğini söyle ve tahminde bulunma.",
                status)
            : new ProviderOutcome(provider, null, status);

    private string Truncate(string text, int max, string providerName)
    {
        if (text.Length <= max) return text;
        _logger.LogWarning("Context provider '{Name}' çıktısı kırpıldı ({Actual} → {Max} karakter)",
            providerName, text.Length, max);
        return text[..max] + "\n…(kırpıldı)";
    }

    private sealed record ProviderOutcome(IContextProvider Provider, string? Text, ContextPartStatus Status);
}
