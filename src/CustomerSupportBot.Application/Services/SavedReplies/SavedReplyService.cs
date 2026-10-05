// Application/Services/SavedReplies/SavedReplyService.cs
// Hazır yanıt kütüphanesi: doğrulama, kısayol benzersizliği, Türkçe sıralama ve arama.

using System.Globalization;
using System.Text.RegularExpressions;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.SavedReplies;

public sealed partial class SavedReplyService(ISavedReplyStore store) : ISavedReplyPort
{
    public const int MaxTitleLength = 100;
    public const int MaxBodyLength = 2000;
    public const int MaxShortcutLength = 40;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly StringComparer TitleOrder = StringComparer.Create(Turkish, ignoreCase: true);

    [GeneratedRegex("^[a-z0-9çğıöşü_-]+$")]
    private static partial Regex ShortcutPattern();

    public async Task<IReadOnlyList<SavedReply>> ListAsync(string? query = null, CancellationToken ct = default)
    {
        var all = await store.ListAsync(ct);
        var q = query?.Trim();
        var filtered = string.IsNullOrEmpty(q)
            ? all
            : all.Where(r => Contains(r.Title, q) || Contains(r.Body, q) || Contains(r.Shortcut, q));
        return filtered.OrderBy(r => r.Title, TitleOrder).ToList();
    }

    public async Task<SavedReplyResult> CreateAsync(SavedReplyInput input, string? createdBy, CancellationToken ct = default)
    {
        if (Validate(input) is { } invalid) return invalid;
        var shortcut = NormalizeShortcut(input.Shortcut);
        if (shortcut is not null && await store.ShortcutExistsAsync(shortcut, null, ct)) return Duplicate(shortcut);

        var now = DateTime.UtcNow;
        var reply = new SavedReply
        {
            Title = input.Title!.Trim(), Body = input.Body!.Trim(), Shortcut = shortcut,
            CreatedBy = createdBy, CreatedAt = now, UpdatedAt = now
        };
        try { await store.AddAsync(reply, ct); }
        catch (SavedReplyShortcutConflictException) { return Duplicate(shortcut!); }   // eşzamanlı ekleme yarışı
        return new SavedReplyResult(SavedReplyStatus.Ok, reply);
    }

    public async Task<SavedReplyResult> UpdateAsync(string id, SavedReplyInput input, CancellationToken ct = default)
    {
        if (Validate(input) is { } invalid) return invalid;
        var existing = await store.GetAsync(id, ct);
        if (existing is null) return new SavedReplyResult(SavedReplyStatus.NotFound, Error: "Hazır yanıt bulunamadı.");

        var shortcut = NormalizeShortcut(input.Shortcut);
        if (shortcut is not null && await store.ShortcutExistsAsync(shortcut, id, ct)) return Duplicate(shortcut);

        existing.Title = input.Title!.Trim();
        existing.Body = input.Body!.Trim();
        existing.Shortcut = shortcut;
        existing.UpdatedAt = DateTime.UtcNow;
        try
        {
            if (!await store.UpdateAsync(existing, ct))
                return new SavedReplyResult(SavedReplyStatus.NotFound, Error: "Hazır yanıt bulunamadı.");
        }
        catch (SavedReplyShortcutConflictException) { return Duplicate(shortcut!); }
        return new SavedReplyResult(SavedReplyStatus.Ok, existing);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => store.DeleteAsync(id, ct);

    private static SavedReplyResult? Validate(SavedReplyInput input)
    {
        var title = input.Title?.Trim();
        var body = input.Body?.Trim();
        if (string.IsNullOrEmpty(title) || title.Length > MaxTitleLength)
            return Invalid($"Başlık 1–{MaxTitleLength} karakter olmalı.");
        if (string.IsNullOrEmpty(body) || body.Length > MaxBodyLength)
            return Invalid($"Metin 1–{MaxBodyLength} karakter olmalı.");
        var shortcut = NormalizeShortcut(input.Shortcut);
        if (shortcut is not null && (shortcut.Length > MaxShortcutLength || !ShortcutPattern().IsMatch(shortcut)))
            return Invalid($"Kısayol en fazla {MaxShortcutLength} karakter; yalnız küçük harf, rakam, '-' ve '_' içerebilir.");
        return null;
    }

    /// <summary>
    /// Boşsa null; değilse kırpılmış ve küçük harfe çevrilmiş. Kısayol bir tanımlayıcıdır: Türkçe kuralla
    /// "IADE" → "ıade" olurdu ve kullanıcının kastettiği "iade" ile eşleşmezdi; bu yüzden I ve İ önce "i"
    /// yapılır, diğer harfler (Ç, Ğ, Ö, Ş, Ü) Türkçe kuralla küçültülür.
    /// </summary>
    private static string? NormalizeShortcut(string? shortcut) =>
        string.IsNullOrWhiteSpace(shortcut)
            ? null
            : shortcut.Trim().Replace('I', 'i').Replace('İ', 'i').ToLower(Turkish);

    private static bool Contains(string? text, string query) =>
        text is not null && Turkish.CompareInfo.IndexOf(text, query, CompareOptions.IgnoreCase) >= 0;

    private static SavedReplyResult Invalid(string error) => new(SavedReplyStatus.Invalid, Error: error);

    private static SavedReplyResult Duplicate(string shortcut) =>
        new(SavedReplyStatus.DuplicateShortcut, Error: $"'{shortcut}' kısayolu başka bir yanıtta kullanılıyor.");
}
