using CustomerSupportBot.Application.Services.Improvement;
using CustomerSupportBot.Domain.Model.Improvement;

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>
/// Lesson madenciliği ve onay/ret akışı için primary (driving) port.
/// </summary>
public interface IImprovementsPort
{
    Task<MiningRunReport> MineAsync(CancellationToken ct = default);
    IReadOnlyList<Lesson> GetLessons(LessonStatus? status = null);
    Lesson? GetLesson(string id);
    Task<bool> ApproveAsync(string id, string decidedBy, string? reason, CancellationToken ct = default);
    Task<bool> RejectAsync(string id, string decidedBy, string? reason);
}
