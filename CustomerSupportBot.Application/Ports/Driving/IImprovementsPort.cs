// Ports/Driving/IImprovementsPort.cs
// PRIMARY PORT — Self-improvement döngüsü: lesson madenciliği ve onay akışı.

using CustomerSupportBot.Application.Services.Improvement;
using CustomerSupportBot.Domain.Model.Improvement;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Lesson madenciliği ve onay/ret akışı için primary (driving) port.
/// </summary>
public interface IImprovementsPort
{
    Task<MiningRunReport> MineAsync(CancellationToken ct = default);
    IReadOnlyList<Lesson> GetLessons(LessonStatus? status = null);
    Lesson? GetLesson(string id);
    Task<bool> ApproveAsync(string id, string decidedBy, string? reason, CancellationToken ct = default);
    bool Reject(string id, string decidedBy, string? reason);
}
