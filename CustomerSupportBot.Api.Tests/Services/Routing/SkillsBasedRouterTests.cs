// Tests/Services/Routing/SkillsBasedRouterTests.cs

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;
using CustomerSupportBot.Application.Services.Routing;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Tests.Services.Routing;

public class SkillsBasedRouterTests
{
    private static (SkillsBasedRouter router, InMemoryHumanAgentRegistry registry) Build(
        Action<RoutingOptions>? configure = null,
        params HumanAgent[] seeds)
    {
        var opts = new RoutingOptions
        {
            SeedAgents = seeds.ToList(),
            IntentSkillMap = new(StringComparer.OrdinalIgnoreCase)
            {
                ["�ikayet"] = new() { "complaint" },
                ["sipari�_sorgulama"] = new() { "order" },
                ["�r�n_bilgisi"] = new() { "product" }
            },
            ProfileKeywordSkillMap = new(StringComparer.OrdinalIgnoreCase)
            {
                ["VIP"] = "vip"
            }
        };
        configure?.Invoke(opts);
        var optsWrapper = Options.Create(opts);
        var registry = new InMemoryHumanAgentRegistry(optsWrapper);
        var router = new SkillsBasedRouter(registry, optsWrapper);
        return (router, registry);
    }

    private static ReasoningTrace TraceWithIntent(string intent) => new()
    {
        TraceId = "t1",
        SessionId = "s1",
        UserQuery = "test",
        Reasoning = new ReasoningResult { Intent = intent }
    };

    [Fact]
    public void Decide_NoActiveAgents_ReturnsEmptyDecisionWithNote()
    {
        var (router, _) = Build();

        var decision = router.Decide(TraceWithIntent("�ikayet"), "ComplaintAgent", null);

        decision.SuggestedAgentId.Should().BeNull();
        decision.Note.Should().Contain("temsilci yok");
    }

    [Fact]
    public void Decide_PicksAgentWithBestSkillMatch()
    {
        var alice = new HumanAgent
        {
            Id = "alice",
            DisplayName = "Alice",
            Skills = new() { "complaint", "refund" },
            Languages = new() { "tr" },
            IsActive = true,
            MaxConcurrentLoad = 5
        };
        var bob = new HumanAgent
        {
            Id = "bob",
            DisplayName = "Bob",
            Skills = new() { "order", "product" },
            Languages = new() { "tr" },
            IsActive = true,
            MaxConcurrentLoad = 5
        };
        var (router, _) = Build(seeds: new[] { alice, bob });

        var decision = router.Decide(TraceWithIntent("�ikayet"), "ComplaintAgent", null);

        decision.SuggestedAgentId.Should().Be("alice");
        decision.MatchedSkills.Should().Contain("complaint");
    }

    [Fact]
    public void Decide_PrefersAgentSpeakingPreferredLanguage()
    {
        var trAgent = new HumanAgent
        {
            Id = "tr-only",
            DisplayName = "TR",
            Skills = new() { "order" },
            Languages = new() { "tr" },
            IsActive = true,
            MaxConcurrentLoad = 5
        };
        var enAgent = new HumanAgent
        {
            Id = "en-only",
            DisplayName = "EN",
            Skills = new() { "order" },
            Languages = new() { "en" },
            IsActive = true,
            MaxConcurrentLoad = 5
        };
        var (router, _) = Build(o => o.LanguageWeight = 0.5,
                                 seeds: new[] { trAgent, enAgent });

        var profile = new CustomerProfile { CustomerId = "c1", PreferredLanguage = "en" };
        var decision = router.Decide(TraceWithIntent("sipari�_sorgulama"), "OrderAgent", profile);

        decision.SuggestedAgentId.Should().Be("en-only");
    }

    [Fact]
    public void Decide_SkipsAgentsAtMaxLoad()
    {
        var busy = new HumanAgent
        {
            Id = "busy",
            DisplayName = "Busy",
            Skills = new() { "complaint" },
            Languages = new() { "tr" },
            IsActive = true,
            MaxConcurrentLoad = 1,
            CurrentLoad = 1
        };
        var free = new HumanAgent
        {
            Id = "free",
            DisplayName = "Free",
            Skills = new() { "order" }, // farkl� skill � yine de se�ilmeli (busy filtre d���)
            Languages = new() { "tr" },
            IsActive = true,
            MaxConcurrentLoad = 5
        };
        var (router, _) = Build(seeds: new[] { busy, free });

        var decision = router.Decide(TraceWithIntent("�ikayet"), "ComplaintAgent", null);

        decision.SuggestedAgentId.Should().Be("free");
    }

    [Fact]
    public void Decide_DisabledRouting_ReturnsEmptyDecision()
    {
        var (router, _) = Build(o => o.Enabled = false,
                                 seeds: new[] { new HumanAgent { DisplayName = "X", IsActive = true } });

        var decision = router.Decide(TraceWithIntent("�ikayet"), "ComplaintAgent", null);

        decision.SuggestedAgentId.Should().BeNull();
        decision.Note.Should().Contain("devre d");
    }

    [Fact]
    public void ExtractRequiredSkills_IntentMappedToTags()
    {
        var (router, _) = Build();
        var trace = TraceWithIntent("�ikayet");
        var profile = new CustomerProfile { CustomerId = "c1", PreferredLanguage = "tr" };

        var skills = router.ExtractRequiredSkills(trace, "ComplaintAgent", profile);

        skills.Should().Contain("complaint")
              .And.Contain("tr");
    }

    [Fact]
    public void ExtractRequiredSkills_VipKeywordFromAdminNote_AddsVipTag()
    {
        var (router, _) = Build();
        var trace = TraceWithIntent("�ikayet");
        var profile = new CustomerProfile
        {
            CustomerId = "c1",
            PreferredLanguage = "tr",
            AdminNote = "VIP m��teri, kurumsal hesap"
        };

        var skills = router.ExtractRequiredSkills(trace, "ComplaintAgent", profile);

        skills.Should().Contain("vip");
    }

    [Fact]
    public void Decide_VipMatchedSkill_ChosenOverSamerSkillNonVip()
    {
        var generic = new HumanAgent
        {
            Id = "generic",
            DisplayName = "Generic",
            Skills = new() { "complaint" },
            Languages = new() { "tr" },
            IsActive = true,
            MaxConcurrentLoad = 5
        };
        var vipExpert = new HumanAgent
        {
            Id = "vip-expert",
            DisplayName = "VIP Expert",
            Skills = new() { "complaint", "vip" },
            Languages = new() { "tr" },
            IsActive = true,
            MaxConcurrentLoad = 5
        };
        var (router, _) = Build(seeds: new[] { generic, vipExpert });

        var profile = new CustomerProfile
        {
            CustomerId = "c1",
            PreferredLanguage = "tr",
            AdminNote = "VIP m��teri"
        };

        var decision = router.Decide(TraceWithIntent("�ikayet"), "ComplaintAgent", profile);

        decision.SuggestedAgentId.Should().Be("vip-expert");
        decision.MatchedSkills.Should().Contain("vip");
    }
}
