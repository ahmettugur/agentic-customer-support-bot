// Services/InMemoryRatingStore.cs
// Bellek içi konuşma değerlendirme deposu.
// Thread-safe erişim için ConcurrentDictionary kullanılır.

using System.Collections.Concurrent;
using CustomerSupportBot.Models;

namespace CustomerSupportBot.Services;

public class InMemoryRatingStore : IRatingStore
{
    private readonly ConcurrentDictionary<string, ConversationRating> _ratings = new();
    private readonly ILogger<InMemoryRatingStore> _logger;

    public InMemoryRatingStore(ILogger<InMemoryRatingStore> logger)
    {
        _logger = logger;
    }

    public ConversationRating Submit(string sessionId, int stars, string? feedback)
    {
        var rating = new ConversationRating
        {
            SessionId = sessionId,
            Stars = Math.Clamp(stars, 1, 5),
            Feedback = feedback?.Trim(),
            RatedAt = DateTime.UtcNow
        };

        _ratings.AddOrUpdate(sessionId, rating, (_, _) => rating);

        _logger.LogInformation(
            "[Rating] Session {SessionId} rated {Stars} stars. Feedback: {Feedback}",
            sessionId, rating.Stars, rating.Feedback ?? "(yok)");

        return rating;
    }

    public ConversationRating? GetBySession(string sessionId) =>
        _ratings.TryGetValue(sessionId, out var r) ? r : null;

    public IReadOnlyList<ConversationRating> GetAll() =>
        _ratings.Values
            .OrderByDescending(r => r.RatedAt)
            .ToList();

    public IReadOnlyList<ConversationRating> GetRecent(int count = 20) =>
        _ratings.Values
            .OrderByDescending(r => r.RatedAt)
            .Take(count)
            .ToList();
}
