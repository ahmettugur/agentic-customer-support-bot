// Application/Services/Chat/OutputGuardrailService.cs
// OutputGuardrail'in bağlamını (müşterinin kendi e-postası, yazdıkları, izinli iletişimler) toplayan servis.

using System.Diagnostics.Metrics;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Chat;

public sealed class OutputGuardrailOptions
{
    public const string SectionName = "OutputGuardrail";

    public bool Enabled { get; set; } = true;

    // Config binder tuzağı: önceden doldurulmuş liste verilen değerleri üzerine EKLER → null varsayılan + Effective.
    public List<string>? AllowedContacts { get; set; }
    public List<string>? BlockedPhrases { get; set; }

    public IReadOnlyList<string> EffectiveAllowedContacts => AllowedContacts ?? [];
    public IReadOnlyList<string> EffectiveBlockedPhrases => BlockedPhrases ?? [];
}

public sealed class OutputGuardrailService(
    ICustomerRepository customers,
    IOptions<OutputGuardrailOptions> options,
    IOptions<EmailOptions> email,
    ILogger<OutputGuardrailService> logger)
{
    private static readonly Meter Meter = new(TelemetryConstants.MeterName, "1.0.0");
    private static readonly Counter<long> Verdicts = Meter.CreateCounter<long>(
        "chat.output_guardrail", unit: "{answer}", description: "Çıktı guardrail'inin müdahale ettiği yanıtlar (verdict, finding).");

    private readonly OutputGuardrailOptions _options = options.Value;

    /// <summary>
    /// Kısa, serbest metin alanları (ör. reasoning analizi) için tek seferlik bağlamlı temizleyici: kayıtlı e-postayı bir kez okur, sonra
    /// her metni <see cref="OutputGuardrail"/>'dan geçirir. Maskelenecek kişisel veri varsa <c>[gizlendi]</c> görünür; sır/istem sızıntısı
    /// (Blocked) varsa alanın TAMAMI <c>[gizlendi]</c> olur. Kapalıysa metni olduğu gibi döner.
    /// </summary>
    public async Task<Func<string, string>> CreateTextSanitizerAsync(
        string query, IReadOnlyList<ConversationMessage> history, string? customerId, CancellationToken ct)
    {
        if (!_options.Enabled) return s => s;

        string? ownEmail = null;
        if (long.TryParse(customerId, out var id))
        {
            try { ownEmail = await customers.GetEmailAsync(id, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "[Guardrail] Müşteri e-postası okunamadı; temizleyici e-postasız sürüyor");
            }
        }

        var allowed = new List<string>(_options.EffectiveAllowedContacts);
        if (!string.IsNullOrWhiteSpace(email.Value.FromAddress)) allowed.Add(email.Value.FromAddress);
        var userTexts = history.Where(m => m.Role == ConversationRoles.User).Select(m => m.Text).Append(query).ToList();
        var context = new OutputGuardContext(userTexts, ownEmail, allowed, _options.EffectiveBlockedPhrases);

        return text =>
        {
            var result = OutputGuardrail.Inspect(text, context);
            return result.Verdict switch
            {
                OutputGuardVerdict.Clean => text,
                OutputGuardVerdict.Redacted => result.Text,
                _ => OutputGuardrail.RedactedMark
            };
        };
    }

    public async Task<OutputGuardResult> InspectAsync(
        string answer, string query, IReadOnlyList<ConversationMessage> history, string? customerId, CancellationToken ct)
    {
        if (!_options.Enabled) return new(answer, OutputGuardVerdict.Clean, []);

        string? ownEmail = null;
        if (long.TryParse(customerId, out var id))
        {
            try { ownEmail = await customers.GetEmailAsync(id, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Arama başarısızsa kapı yine çalışır — e-posta bilinmediği için fazla maskelemek, hiç kontrol etmemekten iyidir.
                logger.LogWarning(ex, "[Guardrail] Müşteri e-postası okunamadı; kontrol e-postasız sürüyor");
            }
        }

        var allowed = new List<string>(_options.EffectiveAllowedContacts);
        if (!string.IsNullOrWhiteSpace(email.Value.FromAddress)) allowed.Add(email.Value.FromAddress);

        var userTexts = history.Where(m => m.Role == ConversationRoles.User).Select(m => m.Text).Append(query).ToList();
        var result = OutputGuardrail.Inspect(answer, new OutputGuardContext(userTexts, ownEmail, allowed, _options.EffectiveBlockedPhrases));

        if (result.Verdict != OutputGuardVerdict.Clean)
        {
            // İçerik loglanmaz (kendisi PII/sır olabilir); yalnızca neyin yakalandığı.
            logger.LogWarning("[Guardrail] Yanıta müdahale edildi | verdict={Verdict} findings={Findings}",
                result.Verdict, string.Join(',', result.Findings));
            foreach (var f in result.Findings)
                Verdicts.Add(1, new KeyValuePair<string, object?>("verdict", result.Verdict.ToString().ToLowerInvariant()),
                    new KeyValuePair<string, object?>("finding", f));
        }
        return result;
    }
}
