// Application/Ports/Driven/Auth/IRefreshTokenRepository.cs
// Secondary port — refresh token persistence.

using CustomerSupportBot.Domain.Model.Auth;

namespace CustomerSupportBot.Application.Ports.Outbound.Auth;

public interface IRefreshTokenRepository
{
    Task CreateAsync(string id, string userId, string tokenHash,
        DateTime expiresAt, DateTime createdAt, CancellationToken ct = default);

    Task<RefreshTokenInfo?> FindByHashAsync(string tokenHash, CancellationToken ct = default);

    Task RevokeAsync(string id, DateTime revokedAt,
        string? replacedByTokenHash, CancellationToken ct = default);

    /// <summary>
    /// Yalnızca kayıt HÂLÂ iptal edilmemişse iptal eder — koşullu sahiplenme.
    ///
    /// <para>
    /// <see cref="RevokeAsync"/>'in aksine oku-değiştir-yaz DEĞİLDİR: tek bir koşullu
    /// UPDATE'tir (<c>WHERE Id = id AND RevokedAt IS NULL</c>). Fark önemlidir — aynı refresh
    /// token'la eşzamanlı gelen iki yenileme isteği ikisi de "hâlâ geçerli" okuyup ikisi de
    /// yeni bir token üretebilirdi; tek bir çalıntı/paylaşılan token'dan iki geçerli oturum
    /// zinciri doğar ve yeniden kullanım tespiti sessizce atlanırdı. Bu metotla yalnızca BİR
    /// çağıran satırı gerçekten değiştirir; kaybeden <c>false</c> alır ve reddedilmelidir.
    /// </para>
    /// </summary>
    /// <returns>Bu çağrı kaydı iptal ettiyse <c>true</c>; kayıt zaten iptal edilmişse <c>false</c>.</returns>
    Task<bool> TryRevokeAsync(string id, DateTime revokedAt,
        string? replacedByTokenHash, CancellationToken ct = default);
}
