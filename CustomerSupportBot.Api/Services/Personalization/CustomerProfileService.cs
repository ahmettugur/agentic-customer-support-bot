using CustomerSupportBot.Application.Ports.Driven.Persistence;
// Services/Personalization/CustomerProfileService.cs
// Per-customer kişiselleştirme servisi.
//
// İki güncelleme yolu var:
//
// 1) RecordInteractionAsync (her workflow turunda otomatik) — DETERMİNİSTİK.
//    Sıfır LLM maliyeti. Niyet frekansı, ürün ilgi alanları, dil ve son
//    rating gibi alanları kural tabanlı günceller.
//
// 2) ConsolidateAsync (admin tetikler) — LLM ÇAĞRISI.
//    Toplanan ham veriden kısa "Summary" + "PreferredTone" üretir; sonuç
//    profilin özet alanlarına yazılır. Düzenli iş yükü değil; admin elle
//    veya N turda bir tetikler.
//
// Tasarım kararı: episodik bellek yazımı zaten her turda LLM çağırmıyor;
// profil de aynı maliyet sınıfında kalmalı. LLM consolidate sadece talep
// üzerine çalışır.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;
using CustomerSupportBot.Api.Services.Locking;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Api.Services.Personalization;

public sealed partial class CustomerProfileService
{
    private const int MaxProductInterests = 10;
    private const int MaxRecentRatings = 10;
    private const int MaxIntents = 20;

    private readonly ICustomerProfileStore _store;
    private readonly IChatClient _chatClient;
    private readonly IAppDistributedLock _distributedLock;
    private readonly IProductCatalogRepository _products;
    private readonly ILogger<CustomerProfileService> _logger;

    public CustomerProfileService(
        ICustomerProfileStore store,
        IChatClient chatClient,
        IAppDistributedLock distributedLock,
        IProductCatalogRepository products,
        ILogger<CustomerProfileService> logger)
    {
        _store = store;
        _chatClient = chatClient;
        _distributedLock = distributedLock;
        _products = products;
        _logger = logger;
    }

    /// <summary>
    /// Bir oturum turunun bitiminde çağrılır. Heuristik (LLM-siz) profil güncellemesi yapar.
    /// Distributed lock ile per-customer serialize edilir.
    /// </summary>
    public async Task<CustomerProfile?> RecordInteractionAsync(
        string? customerId,
        string userQuery,
        string botResponse,
        string? intent,
        int? rating = null,
        bool isNewSession = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(customerId) || string.IsNullOrWhiteSpace(userQuery))
            return null;

        await using var handle = await _distributedLock
            .AcquireAsync($"profile:{customerId}", ct: ct)
            .ConfigureAwait(false);

        return RecordInteractionCore(customerId, userQuery, botResponse, intent, rating, isNewSession);
    }

    private CustomerProfile? RecordInteractionCore(
        string customerId,
        string userQuery,
        string botResponse,
        string? intent,
        int? rating,
        bool isNewSession)
    {
        var profile = _store.GetOrCreate(customerId);

        profile.TotalTurns++;
        if (isNewSession) profile.TotalSessions++;
        profile.LastInteractionAt = DateTime.UtcNow;

        // Niyet frekansı
        if (!string.IsNullOrWhiteSpace(intent))
        {
            profile.IntentFrequency[intent] = profile.IntentFrequency.GetValueOrDefault(intent) + 1;
            // Çok büyürse en az kullanılan girdiyi at
            if (profile.IntentFrequency.Count > MaxIntents)
            {
                var weakest = profile.IntentFrequency.OrderBy(kv => kv.Value).First().Key;
                profile.IntentFrequency.Remove(weakest);
            }
        }

        // Ürün ilgi alanları (sorudan + yanıttan basit ekstraksiyon)
        var products = ExtractProductMentions(userQuery + " " + botResponse);
        foreach (var p in products)
        {
            profile.ProductInterests.Remove(p);          // dedupe
            profile.ProductInterests.Insert(0, p);       // en yeni başta
        }
        if (profile.ProductInterests.Count > MaxProductInterests)
            profile.ProductInterests.RemoveRange(MaxProductInterests, profile.ProductInterests.Count - MaxProductInterests);

        // Dil tespiti (yalın heuristik — Türkçe karakter veya yaygın kelime varsa "tr")
        if (LooksTurkish(userQuery)) profile.PreferredLanguage = "tr";
        else if (LooksEnglish(userQuery)) profile.PreferredLanguage = "en";

        // Rating
        if (rating is int r && r is >= 1 and <= 5)
        {
            profile.RecentRatings.Add(r);
            if (profile.RecentRatings.Count > MaxRecentRatings)
                profile.RecentRatings.RemoveAt(0);
        }

        _store.Upsert(profile);
        return profile;
    }

    /// <summary>
    /// LLM ile profilin "Summary" + "PreferredTone" alanlarını günceller.
    /// </summary>
    public async Task<CustomerProfile?> ConsolidateAsync(string customerId, CancellationToken ct = default)
    {
        var profile = _store.Get(customerId);
        if (profile == null) return null;
        if (profile.TotalTurns == 0) return profile;

        var prompt = BuildConsolidatePrompt(profile);
        try
        {
            var resp = await _chatClient.GetResponseAsync(
                new[]
                {
                    new ChatMessage(ChatRole.System,
                        "Sen müşteri profili özetleyicisisin. SADECE geçerli JSON dön: " +
                        "{\"summary\":\"...\",\"preferredTone\":\"formal|casual|concise|verbose|neutral\"}. " +
                        "Summary 1-2 cümle, Türkçe."),
                    new ChatMessage(ChatRole.User, prompt)
                },
                options: null,
                cancellationToken: ct);

            var json = ExtractJson(resp.Text ?? "");
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogWarning("Profil consolidate: LLM JSON dönmedi. customerId={Id}", customerId);
                return profile;
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("summary", out var s))
                profile.Summary = s.GetString();
            if (doc.RootElement.TryGetProperty("preferredTone", out var t))
                profile.PreferredTone = t.GetString() ?? profile.PreferredTone;

            profile.LastConsolidatedAt = DateTime.UtcNow;
            _store.Upsert(profile);
            return profile;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Profil consolidate başarısız oldu (customerId={Id})", customerId);
            return profile;
        }
    }

    // ─── Internals ───

    private static string BuildConsolidatePrompt(CustomerProfile p)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Müşteri: {p.CustomerId}");
        sb.AppendLine($"Toplam oturum: {p.TotalSessions}, toplam tur: {p.TotalTurns}");
        sb.AppendLine($"Tercih edilen dil: {p.PreferredLanguage}");

        if (p.IntentFrequency.Count > 0)
        {
            var top = p.IntentFrequency.OrderByDescending(kv => kv.Value).Take(5)
                .Select(kv => $"{kv.Key}({kv.Value})");
            sb.AppendLine("En sık niyetler: " + string.Join(", ", top));
        }
        if (p.ProductInterests.Count > 0)
        {
            sb.AppendLine("İlgilendiği ürünler: " + string.Join(", ", p.ProductInterests.Take(5)));
        }
        if (p.RecentRatings.Count > 0)
        {
            sb.AppendLine($"Son puanlar: {string.Join(",", p.RecentRatings)} (ort. {p.RecentRatings.Average():F1}/5)");
        }
        sb.AppendLine();
        sb.AppendLine("Bu bilgilere bakarak müşteri için 1-2 cümlelik kısa bir profil özeti ve uygun ton önerisi üret. JSON dön.");
        return sb.ToString();
    }

    internal static string? ExtractJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // ```json ... ``` veya ham JSON
        var fenceMatch = JsonFenceRegex().Match(text);
        if (fenceMatch.Success) return fenceMatch.Groups[1].Value.Trim();

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start) return text[start..(end + 1)];
        return null;
    }

    internal IReadOnlyList<string> ExtractProductMentions(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        var found = new List<string>();
        foreach (var key in _products.GetAll().Select(p => p.Name).Where(n => !string.IsNullOrEmpty(n)))
        {
            if (text.Contains(key, StringComparison.OrdinalIgnoreCase) && !found.Contains(key))
                found.Add(key);
        }
        return found;
    }

    private static readonly HashSet<char> _turkishChars =
        new("çğıöşüÇĞİÖŞÜ");

    internal static bool LooksTurkish(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (text.Any(c => _turkishChars.Contains(c))) return true;
        var lowered = text.ToLowerInvariant();
        return TurkishKeywordRegex().IsMatch(lowered);
    }

    internal static bool LooksEnglish(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (text.Any(c => _turkishChars.Contains(c))) return false;
        var lowered = text.ToLowerInvariant();
        return EnglishKeywordRegex().IsMatch(lowered);
    }

    [GeneratedRegex(@"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase)]
    private static partial Regex JsonFenceRegex();

    [GeneratedRegex(@"\b(merhaba|sipariş|şikayet|teşekkür|nerede|nasıl|nedir|var mı|lütfen|iade)\b")]
    private static partial Regex TurkishKeywordRegex();

    [GeneratedRegex(@"\b(hello|order|where|how|please|thanks|thank you|return|complaint)\b")]
    private static partial Regex EnglishKeywordRegex();
}

