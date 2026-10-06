// Temsilci ile müşteri arasındaki sesli görüşme. Ses tarayıcılar arasında akar (WebRTC); bu kayıt
// görüşmenin durumunu, rızasını ve zamanlarını tutar.

namespace CustomerSupportBot.Domain.Model.Voice;

public enum VoiceCallStatus { Ringing, Active, Ended, Declined, Missed, Cancelled, Failed }

public static class VoiceCallEndReasons
{
    public const string AgentHangup    = "agent_hangup";
    public const string CustomerHangup = "customer_hangup";
    public const string ConnectionLost = "connection_lost";
    public const string ConnectFailed  = "connect_failed";
    public const string Declined       = "declined";
    public const string NoMicrophone   = "no_microphone";
    public const string Missed         = "missed";
}

public sealed class VoiceCallStateException(string message) : InvalidOperationException(message);

/// <summary>
/// Durumlar: <c>Ringing → Active → Ended|Failed</c>; <c>Ringing → Declined|Missed|Cancelled</c>.
/// <see cref="ConsentAt"/> (müşterinin kayıt rızası) olmadan <c>Active</c> olunamaz — kabul rızadır.
/// Bitmiş görüşmede yeniden kapatma çağrısı yok sayılır (iki taraf aynı anda kapatabilir).
/// </summary>
public sealed class VoiceCall
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string AgentDisplayName { get; set; } = "";
    public VoiceCallStatus Status { get; set; } = VoiceCallStatus.Ringing;
    public DateTime CreatedAt { get; set; }
    public DateTime? AnsweredAt { get; set; }
    public DateTime? ConsentAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? EndReason { get; set; }

    /// <summary>Son kayıt parçasının yüklendiği an — sekmesi kapanan temsilciyi yakalamak için.</summary>
    public DateTime? LastChunkAt { get; set; }

    public bool IsOpen => Status is VoiceCallStatus.Ringing or VoiceCallStatus.Active;

    public TimeSpan? Duration => AnsweredAt is { } a && EndedAt is { } e ? e - a : null;

    public static VoiceCall Start(string sessionId, string agentId, string agentDisplayName, DateTime now) => new()
    {
        SessionId = sessionId,
        AgentId = agentId,
        AgentDisplayName = agentDisplayName,
        Status = VoiceCallStatus.Ringing,
        CreatedAt = now
    };

    public void Accept(DateTime now)
    {
        Require(VoiceCallStatus.Ringing, "kabul");
        ConsentAt = now;
        AnsweredAt = now;
        Status = VoiceCallStatus.Active;
    }

    public void Decline(DateTime now, string reason)
    {
        Require(VoiceCallStatus.Ringing, "ret");
        Close(now, VoiceCallStatus.Declined, reason);
    }

    public void Miss(DateTime now)
    {
        Require(VoiceCallStatus.Ringing, "cevapsız");
        Close(now, VoiceCallStatus.Missed, VoiceCallEndReasons.Missed);
    }

    /// <summary>Her iki taraf da kapatabilir. Çalarken kapatmak iptaldir; bağlantı sorunu başarısızlıktır.</summary>
    public void Hangup(DateTime now, string reason)
    {
        if (!IsOpen) return;
        var status = Status == VoiceCallStatus.Ringing
            ? VoiceCallStatus.Cancelled
            : reason is VoiceCallEndReasons.ConnectionLost or VoiceCallEndReasons.ConnectFailed
                ? VoiceCallStatus.Failed
                : VoiceCallStatus.Ended;
        Close(now, status, reason);
    }

    private void Close(DateTime now, VoiceCallStatus status, string reason)
    {
        Status = status;
        EndedAt = now;
        EndReason = reason;
    }

    private void Require(VoiceCallStatus expected, string action)
    {
        if (Status != expected)
            throw new VoiceCallStateException($"Görüşme {Status} durumunda; {action} yapılamaz.");
    }
}
