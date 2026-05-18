// PortAliases.cs
// Hexagonal Architecture geçiş katmanı — global using alias'ları.
//
// Bu dosya, eski Api interface isimlerini Application port isimlerine yönlendirir.
// Böylece mevcut kodun refaktör edilmesine gerek kalmadan Application port'ları
// otomatik olarak kullanılmış olur.

global using IApprovalQueue           = CustomerSupportBot.Application.Ports.Driven.Persistence.IApprovalQueueRepository;
global using IEscalationSink          = CustomerSupportBot.Application.Ports.Driven.Persistence.IEscalationRepository;
global using IChatBridge              = CustomerSupportBot.Application.Ports.Driven.Persistence.IChatBridgeRepository;
global using IChatModeRegistry        = CustomerSupportBot.Application.Ports.Driven.Persistence.IChatModeRepository;
global using IReasoningTraceStore     = CustomerSupportBot.Application.Ports.Driven.Observability.IReasoningTraceRepository;
global using IRatingStore             = CustomerSupportBot.Application.Ports.Driven.Persistence.IRatingRepository;
global using ILessonStore             = CustomerSupportBot.Application.Ports.Driven.Persistence.ILessonRepository;
global using ICustomerProfileStore    = CustomerSupportBot.Application.Ports.Driven.Persistence.ICustomerProfileRepository;
global using IHumanAgentRegistry      = CustomerSupportBot.Application.Ports.Driven.Persistence.IHumanAgentRepository;
global using IWorkflowDefinitionStore = CustomerSupportBot.Application.Ports.Driven.Persistence.IWorkflowDefinitionRepository;
global using ISlaEventSink            = CustomerSupportBot.Application.Ports.Driven.Persistence.ISlaEventRepository;
global using IVectorMemoryStore       = CustomerSupportBot.Application.Ports.Driven.AI.IVectorMemoryPort;
global using IEmbeddingService        = CustomerSupportBot.Application.Ports.Driven.AI.IEmbeddingPort;
global using IAppDistributedLock      = CustomerSupportBot.Application.Ports.Driven.Locking.IDistributedLockPort;
global using ISessionManager          = CustomerSupportBot.Application.Ports.Driven.Persistence.ISessionRepository;
global using IConversationStore       = CustomerSupportBot.Application.Ports.Driven.Persistence.ISessionRepository;
global using SessionInfo              = CustomerSupportBot.Application.Ports.Driven.Persistence.SessionInfo;

// Application Services re-export
global using CustomerSupportBot.Application.Services;

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Application.Ports.Driven.Persistence;

namespace CustomerSupportBot.Api;

/// <summary>
/// Geriye uyumluluk için eski method isimlerini sağlayan extension method'lar.
/// ISessionRepository'nin yeni method isimleri:
///   GetOrCreate, Get, Update, GetAll
/// Eski isimler:
///   GetOrCreateSession, GetSession, UpdateSession
/// </summary>
public static class SessionRepositoryExtensions
{
    public static AgentSession GetOrCreateSession(this ISessionRepository repo, string? sessionId)
        => repo.GetOrCreate(sessionId);

    public static AgentSession? GetSession(this ISessionRepository repo, string sessionId)
        => repo.Get(sessionId);

    public static void UpdateSession(this ISessionRepository repo, AgentSession session)
        => repo.Update(session);
}
