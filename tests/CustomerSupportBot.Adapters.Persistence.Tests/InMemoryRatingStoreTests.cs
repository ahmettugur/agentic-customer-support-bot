// Tests/Services/InMemoryRatingStoreTests.cs

using Microsoft.Extensions.Logging.Abstractions;
using CustomerSupportBot.Adapters.Persistence.InMemory;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public class InMemoryRatingStoreTests
{
    private readonly InMemoryRatingStore _store = new(NullLogger<InMemoryRatingStore>.Instance);

    [Fact]
    public async Task Submit_StarsClampedHigh()
    {
        var r = await _store.SubmitAsync("s1", 10, "great");
        r.Stars.Should().Be(5);
    }

    [Fact]
    public async Task Submit_StarsClampedLow()
    {
        var r = await _store.SubmitAsync("s1", 0, null);
        r.Stars.Should().Be(1);
    }

    [Fact]
    public async Task Submit_PersistsAndOverwrites()
    {
        await _store.SubmitAsync("s1", 3, "ok");
        await _store.SubmitAsync("s1", 5, "great");
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
    public async Task GetAll_ReturnsAllRatings()
    {
        await _store.SubmitAsync("s1", 4, null);
        await _store.SubmitAsync("s2", 3, null);
        _store.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public async Task GetRecent_RespectsLimit()
    {
        for (int i = 0; i < 5; i++) await _store.SubmitAsync($"s{i}", 3, null);
        _store.GetRecent(2).Should().HaveCount(2);
    }
}
