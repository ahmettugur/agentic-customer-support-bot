// Ports/Outbound/Observability/ILlmCallAttribution.cs
// LLM çağrısının hangi görüşme için yapıldığı — görüşme başına maliyet için.

namespace CustomerSupportBot.Application.Ports.Outbound.Observability;

/// <summary>
/// Bir görüşmenin işini yapan kod (sohbet turu, fotoğraf analizi, temsilci asistanı) kapsamı açar; bu
/// akıştaki LLM çağrıları maliyet kaydına o görüşmenin kimliğiyle yazılır. Kapsam AsyncLocal'dır: paralel
/// görüşmeler birbirine karışmaz. Kapsam dışındaki çağrılar (arka plan işleri) oturumsuz kalır.
/// </summary>
public interface ILlmCallAttribution
{
    string? CurrentSessionId { get; }

    /// <summary>Kapsam açar; dispose edilince önceki kapsama döner. <c>null</c>/boş kimlik kapsamı değiştirmez.</summary>
    IDisposable BeginSession(string? sessionId);
}
