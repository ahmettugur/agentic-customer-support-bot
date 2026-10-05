// Application/Services/Budget/LlmSpendGuard.cs
// LLM harcama limiti: kontrol, sayım, eşik uyarısı, panel durumu.

using System.Globalization;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Budget;

/// <summary>
/// <see cref="ILlmSpendGuard"/> uygulaması.
///
/// <para>
/// <b>Sayaçlar</b> pod'lar arası ortaktır (<see cref="ILlmSpendCounter"/>, Redis): gün, ay ve görüşme. Gün/ay
/// sayacı yoksa (Redis yeniden başladı ya da dönem yeni) <c>llm_call_usage</c> toplamıyla tohumlanır — aksi
/// hâlde bir Redis kesintisi dönemin harcamasını sıfırlar ve limit fiilen iki katına çıkardı.
/// </para>
///
/// <para>
/// <b>Fail-open:</b> sayaç ya da veritabanı hatası loglanır, çağrı engellenmez. Bütçe kontrolü bir koruma
/// katmanıdır; Redis hıçkırığı müşteri hizmetini durdurmamalı.
/// </para>
///
/// <para>
/// <b>Uyarılar</b> yalnızca eşiği geçen artışta verilir (önceki &lt; eşik ≤ yeni). Atomik artış yeni toplamı
/// döndürdüğü için eşiği tam olarak bir artış geçer; <see cref="INotificationLedger"/> yine de çok pod'da tek
/// bildirimi garanti eder.
/// </para>
/// </summary>
public sealed class LlmSpendGuard(
    IOptionsMonitor<LlmBudgetOptions> options,
    ILlmSpendCounter counter,
    ILogger<LlmSpendGuard> logger,
    TimeProvider? clock = null,
    ILlmCallPersistencePort? persistence = null,
    INotificationLedger? ledger = null,
    IEmailSender? email = null) : ILlmSpendGuard
{
    private static readonly TimeSpan DayTtl = TimeSpan.FromDays(2);
    private static readonly TimeSpan MonthTtl = TimeSpan.FromDays(40);
    private static readonly TimeSpan SessionTtl = TimeSpan.FromDays(7);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private sealed record Period(string Name, string Id, string Label, DateTime StartUtc, TimeSpan Ttl, decimal LimitUsd, LlmBudgetScope Scope)
    {
        public string Key => $"llm-spend:{Name}:{Id}";
    }

    private Period[] Periods(LlmBudgetOptions o)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var day = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return
        [
            new("day", day.ToString("yyyyMMdd", CultureInfo.InvariantCulture), "günlük", day, DayTtl, o.DailyLimitUsd, LlmBudgetScope.Daily),
            new("month", month.ToString("yyyyMM", CultureInfo.InvariantCulture), "aylık", month, MonthTtl, o.MonthlyLimitUsd, LlmBudgetScope.Monthly)
        ];
    }

    private static string SessionKey(string sessionId) => $"llm-spend:session:{sessionId}";

    public async Task<LlmBudgetExceeded?> CheckAsync(string? sessionId, CancellationToken ct = default)
    {
        var o = options.CurrentValue;
        if (!o.Enabled) return null;
        try
        {
            foreach (var p in Periods(o))
            {
                if (p.LimitUsd <= 0) continue;
                var spent = await SpentAsync(p, ct);
                if (spent >= p.LimitUsd) return new LlmBudgetExceeded(p.Scope, p.LimitUsd, spent);
            }
            if (sessionId is not null && o.PerConversationLimitUsd > 0)
            {
                var spent = await counter.GetAsync(SessionKey(sessionId), ct) ?? 0m;
                if (spent >= o.PerConversationLimitUsd)
                    return new LlmBudgetExceeded(LlmBudgetScope.Conversation, o.PerConversationLimitUsd, spent);
            }
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[Budget] Harcama sayacı okunamadı — kontrol atlandı (fail-open)");
            return null;
        }
    }

    public async Task RecordAsync(decimal costUsd, string? sessionId, CancellationToken ct = default)
    {
        var o = options.CurrentValue;
        if (!o.Enabled || costUsd <= 0) return;
        try
        {
            foreach (var p in Periods(o))
            {
                await SpentAsync(p, ct);   // tohumlanmamış sayaca yalnızca bu çağrının maliyeti yazılmasın
                var total = await counter.AddAsync(p.Key, costUsd, p.Ttl, ct);
                if (p.LimitUsd > 0) await AlertIfCrossedAsync(o, p, total - costUsd, total, ct);
            }
            if (sessionId is not null)
                await counter.AddAsync(SessionKey(sessionId), costUsd, SessionTtl, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[Budget] Harcama sayılamadı | cost={Cost} session={SessionId}", costUsd, sessionId);
        }
    }

    public async Task<LlmBudgetStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var o = options.CurrentValue;
        decimal day = 0, month = 0;
        if (o.Enabled)
        {
            try
            {
                var periods = Periods(o);
                day = await SpentAsync(periods[0], ct);
                month = await SpentAsync(periods[1], ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "[Budget] Harcama durumu okunamadı");
            }
        }
        return new LlmBudgetStatus(o.Enabled, o.DailyLimitUsd, day, o.MonthlyLimitUsd, month,
            o.PerConversationLimitUsd, o.WarningThresholdPercent);
    }

    /// <summary>Dönem sayacı; yoksa veritabanındaki dönem toplamıyla tohumlanır.</summary>
    private async Task<decimal> SpentAsync(Period p, CancellationToken ct)
    {
        if (await counter.GetAsync(p.Key, ct) is { } spent) return spent;
        var seed = persistence is null ? 0m : await persistence.GetTotalCostSinceAsync(p.StartUtc, ct);
        await counter.SeedAsync(p.Key, seed, p.Ttl, ct);
        return await counter.GetAsync(p.Key, ct) ?? seed;
    }

    private async Task AlertIfCrossedAsync(LlmBudgetOptions o, Period p, decimal previous, decimal total, CancellationToken ct)
    {
        var percent = Math.Clamp(o.WarningThresholdPercent, 0, 100);
        var warningAt = p.LimitUsd * percent / 100m;
        if (percent is > 0 and < 100 && previous < warningAt && total >= warningAt)
            await AlertAsync(o, p, "warning",
                $"LLM bütçesi uyarısı: {p.Label} harcama %{percent} eşiğini geçti", total, ct);
        if (previous < p.LimitUsd && total >= p.LimitUsd)
            await AlertAsync(o, p, "limit", $"LLM bütçesi: {p.Label} limit doldu", total, ct);
    }

    private async Task AlertAsync(LlmBudgetOptions o, Period p, string level, string subject, decimal total, CancellationToken ct)
    {
        var key = $"llm-budget:{p.Name}:{p.Id}:{level}";
        if (ledger is not null && !await ledger.TryClaimAsync(key, ct)) return;

        var advice = level == "limit"
            ? "Limit aşıldı: yeni LLM çağrıları dönem sonuna kadar yapılmaz, müşteriler bilgilendirme mesajı alır."
            : "Limite yaklaşılıyor. Gerekirse LlmBudget ayarlarını gözden geçirin.";
        var body = string.Create(CultureInfo.InvariantCulture,
            $"{subject}.\n\nDönem: {p.Id} (UTC)\nHarcama: ${total:F2}\nLimit: ${p.LimitUsd:F2}\n\n{advice}");
        if (level == "limit")
            logger.LogError("[Budget] {Subject} | harcama={Spent} limit={Limit}", subject, total, p.LimitUsd);
        else
            logger.LogWarning("[Budget] {Subject} | harcama={Spent} limit={Limit}", subject, total, p.LimitUsd);

        var recipients = o.AlertEmails.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (email is null || !email.IsEnabled || recipients.Count == 0) return;
        try
        {
            foreach (var to in recipients)
                await email.SendAsync(new EmailMessage(to, null, subject, body), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[Budget] Bütçe uyarısı e-postası gönderilemedi | key={Key}", key);
            if (ledger is not null) await ledger.ReleaseAsync(key, ct);
        }
    }
}
