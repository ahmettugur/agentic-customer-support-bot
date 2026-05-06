// Services/Improvement/InMemoryLessonStore.cs

using System.Collections.Concurrent;
using CustomerSupportBot.Models.Improvement;

namespace CustomerSupportBot.Services.Improvement;

public sealed class InMemoryLessonStore : ILessonStore
{
    private readonly ConcurrentDictionary<string, Lesson> _byId = new();

    public void Add(Lesson lesson) => _byId[lesson.Id] = lesson;
    public Lesson? Get(string id) => _byId.TryGetValue(id, out var l) ? l : null;
    public void Update(Lesson lesson) => _byId[lesson.Id] = lesson;

    public IReadOnlyList<Lesson> GetByStatus(LessonStatus status) =>
        _byId.Values.Where(l => l.Status == status)
            .OrderByDescending(l => l.CreatedAt).ToList();

    public IReadOnlyList<Lesson> GetAll(int limit = 200) =>
        _byId.Values.OrderByDescending(l => l.CreatedAt).Take(limit).ToList();
}
