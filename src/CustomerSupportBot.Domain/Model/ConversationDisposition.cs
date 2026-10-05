// Domain/Model/ConversationDisposition.cs
// Temsilcinin canlı sohbeti kapatırken kaydettiği kapanış nedeni, etiketler ve not ("wrap-up").

namespace CustomerSupportBot.Domain.Model;

public sealed class ConversationDisposition
{
    public const int MaxTags = 10;
    public const int MaxTagLength = 30;
    public const int MaxNoteLength = 1000;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string SessionId { get; set; } = "";

    /// <summary>Yapılandırılmış nedenlerden birinin kodu (ör. <c>resolved</c>).</summary>
    public string ReasonCode { get; set; } = "";

    /// <summary>Normalleştirilmiş etiketler (küçük harf, boşluk yerine <c>-</c>).</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Temsilci notu — serbest metin, kişisel veri içerebilir (KVKK silmesine dahil).</summary>
    public string? Note { get; set; }

    public string? ClosedBy { get; set; }
    public DateTime ClosedAt { get; set; }
}

/// <summary>Analitik: bir kapanış nedeninin kaç kez seçildiği.</summary>
public sealed record ClosingReasonCount(string Code, string Label, int Count);

/// <summary>Analitik: bir etiketin kaç kapanışta kullanıldığı.</summary>
public sealed record TagUsage(string Tag, int Count);
