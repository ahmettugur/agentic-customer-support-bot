using CustomerSupportBot.Domain.Model.Improvement;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>
/// LessonMiner tarafından üretilen derslerin kalıcılığı için secondary port.
///</summary>
public interface ILessonStore
{
    /// <summary>Yazma bilinçli olarak CancellationToken almaz: istemci bağlantıyı kesse bile cache ile DB tutarlı kalmalı.</summary>
    Task AddAsync(Lesson lesson);
    Lesson? Get(string id);
    IReadOnlyList<Lesson> GetByStatus(LessonStatus status);
    IReadOnlyList<Lesson> GetAll(int limit = 200);
    /// <summary>Yazma bilinçli olarak CancellationToken almaz: istemci bağlantıyı kesse bile cache ile DB tutarlı kalmalı.</summary>
    Task UpdateAsync(Lesson lesson);
}
