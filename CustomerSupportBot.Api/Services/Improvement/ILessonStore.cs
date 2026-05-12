// Services/Improvement/ILessonStore.cs
// Lesson'ları saklayan store soyutlaması. Default: in-memory.
// Production'da PostgresLessonStore eklenebilir (migration gerekir).

using CustomerSupportBot.Api.Models.Improvement;

namespace CustomerSupportBot.Api.Services.Improvement;

public interface ILessonStore
{
    void Add(Lesson lesson);
    Lesson? Get(string id);
    IReadOnlyList<Lesson> GetByStatus(LessonStatus status);
    IReadOnlyList<Lesson> GetAll(int limit = 200);
    void Update(Lesson lesson);
}
