// Adapters.Agents/IWorkflowRunner.cs
// Tek bir alt görevi/sorguyu çalıştıran koşucunun sözleşmesi.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;
using AgentSession = CustomerSupportBot.Domain.Model.AgentSession;

namespace CustomerSupportBot.Adapters.Agents;

/// <summary>
/// <see cref="WorkflowRunner"/>'ın <see cref="DecomposedRunner"/> tarafından kullanılan yüzeyi.
///
/// <para>
/// <b>Neden var:</b> <c>DecomposedRunner</c> compound sorgu orkestrasyonunun tamamını taşıyor —
/// alt görevleri gruplara ayırma, paralel/sıralı çalıştırma, sonuçları <c>sub.Order</c>
/// sırasında toplama ve tamamlandıkça ilerlemeli olarak yayınlama. Bunların hepsi test edilmesi
/// gereken gerçek mantık, ama <c>WorkflowRunner</c> <c>sealed</c> bir sınıf olduğu ve kendisi de
/// altı ajanı + MAF workflow'unu kurmayı gerektirdiği için (yani Docker/Testcontainers'a bağımlı
/// bir fixture) <c>DecomposedRunner</c> pratikte hiç izole test edilemiyordu. Bu arayüz, tek
/// alt görev koşusunu taklit edilebilir kılarak o mantığı Docker'sız test edilebilir hâle getirir.
/// </para>
///
/// <para>
/// Kasıtlı olarak dar: yalnızca <c>DecomposedRunner</c>'ın ihtiyaç duyduğu iki metot var.
/// <c>GetWorkflowDiagram</c> buraya alınmadı — onu yalnızca <c>CustomerSupportTeam</c> çağırır
/// ve somut tipi zaten elinde tutar.
/// </para>
/// </summary>
internal interface IWorkflowRunner
{
    Task<string> RunAsync(
        string query,
        List<ConversationMessage>? conversationHistory,
        AgentSession? session,
        ReasoningResult? reasoning,
        CancellationToken ct);

    IAsyncEnumerable<StreamEvent> RunStreamingAsync(
        string query,
        List<ConversationMessage>? conversationHistory,
        AgentSession? session,
        ReasoningResult? reasoning,
        CancellationToken ct);
}
