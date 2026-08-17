// Application/Services/ImprovementsPortService.cs
// DRIVING PORT IMPL — IImprovementsPort → LessonMiner + ILessonStore.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Services.Improvement;
using CustomerSupportBot.Domain.Model.Improvement;

namespace CustomerSupportBot.Application.Services.Improvement;

public sealed class ImprovementsPortService : IImprovementsPort
{
    private readonly LessonMiner _miner;
    private readonly ILessonStore _lessons;

    public ImprovementsPortService(LessonMiner miner, ILessonStore lessons)
    {
        _miner = miner;
        _lessons = lessons;
    }

    public Task<MiningRunReport> MineAsync(CancellationToken ct = default)
        => _miner.MineAsync(ct);

    public IReadOnlyList<Lesson> GetLessons(LessonStatus? status = null)
        => status.HasValue ? _lessons.GetByStatus(status.Value) : _lessons.GetAll();

    public Lesson? GetLesson(string id)
        => _lessons.Get(id);

    public Task<bool> ApproveAsync(string id, string decidedBy, string? reason, CancellationToken ct = default)
        => _miner.ApproveAsync(id, decidedBy, reason, ct);

    public bool Reject(string id, string decidedBy, string? reason)
        => _miner.Reject(id, decidedBy, reason);
}
