// Services/EscalationStates.cs
// State Pattern — EscalationRequest yaşam döngüsünü yönetir.
// Her durum ayrı bir sınıf olarak modellenir; geçersiz geçişler
// switch-case yerine polimorfizm ile engellenir.
//
// Durum grafiği:
//   Open ──→ Acknowledged ──→ Resolved
//        └──→ Resolved        └──→ Dismissed
//        └──→ Dismissed

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Api.Services;

/// <summary>
/// Eskalasyon durumunun davranış arayüzü.
/// Her durum (Open, Acknowledged, Resolved, Dismissed) bu arayüzü implemente eder.
/// </summary>
public interface IEscalationState
{
    /// <summary>Bu duruma karşılık gelen enum değeri.</summary>
    EscalationStatus Status { get; }

    /// <summary>Temsilci eskalasyonu aldı — henüz çözmedi.</summary>
    bool Acknowledge(EscalationRequest req, string? assignedTo);

    /// <summary>Eskalasyon çözüldü.</summary>
    bool Resolve(EscalationRequest req, string? assignedTo, string? resolution);

    /// <summary>Eskalasyon geçersiz bulundu (yanlış eskalasyon).</summary>
    bool Dismiss(EscalationRequest req, string? assignedTo, string? resolution);
}

// ════════════════════════════════════════════════════════════════
// DURUMLAR
// ════════════════════════════════════════════════════════════════

/// <summary>
/// Açık durum — eskalasyon yeni oluşturulmuş.
/// Acknowledge, Resolve ve Dismiss geçişleri mümkün.
/// </summary>
public sealed class OpenEscalationState : IEscalationState
{
    public static readonly OpenEscalationState Instance = new();
    public EscalationStatus Status => EscalationStatus.Open;

    public bool Acknowledge(EscalationRequest req, string? assignedTo)
    {
        req.Status = EscalationStatus.Acknowledged;
        req.AcknowledgedAt = DateTime.UtcNow;
        req.AssignedTo = assignedTo ?? req.AssignedTo;
        return true;
    }

    public bool Resolve(EscalationRequest req, string? assignedTo, string? resolution)
    {
        req.Status = EscalationStatus.Resolved;
        req.ResolvedAt = DateTime.UtcNow;
        req.AssignedTo = assignedTo ?? req.AssignedTo;
        req.Resolution = resolution;
        return true;
    }

    public bool Dismiss(EscalationRequest req, string? assignedTo, string? resolution)
    {
        req.Status = EscalationStatus.Dismissed;
        req.ResolvedAt = DateTime.UtcNow;
        req.AssignedTo = assignedTo ?? req.AssignedTo;
        req.Resolution = resolution ?? WellKnown.EscalationActions.Dismiss;
        return true;
    }
}

/// <summary>
/// Kabul edilmiş durum — temsilci aldı ama henüz çözmedi.
/// Resolve ve Dismiss geçişleri mümkün. Yeniden Acknowledge geçersiz.
/// </summary>
public sealed class AcknowledgedEscalationState : IEscalationState
{
    public static readonly AcknowledgedEscalationState Instance = new();
    public EscalationStatus Status => EscalationStatus.Acknowledged;

    public bool Acknowledge(EscalationRequest req, string? assignedTo) => false;

    public bool Resolve(EscalationRequest req, string? assignedTo, string? resolution)
    {
        req.Status = EscalationStatus.Resolved;
        req.ResolvedAt = DateTime.UtcNow;
        req.AssignedTo = assignedTo ?? req.AssignedTo;
        req.Resolution = resolution;
        return true;
    }

    public bool Dismiss(EscalationRequest req, string? assignedTo, string? resolution)
    {
        req.Status = EscalationStatus.Dismissed;
        req.ResolvedAt = DateTime.UtcNow;
        req.AssignedTo = assignedTo ?? req.AssignedTo;
        req.Resolution = resolution ?? WellKnown.EscalationActions.Dismiss;
        return true;
    }
}

/// <summary>
/// Çözülmüş durum — terminal. Hiçbir geçiş kabul edilmez.
/// </summary>
public sealed class ResolvedEscalationState : IEscalationState
{
    public static readonly ResolvedEscalationState Instance = new();
    public EscalationStatus Status => EscalationStatus.Resolved;

    public bool Acknowledge(EscalationRequest req, string? assignedTo) => false;
    public bool Resolve(EscalationRequest req, string? assignedTo, string? resolution) => false;
    public bool Dismiss(EscalationRequest req, string? assignedTo, string? resolution) => false;
}

/// <summary>
/// Reddedilmiş durum — terminal. Hiçbir geçiş kabul edilmez.
/// </summary>
public sealed class DismissedEscalationState : IEscalationState
{
    public static readonly DismissedEscalationState Instance = new();
    public EscalationStatus Status => EscalationStatus.Dismissed;

    public bool Acknowledge(EscalationRequest req, string? assignedTo) => false;
    public bool Resolve(EscalationRequest req, string? assignedTo, string? resolution) => false;
    public bool Dismiss(EscalationRequest req, string? assignedTo, string? resolution) => false;
}

// ════════════════════════════════════════════════════════════════
// FACTORY
// ════════════════════════════════════════════════════════════════

/// <summary>
/// <see cref="EscalationStatus"/> enum değerinden ilgili
/// <see cref="IEscalationState"/> singleton'ını döndüren factory.
/// </summary>
public static class EscalationStateFactory
{
    /// <summary>Enum → State mapping. Singleton instance'lar kullanılır.</summary>
    public static IEscalationState Create(EscalationStatus status) => status switch
    {
        EscalationStatus.Open => OpenEscalationState.Instance,
        EscalationStatus.Acknowledged => AcknowledgedEscalationState.Instance,
        EscalationStatus.Resolved => ResolvedEscalationState.Instance,
        EscalationStatus.Dismissed => DismissedEscalationState.Instance,
        _ => OpenEscalationState.Instance
    };
}

