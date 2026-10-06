namespace CustomerSupportBot.Web.Services;

/// <summary>
/// Personel kenar çubuğundaki rozet sayıları (bekleyen onay, açık eskalasyon, canlı sohbet, ders önerisi).
/// Sayıları Admin sayfası zaten periyodik çekiyor; kenar çubuğu ayrıca yoklama YAPMAZ — her sayfada ek
/// istek, "general" hız sınırı kotasını boşa tüketirdi. Başka bir sayfadayken son bilinen değerler
/// gösterilir; hiç yüklenmemişse (ör. doğrudan /traces açıldı) rozet gizli kalır.
/// </summary>
public sealed class StaffBadgeState
{
    public int? PendingApprovals { get; private set; }
    public int? OpenEscalations  { get; private set; }
    public int? ActiveChats      { get; private set; }
    public int? ProposedLessons  { get; private set; }

    public event Action? OnChange;

    public void SetQueues(int pendingApprovals, int openEscalations, int activeChats)
    {
        if (PendingApprovals == pendingApprovals && OpenEscalations == openEscalations && ActiveChats == activeChats)
            return;
        PendingApprovals = pendingApprovals;
        OpenEscalations  = openEscalations;
        ActiveChats      = activeChats;
        OnChange?.Invoke();
    }

    public void SetProposedLessons(int count)
    {
        if (ProposedLessons == count) return;
        ProposedLessons = count;
        OnChange?.Invoke();
    }
}
