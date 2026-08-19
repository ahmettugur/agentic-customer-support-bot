// Application/Services/A2A/A2ATokenExchangeService.cs
// Partner makine kimliği → tek müşteriye kilitli, kısa ömürlü ÖZNE token'ı.

using CustomerSupportBot.Application.Ports.Outbound.A2A;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.A2A;

/// <summary>A2A kanalında kullanılan roller.</summary>
public static class A2ARoles
{
    /// <summary>
    /// Partner makine kimliği. Müşteri bağımsız ürün ajanını doğrudan çağırabilir; müşteri
    /// verisi döndüren ajanlar için önce tek müşteriye kilitli bir özne token'ı almalıdır.
    /// </summary>
    public const string Partner = "Partner";

    /// <summary>
    /// Değişimle üretilen özne token'ı — tek bir müşteriye kilitlidir ve yalnızca A2A
    /// endpoint'lerinde geçerlidir. Sohbet/sesli kanal <c>"Customer"</c> rolü ister, bu rol
    /// oraya girmez; tersi de geçerlidir. Ayrım bilinçli: bir kanalın token'ı diğerinde
    /// kullanılamasın.
    /// </summary>
    public const string Subject = "A2ASubject";
}

/// <summary>Değişim sonucu.</summary>
public sealed record A2ATokenResult(string AccessToken, DateTime ExpiresAt, string CustomerId);

/// <summary>
/// Partner'ın kendi kimliğiyle kimlik doğrulayıp, belirli bir müşteri adına hareket etmek için
/// <b>kısa ömürlü ve tek müşteriye kilitli</b> bir token aldığı akış (OAuth 2.0 Token Exchange,
/// RFC 8693 deseni).
///
/// <para>
/// <b>Neden partner token'ı doğrudan A2A'da kullanılmıyor:</b> partner token'ı "hangi sistem"
/// sorusunu cevaplar, "hangi müşteri" sorusunu değil. Doğrudan kullanılsaydı müşteri kimliğinin
/// çağrı gövdesinde parametre olarak taşınması gerekirdi — yani tool'ların güvendiği kimlik,
/// istemcinin serbestçe değiştirebildiği bir alan olurdu. Bu, sohbet kanalında bilerek kapatılan
/// açığın (<c>SessionState.CustomerId</c> yerine <c>AuthenticatedCustomerId</c>) A2A'da yeniden
/// açılması demekti. Değişim sonrası müşteri kimliği <b>imzalı token'ın içinde</b> gelir.
/// </para>
///
/// <para>
/// Yetki kararı bu sınıfta değil <see cref="IA2ASubjectAuthorizer"/>'da verilir — o bir iş
/// kuralıdır ve varsayılanı reddetmektir.
/// </para>
/// </summary>
public sealed class A2ATokenExchangeService
{
    private readonly IA2ASubjectAuthorizer _authorizer;
    private readonly IJwtAccessTokenProvider _tokens;
    private readonly A2AOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<A2ATokenExchangeService> _logger;

    public A2ATokenExchangeService(
        IA2ASubjectAuthorizer authorizer,
        IJwtAccessTokenProvider tokens,
        IOptions<A2AOptions> options,
        ILogger<A2ATokenExchangeService> logger,
        TimeProvider? clock = null)
    {
        _authorizer = authorizer;
        _tokens = tokens;
        _options = options.Value;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Yetki varsa özne token'ı üretir, yoksa <c>null</c> döner. Çağıran <c>null</c>'ı
    /// 403 olarak yansıtmalı ve <b>sebebini istemciye açmamalıdır</b> — "bu müşteri yok" ile
    /// "bu müşteriye yetkin yok" ayrımı, dışarıdan müşteri numarası taraması yapmayı kolaylaştırır.
    /// </summary>
    public async Task<A2ATokenResult?> ExchangeAsync(
        string partnerId, string customerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(partnerId) || string.IsNullOrWhiteSpace(customerId))
            return null;

        if (!await _authorizer.CanActForCustomerAsync(partnerId, customerId, ct).ConfigureAwait(false))
            return null;

        // Sentetik kimlik: kalıcı bir kullanıcı kaydı DEĞİL, yalnızca token'ın taşıyacağı
        // iddiaların kabı. Id'ye partner de yazılır ki denetim kaydında "hangi partner adına
        // üretildi" görünsün.
        var subject = new UserInfo(
            Id: A2ASubjectIdentity.BuildId(partnerId, customerId),
            Username: $"a2a:{partnerId}",
            PasswordHash: "",
            Role: A2ARoles.Subject,
            LinkedAgentId: null,
            IsActive: true,
            CreatedAt: _clock.GetUtcNow().UtcDateTime,
            LastLoginAt: null,
            LinkedCustomerId: customerId);

        var lifetime = Math.Max(1, _options.SubjectTokenMinutes);
        var (token, expiresAt) = _tokens.GenerateAccessToken(subject, _clock.GetUtcNow().UtcDateTime, lifetime);

        _logger.LogInformation(
            "[A2A] Özne token'ı üretildi partner={PartnerId} customer={CustomerId} ömür={Minutes}dk",
            partnerId, customerId, lifetime);

        return new A2ATokenResult(token, expiresAt, customerId);
    }
}
