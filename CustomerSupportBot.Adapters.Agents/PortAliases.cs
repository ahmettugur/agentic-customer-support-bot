// PortAliases.cs — Adapters.Agents için Application port alias'ları

global using IApprovalQueue        = CustomerSupportBot.Application.Ports.Driven.Persistence.IApprovalQueueRepository;
global using IEscalationSink       = CustomerSupportBot.Application.Ports.Driven.Persistence.IEscalationRepository;
global using IReasoningTraceStore  = CustomerSupportBot.Application.Ports.Driven.Observability.IReasoningTraceRepository;
global using ICustomerProfileStore = CustomerSupportBot.Application.Ports.Driven.Persistence.ICustomerProfileRepository;
global using IHumanAgentRegistry   = CustomerSupportBot.Application.Ports.Driven.Persistence.IHumanAgentRepository;
global using ISessionManager       = CustomerSupportBot.Application.Ports.Driven.Persistence.ISessionRepository;
global using ISkillsBasedRouter    = CustomerSupportBot.Application.Ports.Driven.ISkillsBasedRouter;
global using IContextProvider      = CustomerSupportBot.Application.Ports.Driven.IContextProvider;
