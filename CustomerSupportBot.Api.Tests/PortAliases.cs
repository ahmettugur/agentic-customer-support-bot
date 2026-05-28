// PortAliases.cs — Test projesi
// Driven port alias'ları kaldırıldı; namespace import'ları kalmıştır.

global using CustomerSupportBot.Application.Ports.Outbound.Persistence;
global using CustomerSupportBot.Application.Ports.Outbound.Observability;
global using CustomerSupportBot.Application.Ports.Outbound.Locking;

// InMemory adapter alias'ları
global using InMemoryApprovalQueue       = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryApprovalQueue;
global using InMemoryEscalationSink      = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryEscalationSink;
global using InMemoryReasoningTraceStore = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryReasoningTraceStore;
global using InMemoryRatingStore         = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryRatingStore;
global using InMemoryChatModeRegistry    = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryChatModeRegistry;
global using InMemoryChatBridge          = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryChatBridge;
global using InMemoryCustomerProfileStore = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryCustomerProfileStore;
global using InMemorySessionManager      = CustomerSupportBot.Adapters.Persistence.InMemory.InMemorySessionManager;
global using InMemoryHumanAgentRegistry  = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryHumanAgentRegistry;
global using InMemoryWorkflowDefinitionStore = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryWorkflowDefinitionStore;
global using InMemorySlaEventSink            = CustomerSupportBot.Adapters.Persistence.InMemory.InMemorySlaEventSink;

// Auth ports
global using IPasswordHasher = CustomerSupportBot.Application.Ports.Outbound.Auth.IPasswordHasher;

// Approval and context ports
global using CustomerSupportBot.Application.Ports.Outbound;
global using CustomerSupportBot.Application.Ports.Inbound;

// Application Services re-export
global using CustomerSupportBot.Application.Services;
global using CustomerSupportBot.Application.Services.Approval;
global using CustomerSupportBot.Application.Services.Chat;
global using CustomerSupportBot.Application.Services.Escalation;
global using CustomerSupportBot.Application.Services.Reasoning;
global using CustomerSupportBot.Application.Services.Realtime;
global using CustomerSupportBot.Application.Services.Telemetry;
global using CustomerSupportBot.Application.Services.Providers;
global using CustomerSupportBot.Application.Services.Routing;
