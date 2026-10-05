// Ports/Inbound/ISavedReplyPort.cs
// Hazır yanıt kütüphanesi — okuma herkes (yönetici/temsilci), yazma yalnız yönetici (uç katmanında).

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Inbound;

public interface ISavedReplyPort
{
    Task<IReadOnlyList<SavedReply>> ListAsync(string? query = null, CancellationToken ct = default);
    Task<SavedReplyResult> CreateAsync(SavedReplyInput input, string? createdBy, CancellationToken ct = default);
    Task<SavedReplyResult> UpdateAsync(string id, SavedReplyInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}

public sealed record SavedReplyInput(string? Title, string? Body, string? Shortcut);

public enum SavedReplyStatus { Ok, Invalid, NotFound, DuplicateShortcut }

public sealed record SavedReplyResult(SavedReplyStatus Status, SavedReply? Reply = null, string? Error = null);
