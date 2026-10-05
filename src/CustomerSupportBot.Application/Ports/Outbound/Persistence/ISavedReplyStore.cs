// Ports/Outbound/Persistence/ISavedReplyStore.cs

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

public interface ISavedReplyStore
{
    /// <summary>Tüm kayıtlar, sırasız. Sıralama ve arama Türkçe kurallarla serviste yapılır (tablo küçük).</summary>
    Task<IReadOnlyList<SavedReply>> ListAsync(CancellationToken ct = default);

    Task<SavedReply?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>Aynı kısayol (büyük/küçük harf duyarsız) başka bir kayıtta var mı?</summary>
    Task<bool> ShortcutExistsAsync(string shortcut, string? exceptId = null, CancellationToken ct = default);

    /// <summary>Ekler. Kısayol yarışı kaybedilirse (benzersiz indeks) <see cref="SavedReplyShortcutConflictException"/>.</summary>
    Task AddAsync(SavedReply reply, CancellationToken ct = default);

    /// <summary>Günceller; kayıt yoksa <c>false</c>.</summary>
    Task<bool> UpdateAsync(SavedReply reply, CancellationToken ct = default);

    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}

public sealed class SavedReplyShortcutConflictException(string shortcut)
    : Exception($"Bu kısayol zaten kullanılıyor: {shortcut}");
