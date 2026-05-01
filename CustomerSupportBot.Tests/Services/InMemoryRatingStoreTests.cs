// Tests/Services/InMemoryRatingStoreTests.cs
using CustomerSupportBot.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Tests.Services;

public class InMemoryRatingStoreTests
{
    private readonly InMemoryRatingStore _store = new(NullLogger<InMemoryRatingStore>.Instance);

    [Fact]
    public void Submit_StarsClampedHigh()
    {
        var r = _store.Submit("s1", 10, "great");
        r.Stars.Should().Be(5);
    }

    [Fact]
    public void Submit_StarsClampedLow()
    {
        var r = _store.Submit("s1", 0, null);
        r.Stars.Should().Be(1);
    }

    [Fact]
    public void Submit_PersistsAndOverwrites()
    {
        _store.Submit("s1", 3, "ok");
        _store.Submit("s1", 5, "great");
        var r = _store.GetBySession("s1");
        r!.Stars.Should().Be(5);
        r.Feedback.Should().Be("great");
    }

    [Fact]
    public void GetBySession_Unknown_Null()
    {
        _store.GetBySession("nope").Should().BeNull();
    }

    [Fact]
    public void GetAll_ReturnsAllRatings()
    {
        _store.Submit("s1", 4, null);
        _store.Submit("s2", 3, null);
        _store.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void GetRecent_RespectsLimit()
    {
        for (int i = 0; i < 5; i++) _store.Submit($"s{i}", 3, null);
        _store.GetRecent(2).Should().HaveCount(2);
    }
}
