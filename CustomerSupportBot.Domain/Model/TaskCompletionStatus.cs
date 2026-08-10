// Models/TaskCompletionStatus.cs
// PostToolReflection.Status ve SpecialistReasoningParser.NormalizeStatus
// İçin type-safe enum. String karşılaştırma yerine bu kullanılır.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Specialist agent'ın tool sonrası görev tamamlanma durumu.
/// Eski string tabanlı "done"/"needs_followup"/"needs_escalation"/"failed"/"partial"
/// Yerine type-safe alternatif.
/// </summary>
public enum TaskCompletionStatus
{
    /// <summary>Görev başarıyla tamamlandı.</summary>
    Done,

    /// <summary>Ek bilgi veya takip gerekiyor.</summary>
    NeedsFollowUp,

    /// <summary>İnsan temsilciye devredilmeli.</summary>
    NeedsEscalation,

    /// <summary>Tool çağrısı başarısız oldu.</summary>
    Failed,

    /// <summary>Kısmen tamamlandı, eksik var.</summary>
    Partial,

    /// <summary>
    /// Yan etkili tool onay kuyruğuna eklendi ama admin kararı verilmedi — iş HENÜZ
    /// yapılmadı. Bloklamayan HITL modelinde <see cref="ToolResult.Success"/>=true olsa
    /// bile bu durumda <see cref="Done"/> ile karıştırılmamalı (bkz. ToolResult.PendingApproval).
    /// </summary>
    PendingApproval
}

