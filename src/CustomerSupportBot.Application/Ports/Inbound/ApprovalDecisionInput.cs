// Ports/Driving/ApprovalDecisionInput.cs
// IApprovalPort driving port'unun use case input DTO'su.

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>
/// Admin endpoint'inin request body'si — approve/reject kararını taşır.
/// </summary>
public class ApprovalDecisionInput
{
    /// <summary>true → approved, false → rejected.</summary>
    public bool Approved { get; set; }

    /// <summary>Kararı veren kişi (opsiyonel, "admin" default).</summary>
    public string? DecidedBy { get; set; }

    /// <summary>Red gerekçesi (reddedilirse kullanıcıya döner).</summary>
    public string? Reason { get; set; }
}
