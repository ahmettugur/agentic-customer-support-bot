// Services/Improvement/ILessonStore.cs
// Lesson'ları saklayan store soyutlaması. Default: in-memory.
// Production'da PostgresLessonStore eklenebilir (migration gerekir).

using CustomerSupportBot.Models.Improvement;

namespace CustomerSupportBot.Services.Improvement;

public interface ILessonStore
{
    void Add(Lesson lesson);
    Lesson? Get(string id);
    IReadOnlyList<Lesson> GetByStatus(LessonStatus status);
    IReadOnlyList<Lesson> GetAll(int limit = 200);
    void Update(Lesson lesson);
}
