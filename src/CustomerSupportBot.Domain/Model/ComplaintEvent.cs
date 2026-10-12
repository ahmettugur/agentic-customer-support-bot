namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Şikayet zaman çizelgesinin bir satırı. <see cref="Note"/> İÇ nottur: müşteriye, bota ve KVKK
/// dışa aktarımına gitmez. <see cref="Actor"/> personel kullanıcı adı ya da <c>system</c>.
/// </summary>
public sealed record ComplaintEvent(
    DateTime At,
    string Actor,
    string Kind,
    string? FromStatus,
    string? ToStatus,
    string? AssignedAgentId,
    string? Note);

public static class ComplaintEventKinds
{
    public const string Created = "created";
    public const string Claimed = "claimed";
    public const string Assigned = "assigned";
    public const string Resolved = "resolved";
    public const string Rejected = "rejected";
    public const string Reopened = "reopened";
    public const string Note = "note";
}
