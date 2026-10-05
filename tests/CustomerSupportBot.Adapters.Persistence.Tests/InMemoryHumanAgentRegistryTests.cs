// Tests/Services/Routing/InMemoryHumanAgentRegistryTests.cs

using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Options;
using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Services.Routing;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public class InMemoryHumanAgentRegistryTests
{
    private static InMemoryHumanAgentRegistry Build(RoutingOptions? opts = null) =>
        new(Options.Create(opts ?? new RoutingOptions()));

    [Fact]
    public void Empty_GetAll_ReturnsEmpty()
    {
        var sut = Build();
        sut.GetAll().Should().BeEmpty();
        sut.GetActive().Should().BeEmpty();
    }

    [Fact]
    public void Seed_NormalizesSkillsAndLanguages()
    {
        var opts = new RoutingOptions
        {
            SeedAgents = new()
            {
                new HumanAgent
                {
                    DisplayName = "A1",
                    Skills = new() { "  Complaint  ", "REFUND", "complaint" },
                    Languages = new() { "TR", "tr", "en" }
                }
            }
        };
        var sut = Build(opts);
        var a = sut.GetAll().Single();
        a.Skills.Should().BeEquivalentTo(new[] { "complaint", "refund" });
        a.Languages.Should().BeEquivalentTo(new[] { "tr", "en" });
    }

    [Fact]
    public void Create_GeneratesIdWhenMissing()
    {
        var sut = Build();
        var created = sut.Create(new HumanAgent { DisplayName = "X" });
        created.Id.Should().NotBeNullOrWhiteSpace();
        sut.Get(created.Id).Should().NotBeNull();
    }

    [Fact]
    public void Update_PartialFields_OnlyTouchesProvidedOnes()
    {
        var sut = Build();
        var a = sut.Create(new HumanAgent
        {
            DisplayName = "Old",
            Skills = new() { "complaint" },
            IsActive = true,
            Priority = 1
        });

        var updated = sut.Update(a.Id, new HumanAgentInput
        {
            DisplayName = "New",
            IsActive = false
        });

        updated.Should().NotBeNull();
        updated.DisplayName.Should().Be("New");
        updated.IsActive.Should().BeFalse();
        updated.Priority.Should().Be(1); // korundu
        updated.Skills.Should().BeEquivalentTo(new[] { "complaint" }); // korundu
    }

    [Fact]
    public void Update_NewSkills_ReplacesOldList()
    {
        var sut = Build();
        var a = sut.Create(new HumanAgent { DisplayName = "X", Skills = new() { "old" } });

        sut.Update(a.Id, new HumanAgentInput { Skills = new() { "complaint", "refund" } });

        sut.Get(a.Id)!.Skills.Should().BeEquivalentTo(new[] { "complaint", "refund" });
    }

    [Fact]
    public void GetActive_ExcludesInactive()
    {
        var sut = Build();
        sut.Create(new HumanAgent { DisplayName = "Active", IsActive = true });
        sut.Create(new HumanAgent { DisplayName = "Off", IsActive = false });

        sut.GetActive().Should().HaveCount(1).And.Contain(a => a.DisplayName == "Active");
    }

    [Fact]
    public void Delete_RemovesAgent()
    {
        var sut = Build();
        var a = sut.Create(new HumanAgent { DisplayName = "X" });
        sut.Delete(a.Id).Should().BeTrue();
        sut.Get(a.Id).Should().BeNull();
        sut.Delete("non-existent").Should().BeFalse();
    }

    [Fact]
    public void IncrementLoad_UpdatesCountAndLastAssignedAt()
    {
        var sut = Build();
        var a = sut.Create(new HumanAgent { DisplayName = "X" });

        sut.IncrementLoad(a.Id).Should().BeTrue();
        sut.IncrementLoad(a.Id).Should().BeTrue();

        var fresh = sut.Get(a.Id)!;
        fresh.CurrentLoad.Should().Be(2);
        fresh.LastAssignedAt.Should().NotBeNull();
    }

    [Fact]
    public void DecrementLoad_NeverGoesBelowZero()
    {
        var sut = Build();
        var a = sut.Create(new HumanAgent { DisplayName = "X" });

        sut.DecrementLoad(a.Id);
        sut.DecrementLoad(a.Id);

        sut.Get(a.Id)!.CurrentLoad.Should().Be(0);
    }

    [Fact]
    public void IncrementLoad_OnUnknownId_ReturnsFalse()
    {
        var sut = Build();
        sut.IncrementLoad("does-not-exist").Should().BeFalse();
    }

    // ─── Çevrimiçi/uzakta durumu ─────────────────────────────────────────────

    [Fact]
    public void SetPresence_StampsSince_OnlyWhenThePresenceChanges()
    {
        var sut = Build();
        var a = sut.Create(new HumanAgent { DisplayName = "X" });
        var t0 = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

        sut.SetPresence(a.Id, AgentPresence.Online, t0).Should().BeTrue();
        sut.SetPresence(a.Id, AgentPresence.Online, t0.AddMinutes(1)).Should().BeTrue();

        var fresh = sut.Get(a.Id)!;
        fresh.Presence.Should().Be(AgentPresence.Online);
        fresh.PresenceChangedAt.Should().Be(t0);
        fresh.LastSeenAt.Should().Be(t0.AddMinutes(1));
    }

    [Fact]
    public void TouchPresence_UpdatesOnlyLastSeen()
    {
        var sut = Build();
        var a = sut.Create(new HumanAgent { DisplayName = "X" });
        var t0 = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        sut.SetPresence(a.Id, AgentPresence.Away, t0);

        sut.TouchPresence(a.Id, t0.AddSeconds(30)).Should().BeTrue();

        var fresh = sut.Get(a.Id)!;
        fresh.Presence.Should().Be(AgentPresence.Away);
        fresh.PresenceChangedAt.Should().Be(t0);
        fresh.LastSeenAt.Should().Be(t0.AddSeconds(30));
    }

    [Fact]
    public void Presence_OnUnknownId_ReturnsFalse()
    {
        var sut = Build();
        sut.SetPresence("yok", AgentPresence.Online, DateTime.UtcNow).Should().BeFalse();
        sut.TouchPresence("yok", DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void AdminUpdate_DoesNotTouchPresence()
    {
        var sut = Build();
        var a = sut.Create(new HumanAgent { DisplayName = "X" });
        sut.SetPresence(a.Id, AgentPresence.Online, DateTime.UtcNow);

        sut.Update(a.Id, new HumanAgentInput { DisplayName = "Y", IsActive = true });

        sut.Get(a.Id)!.Presence.Should().Be(AgentPresence.Online);
    }
}
