// Tests/Services/Auth/BCryptPasswordHasherTests.cs
using CustomerSupportBot.Adapters.Persistence.Auth;

namespace CustomerSupportBot.Api.Tests.Services.Auth;

public class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_ProducesNonEmptyDifferentFromInput()
    {
        var hash = _hasher.Hash("Secret123!");
        hash.Should().NotBeNullOrEmpty();
        hash.Should().NotBe("Secret123!");
    }

    [Fact]
    public void Hash_SameInput_ProducesDifferentHashes()
    {
        var h1 = _hasher.Hash("password");
        var h2 = _hasher.Hash("password");
        h1.Should().NotBe(h2); // BCrypt salt
    }

    [Fact]
    public void Verify_CorrectPassword_True()
    {
        var hash = _hasher.Hash("MySecret#42");
        _hasher.Verify("MySecret#42", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_False()
    {
        var hash = _hasher.Hash("MySecret#42");
        _hasher.Verify("WrongPassword", hash).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_NullOrEmptyHash_False(string? hash)
    {
        _hasher.Verify("anything", hash!).Should().BeFalse();
    }

    [Fact]
    public void Verify_MalformedHash_FalseDoesNotThrow()
    {
        _hasher.Verify("password", "not-a-bcrypt-hash").Should().BeFalse();
    }
}
