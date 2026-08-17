// Tests/Services/CustomerIdentityHintBuilderTests.cs
// Kimlik/tarih system mesajı — yazılı workflow ve sesli native mod AYNI örneği kullanır.
// Bu cümle iki kanalda kopyalanırsa biri güncellenip diğeri kalır (sesli kanalın kimliği hiç
// görmemesi tam olarak böyle bir sapmayla oluşmuştu), bu yüzden tek kaynak burada test edilir.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Tests;

public class CustomerIdentityHintBuilderTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Saat dilimini de sabitler. Paylaşılan <c>FakeTimeProvider</c> yalnızca GetUtcNow'u
    /// override ediyor; builder GetLocalNow kullandığı için makinenin saat dilimi testi
    /// gün sınırında (ör. UTC-10) kırabilirdi.
    /// </summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static CustomerIdentityHintBuilder Build(ICustomerRepository customers)
        => new(customers, new FixedClock(FixedNow));

    private static ICustomerRepository RepoWith(long id, string? fullName)
    {
        var repo = Substitute.For<ICustomerRepository>();
        repo.GetFullNameAsync(id, Arg.Any<CancellationToken>()).Returns(fullName);
        return repo;
    }

    private static AgentSession SessionWith(string? authenticatedId, string? llmExtractedId = null)
    {
        var s = new AgentSession { SessionId = "s1" };
        s.State.AuthenticatedCustomerId = authenticatedId;
        s.State.CustomerId = llmExtractedId;
        return s;
    }

    [Fact]
    public async Task AuthenticatedCustomer_IncludesNameAndDate()
    {
        var hint = await Build(RepoWith(1027, "Ahmet Tügür")).BuildAsync(SessionWith("1027"));

        hint.Should().Contain("Ahmet Tügür");
        hint.Should().Contain("10 Ağustos 2026");
    }

    [Fact]
    public async Task NoAuthenticatedCustomer_StillGivesDate()
    {
        // Tarih her zaman faydalı: "yarın", "bu ay" gibi göreli ifadeler bunun üzerinden yorumlanır.
        var hint = await Build(Substitute.For<ICustomerRepository>()).BuildAsync(SessionWith(null));

        hint.Should().Contain("10 Ağustos 2026");
        hint.Should().NotContain("kimliği doğrulanmış müşteri");
    }

    [Fact]
    public async Task LlmExtractedCustomerId_IsNeverUsedToResolveName()
    {
        // Kullanıcı "ben 1008 numaralı müşteriyim" derse State.CustomerId 1008 olur.
        // Kimlik bundan çözülürse ajan BAŞKASININ adıyla hitap eder — repo hiç sorgulanmamalı.
        var repo = RepoWith(1008, "Başka Müşteri");

        var hint = await Build(repo).BuildAsync(SessionWith(authenticatedId: null, llmExtractedId: "1008"));

        hint.Should().NotContain("Başka Müşteri");
        await repo.DidNotReceive().GetFullNameAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthenticatedIdWins_WhenLlmClaimsAnotherCustomer()
    {
        var repo = Substitute.For<ICustomerRepository>();
        repo.GetFullNameAsync(1027, Arg.Any<CancellationToken>()).Returns("Ahmet Tügür");
        repo.GetFullNameAsync(1008, Arg.Any<CancellationToken>()).Returns("Başka Müşteri");

        var hint = await Build(repo).BuildAsync(SessionWith(authenticatedId: "1027", llmExtractedId: "1008"));

        hint.Should().Contain("Ahmet Tügür");
        hint.Should().NotContain("Başka Müşteri");
    }

    [Theory]
    [InlineData("abc")]      // sayısal değil
    [InlineData("")]         // boş
    public async Task MalformedAuthenticatedId_FallsBackToDateOnly(string id)
    {
        var hint = await Build(Substitute.For<ICustomerRepository>()).BuildAsync(SessionWith(id));

        hint.Should().Contain("10 Ağustos 2026");
        hint.Should().NotContain("kimliği doğrulanmış müşteri");
    }

    [Fact]
    public async Task CustomerNotFoundInDb_FallsBackToDateOnly()
    {
        var hint = await Build(RepoWith(9999, null)).BuildAsync(SessionWith("9999"));

        hint.Should().NotContain("kimliği doğrulanmış müşteri");
        hint.Should().Contain("10 Ağustos 2026");
    }

    [Fact]
    public async Task NullSession_DoesNotThrow()
    {
        var hint = await Build(Substitute.For<ICustomerRepository>()).BuildAsync(null);
        hint.Should().NotBeNullOrWhiteSpace();
    }
}
