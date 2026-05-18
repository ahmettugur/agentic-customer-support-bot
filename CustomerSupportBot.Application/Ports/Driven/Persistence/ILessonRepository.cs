// Ports/Driven/Persistence/ILessonRepository.cs
// SECONDARY PORT — Self-improvement ders kalıcılığı.

using CustomerSupportBot.Domain.Model.Improvement;

namespace CustomerSupportBot.Application.Ports.Driven.Persistence;

/// <summary>
/// LessonMiner tarafından üretilen derslerin kalıcılığı için secondary port.
/// Adaptörler: PostgresLessonStore, InMemoryLessonStore.
/// </summary>
public interface ILessonRepository
{
    void Add(Lesson lesson);
    Lesson? Get(string id);
    IReadOnlyList<Lesson> GetByStatus(LessonStatus status);
    IReadOnlyList<Lesson> GetAll(int limit = 200);
    void Update(Lesson lesson);
}
