// Ports/Driving/IChatPort.cs
// PRIMARY PORT — HTTP driving adapter (ChatEndpoints) bu port aracılığıyla Core'u çağırır.
// Core'un "chat" kullanım senaryosunu dışarıya sunan sözleşme.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Chat kullanım senaryosu için primary (driving) port.
/// HTTP adaptörü (Endpoints) bu arayüze bağımlıdır; Core implementasyonuna değil.
/// </summary>
public interface IChatPort
{
    /// <summary>
    /// Non-streaming chat: kullanıcı sorgusunu işler ve tek JSON yanıt döndürür.
    /// </summary>
    Task<ChatResponse> HandleAsync(ChatRequest request, CancellationToken ct = default);

    /// <summary>
    /// SSE streaming chat: reasoning → workflow → yanıt deltalarını stream'ler.
    /// </summary>
    IAsyncEnumerable<StreamEvent> HandleStreamAsync(ChatRequest request, CancellationToken ct = default);
}
