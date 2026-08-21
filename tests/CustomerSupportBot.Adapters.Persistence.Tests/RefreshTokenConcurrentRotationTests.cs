// Refresh token rotasyonunun EŞZAMANLI GÜVENLİĞİ.
//
// Yenileme akışı önce token'ın hâlâ geçerli olduğunu okur, sonra iptal edip yenisini yazar.
// Bu oku-değiştir-yaz olduğu sürece bir yarış vardır: aynı token'la (çalıntı ya da paylaşılmış)
// eşzamanlı gelen iki istek ikisi de "hâlâ geçerli" okuyabilir ve ikisi de yeni bir token
// üretebilir. Sonuç tek bir token'dan iki geçerli oturum zinciri — ve rotasyonun asıl amacı
// olan "yeniden kullanım = hırsızlık" tespiti sessizce atlanmış olur.
//
// Test gerçek Postgres'e karşı koşar; ExecuteUpdateAsync'in koşullu etkisini EF InMemory
// provider'da gözlemlemenin bir yolu yok.

using CustomerSupportBot.Adapters.Persistence.EfCore.Auth;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Auth;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class RefreshTokenConcurrentRotationTests
{
    private readonly PostgresCatalogFixture _fixture;

    public RefreshTokenConcurrentRotationTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    private EfRefreshTokenRepository NewRepo() => new(_fixture.DbFactory);

    private async Task<string> NewTokenAsync()
    {
        await using var ctx = _fixture.DbFactory.CreateDbContext();
        var userId = Guid.NewGuid().ToString("N");
        ctx.Users.Add(new UserEntity
        {
            Id = userId, Username = $"{userId}@example.com", PasswordHash = "hash",
            Role = "Customer", IsActive = true, CreatedAt = DateTime.UtcNow
        });
        var tokenId = Guid.NewGuid().ToString("N");
        ctx.RefreshTokens.Add(new RefreshTokenEntity
        {
            Id = tokenId, UserId = userId, TokenHash = $"hash-{Guid.NewGuid():N}",
            ExpiresAt = DateTime.UtcNow.AddDays(7), CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return tokenId;
    }

    /// <summary>
    /// ASIL BULGU. Aynı token için iki eşzamanlı iptal isteği: yalnızca BİRİ gerçekten
    /// iptal etmiş sayılmalı. İkisi de başarılı dönerse, çağıran taraf ikisinden de yeni
    /// token üretir — tek token'dan iki geçerli zincir.
    /// </summary>
    [Fact]
    public async Task ConcurrentRevokeAttempts_OnlyOneSucceeds()
    {
        var tokenId = await NewTokenAsync();
        var repoA = NewRepo();
        var repoB = NewRepo();

        var resultA = repoA.TryRevokeAsync(tokenId, DateTime.UtcNow, "hash-a", TestContext.Current.CancellationToken);
        var resultB = repoB.TryRevokeAsync(tokenId, DateTime.UtcNow, "hash-b", TestContext.Current.CancellationToken);

        var results = await Task.WhenAll(resultA, resultB);

        results.Count(r => r).Should().Be(1, "iki eşzamanlı çağrıdan yalnızca biri gerçekten iptal etmiş olmalı");
    }

    /// <summary>Zaten iptal edilmiş bir token'ı tekrar iptal etmeye çalışmak false dönmeli.</summary>
    [Fact]
    public async Task RevokingAnAlreadyRevokedToken_ReturnsFalse()
    {
        var tokenId = await NewTokenAsync();
        var repo = NewRepo();

        (await repo.TryRevokeAsync(tokenId, DateTime.UtcNow, "hash-1", TestContext.Current.CancellationToken))
            .Should().BeTrue();
        (await repo.TryRevokeAsync(tokenId, DateTime.UtcNow, "hash-2", TestContext.Current.CancellationToken))
            .Should().BeFalse();
    }

    /// <summary>Karşı yön: normal (eşzamanlı olmayan) rotasyon hâlâ çalışmalı.</summary>
    [Fact]
    public async Task SingleRevoke_Succeeds()
    {
        var tokenId = await NewTokenAsync();
        var repo = NewRepo();

        (await repo.TryRevokeAsync(tokenId, DateTime.UtcNow, "hash-1", TestContext.Current.CancellationToken))
            .Should().BeTrue();

        await using var ctx = _fixture.DbFactory.CreateDbContext();
        var row = await ctx.RefreshTokens.AsNoTracking()
            .FirstAsync(t => t.Id == tokenId, TestContext.Current.CancellationToken);
        row.RevokedAt.Should().NotBeNull();
        row.ReplacedByTokenHash.Should().Be("hash-1");
    }
}
