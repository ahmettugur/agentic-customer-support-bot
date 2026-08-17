namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>Bir provider'ın bu turdaki sonucu — ne oldu, ne kadar yer kapladı.</summary>
public sealed record ContextPart(
    string ProviderName,
    int Order,
    ContextPartStatus Status,
    int Length);

public enum ContextPartStatus
{
    /// <summary>Bağlam üretildi ve prompt'a kondu.</summary>
    Included,
    /// <summary>Provider çalıştı ama söyleyecek bir şeyi yoktu (normal durum).</summary>
    Empty,
    /// <summary>Hata verdi.</summary>
    Failed,
    /// <summary>Süresi doldu.</summary>
    TimedOut,
    /// <summary>Bütçe dolduğu için dışarıda bırakıldı.</summary>
    Dropped
}

/// <summary>
/// <see cref="IContextPipeline.BuildContextAsync"/> sonucu.
///
/// <para>
/// Neden düz <c>string</c> değil: çağıranın <b>hangi provider'ın katkı yaptığını</b> bilmesi
/// gerekiyor. Somut sebep, <c>WorkflowMessageBuilder</c>'ın konuşma geçmişini kırpma kararı —
/// özetlenen turlar yalnızca özet BU TURDA gerçekten prompt'a girdiyse atlanabilir. Karar
/// oturum durumuna bakarak verilseydi, özetleyici hata verdiği turda özet de geçmiş de
/// prompt'ta olmaz ve o turlar tamamen kaybolurdu.
/// </para>
///
/// <para>
/// İkinci fayda gözlemlenebilirlik: parçalar trace'e yazılınca "model neyi biliyordu?"
/// sorusu yanıtlanabilir hale gelir.
/// </para>
/// </summary>
public sealed record ContextResult(string Text, IReadOnlyList<ContextPart> Parts)
{
    public static readonly ContextResult Empty = new("", []);

    /// <summary>Adı verilen provider bu turda prompt'a gerçekten katkı yaptı mı?</summary>
    public bool Included(string providerName) =>
        Parts.Any(p => p.ProviderName == providerName && p.Status == ContextPartStatus.Included);
}
