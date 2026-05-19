// PortAliases.cs
// Global using alias'ları — uzun Application port namespace'lerini kısaltır.
// Bu alias'lar, kod okunabilirliği için kısa isimler sağlar; geriye dönük uyumluluk için değil.

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

// Approval context port
global using CustomerSupportBot.Application.Ports.Driven;

// Application Services re-export
global using CustomerSupportBot.Application.Services;

// Admin panel HTTP DTO'ları
global using CustomerSupportBot.Api.Models;
