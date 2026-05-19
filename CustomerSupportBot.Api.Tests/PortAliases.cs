// PortAliases.cs — Test projesi için Application port alias'ları
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

// InMemory adapter alias'ları
global using InMemoryApprovalQueue       = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryApprovalQueue;
global using InMemoryEscalationSink      = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryEscalationSink;
global using InMemoryReasoningTraceStore = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryReasoningTraceStore;
global using InMemoryRatingStore         = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryRatingStore;
global using InMemoryChatModeRegistry    = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryChatModeRegistry;
global using InMemoryChatBridge          = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryChatBridge;
global using InMemoryProductCatalogAdapter = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryProductCatalogAdapter;
global using InMemoryOrderAdapter        = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryOrderAdapter;
global using InMemoryComplaintAdapter    = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryComplaintAdapter;
global using InMemoryCustomerProfileStore = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryCustomerProfileStore;
global using InMemorySessionManager      = CustomerSupportBot.Adapters.Persistence.InMemory.InMemorySessionManager;
global using InMemoryHumanAgentRegistry  = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryHumanAgentRegistry;
global using InMemoryLessonStore         = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryLessonStore;
global using InMemoryWorkflowDefinitionStore = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryWorkflowDefinitionStore;

// Application Services re-export
global using CustomerSupportBot.Application.Services;
