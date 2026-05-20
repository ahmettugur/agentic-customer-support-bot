// PortAliases.cs — Test projesi
// Driven port alias'ları kaldırıldı; namespace import'ları kalmıştır.

global using CustomerSupportBot.Application.Ports.Driven.Persistence;
global using CustomerSupportBot.Application.Ports.Driven.Observability;
global using CustomerSupportBot.Application.Ports.Driven.Locking;

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
global using InMemoryWorkflowDefinitionStore = CustomerSupportBot.Adapters.Persistence.InMemory.InMemoryWorkflowDefinitionStore;
global using InMemorySlaEventSink            = CustomerSupportBot.Adapters.Persistence.InMemory.InMemorySlaEventSink;

// Auth ports
global using IPasswordHasher = CustomerSupportBot.Application.Ports.Driven.Auth.IPasswordHasher;

// Approval and context ports
global using CustomerSupportBot.Application.Ports.Driven;
global using CustomerSupportBot.Application.Ports.Driving;

// Application Services re-export
global using CustomerSupportBot.Application.Services;
