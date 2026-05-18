// Tests/Services/Workflow/InMemoryWorkflowDefinitionStoreTests.cs

using CustomerSupportBot.Domain.Model.Workflow;
using CustomerSupportBot.Api.Services.Workflow;

namespace CustomerSupportBot.Api.Tests.Services.Workflow;

public class InMemoryWorkflowDefinitionStoreTests
{
    [Fact]
    public void Empty_GetAll_ReturnsEmpty()
    {
        var sut = new InMemoryWorkflowDefinitionStore();
        sut.GetAll().Should().BeEmpty();
        sut.GetActive().Should().BeEmpty();
    }

    [Fact]
    public void Upsert_NoIdProvided_GeneratesSlugFromName()
    {
        var sut = new InMemoryWorkflowDefinitionStore();
        var saved = sut.Upsert(new WorkflowDefinition { Name = "Sipariþ Durumu Akýþý" });

        saved.Id.Should().NotBeNullOrWhiteSpace();
        saved.Id.Should().NotContain(" ").And.NotContain("þ").And.NotContain("ý");
    }

    [Fact]
    public void Upsert_ExistingId_IncrementsVersion()
    {
        var sut = new InMemoryWorkflowDefinitionStore();
        var first = sut.Upsert(new WorkflowDefinition { Id = "wf1", Name = "X" });
        first.Version.Should().Be(1);

        var second = sut.Upsert(new WorkflowDefinition { Id = "wf1", Name = "X" });
        second.Version.Should().Be(2);
    }

    [Fact]
    public void Upsert_TracksUpdatedBy()
    {
        var sut = new InMemoryWorkflowDefinitionStore();
        var saved = sut.Upsert(new WorkflowDefinition { Name = "Y" }, "admin");
        saved.UpdatedBy.Should().Be("admin");
    }

    [Fact]
    public void GetActive_ExcludesInactive()
    {
        var sut = new InMemoryWorkflowDefinitionStore();
        sut.Upsert(new WorkflowDefinition { Id = "a", Name = "A", IsActive = true });
        sut.Upsert(new WorkflowDefinition { Id = "b", Name = "B", IsActive = false });

        sut.GetActive().Should().HaveCount(1).And.Contain(d => d.Id == "a");
    }

    [Fact]
    public void Delete_RemovesEntry()
    {
        var sut = new InMemoryWorkflowDefinitionStore();
        var saved = sut.Upsert(new WorkflowDefinition { Id = "x", Name = "X" });

        sut.Delete(saved.Id).Should().BeTrue();
        sut.Get(saved.Id).Should().BeNull();
        sut.Delete("non-existent").Should().BeFalse();
    }

    [Fact]
    public void Get_PreservesCreatedAtAcrossUpserts()
    {
        var sut = new InMemoryWorkflowDefinitionStore();
        var first = sut.Upsert(new WorkflowDefinition { Id = "wf", Name = "n" });
        var originalCreatedAt = first.CreatedAt;

        var second = sut.Upsert(new WorkflowDefinition { Id = "wf", Name = "n2" });
        second.CreatedAt.Should().Be(originalCreatedAt);
    }
}
