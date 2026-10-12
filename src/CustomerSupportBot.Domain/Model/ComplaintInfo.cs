namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Şikayet bilgisini temsil eder.
/// </summary>
public class ComplaintInfo
{
    public string OrderId { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string Complaint { get; set; } = "";
    public string Status { get; set; } = "";

    /// <summary>Oluşturulma (UTC). SLA buradan sayılır. Migration öncesi kayıtlarda migration anı.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Son durum/atama değişikliği (UTC).</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Üstlenen temsilci (<c>HumanAgent.Id</c>); atanmamışsa null.</summary>
    public string? AssignedAgentId { get; set; }

    /// <summary>Çözüldü/Reddedildi için müşteriye görünen açıklama. Yeniden açılınca korunur.</summary>
    public string? Resolution { get; set; }

    /// <summary>Şikayeti doğuran onay kaydı; fotoğraflar bu onaya bağlıdır.</summary>
    public string? ApprovalId { get; set; }

    /// <summary>Her geçişte +1. Koşullu güncelleme ve e-posta defteri anahtarı için.</summary>
    public int Version { get; set; }
}
