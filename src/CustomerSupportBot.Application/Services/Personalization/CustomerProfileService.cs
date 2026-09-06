// Application/Services/Personalization/CustomerProfileService.cs
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
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Locking;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Personalization;

public sealed partial class CustomerProfileService : ICustomerProfileService
{
    private const int MaxProductInterests = 10;
    private const int MaxRecentRatings = 10;
    private const int MaxIntents = 20;
    private const int MaxTraits = 5;

    private readonly ICustomerProfileStore _store;
    private readonly IGeneralChatClient _chatClient;
    private readonly IAppDistributedLock _distributedLock;
    private readonly IProductCatalogRepository _products;
    private readonly ILogger<CustomerProfileService> _logger;

    public CustomerProfileService(
        ICustomerProfileStore store,
        IGeneralChatClient chatClient,
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
    /// <summary>
    /// Admin tetikler; profili LLM ile özetler ve <see cref="CustomerProfile.Traits"/>'i
    /// yeniden üretir.
    ///
    /// <para>
    /// <b>Traits BİRİKMEZ — her çağrıda baştan üretilir.</b> Bilinçli tasarım kararı: profil
    /// artımlı delta değil, birikmiş SAYAÇLARDAN (<c>IntentFrequency</c>, <c>ProductInterests</c>,
    /// <c>RecentRatings</c>) oluşuyor — <c>BuildConsolidatePrompt</c> her seferinde TÜM birikmiş
    /// veriyi LLM'e veriyor. Trait'leri biriktirseydik, 6 ay önce doğru olup artık geçersiz olan
    /// bir iddia (ör. "yeni müşteri, az veri var") sonsuza kadar profilde kalırdı ve yenileriyle
    /// çelişirdi. Bunun bedeli: iki consolidate arasında trait sayısı artmaz, önceki traits'in
    /// <c>Confidence</c>'ı ne olursa olsun sıfırlanır.
    /// </para>
    /// </summary>
    public async Task<CustomerProfile?> ConsolidateAsync(string customerId, CancellationToken ct = default)
    {
        var profile = _store.Get(customerId);
        if (profile == null) return null;
        if (profile.TotalTurns == 0) return profile;

        var prompt = BuildConsolidatePrompt(profile);
        try
        {
            var messages = new List<ConversationMessage>
            {
                new(ConversationRoles.System,
                    "Sen müşteri profili özetleyicisisin. SADECE geçerli JSON dön: " +
                    "{\"summary\":\"...\",\"preferredTone\":\"formal|casual|concise|verbose|neutral\"," +
                    "\"traits\":[{\"claim\":\"...\",\"confidence\":0.0}]}. " +
                    "Summary 1-2 cümle, Türkçe. " +
                    "traits: müşteri davranışından ÇIKARDIĞIN gözlemler (ör. \"Fiyat hassasiyeti yüksek\", " +
                    "\"Teknik detaylara önem veriyor\") — en fazla 5 tane, yalnızca veriden gerçekten " +
                    "desteklenenleri ekle, veri yetersizse boş dizi dön. confidence 0.0-1.0 arası, " +
                    "verinin ne kadar güçlü desteklediğine göre DÜRÜST bir tahmin — az veri varsa düşük " +
                    "confidence ver, uydurma."),
                new(ConversationRoles.User, prompt)
            };
            var responseText = await _chatClient.CompleteAsync(messages, ct);

            var json = ExtractJson(responseText);
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogWarning("Profil consolidate: LLM JSON dönmedi. customerId={Id}", customerId);
                return profile;
            }

            using var doc = JsonDocument.Parse(json);
            var summary = doc.RootElement.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString() : null;
            var tone = doc.RootElement.TryGetProperty("preferredTone", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() : null;
            var traits = ParseTraits(doc.RootElement, customerId, profile.TotalTurns);

            await using var handle = await _distributedLock.AcquireAsync($"profile:{customerId}", ct: ct);
            return await _store.UpdateConsolidationAsync(customerId, summary, tone, traits, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
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

    /// <summary>
    /// LLM'in <c>traits</c> dizisini <see cref="InferredTrait"/> listesine çevirir.
    /// Savunmacı: boş/eksik <c>claim</c> atlanır, <c>confidence</c> [0,1] aralığına
    /// kırpılır (LLM 0-1 dışı veya string döndürebilir), <see cref="MaxTraits"/> ile sınırlanır.
    /// LLM çıktısı güvenilmez veri olarak ele alınır — burada çökmek, profilin geri kalanının
    /// (Summary/PreferredTone zaten yazılmış) kaybolmasına yol açmamalı.
    /// </summary>
    internal static List<InferredTrait> ParseTraits(JsonElement root, string customerId, int totalTurns)
    {
        var result = new List<InferredTrait>();
        if (!root.TryGetProperty("traits", out var traitsEl) || traitsEl.ValueKind != JsonValueKind.Array)
            return result;

        var source = $"consolidate:{customerId}@turn{totalTurns}";
        var now = DateTime.UtcNow;

        foreach (var t in traitsEl.EnumerateArray())
        {
            if (result.Count >= MaxTraits) break;
            if (t.ValueKind != JsonValueKind.Object) continue;
            if (!t.TryGetProperty("claim", out var claimEl) || claimEl.ValueKind != JsonValueKind.String)
                continue;
            var claim = claimEl.GetString();
            if (string.IsNullOrWhiteSpace(claim)) continue;

            double confidence = 0.5; // LLM confidence vermezse "belirsiz" — ne yüksek ne düşük.
            if (t.TryGetProperty("confidence", out var confEl))
            {
                if (confEl.ValueKind == JsonValueKind.Number && confEl.TryGetDouble(out var c))
                    confidence = c;
                else if (confEl.ValueKind == JsonValueKind.String && double.TryParse(confEl.GetString(), out var cs))
                    confidence = cs;
            }
            confidence = Math.Clamp(confidence, 0.0, 1.0);

            result.Add(new InferredTrait(claim.Trim(), confidence, source, now));
        }

        return result;
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
