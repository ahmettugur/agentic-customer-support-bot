// Tests/Services/Workflow/WorkflowExecutorTests.cs

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Workflow;
using CustomerSupportBot.Application.Services.Workflow;
using CustomerSupportBot.Api.Tests.Helpers;
using CustomerSupportBot.Api.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Api.Tests.Services.Workflow;

[Collection("PostgresCatalog")]
public class WorkflowExecutorTests
{
    private readonly WorkflowExecutor _sut;

    public WorkflowExecutorTests(PostgresCatalogFixture fixture)
    {
        _sut = new WorkflowExecutor(
            TestFactory.CreateToolsService(fixture.ProductRepo, fixture.OrderRepo, fixture.ComplaintRepo),
            NullLogger<WorkflowExecutor>.Instance);
    }

    private static WorkflowDefinition BuildDef(params WorkflowStep[] steps) => new()
    {
        Id       = "wf-test",
        Name     = "Test",
        IsActive = true,
        Steps    = steps.ToList()
    };

    // ─── Temel ──────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_InactiveWorkflow_ReturnsErrorWithoutSteps()
    {
        var def = BuildDef();
        def.IsActive = false;

        var result = _sut.Execute(def, "merhaba");

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
        result.StepTraces.Should().BeEmpty();
    }

    [Fact]
    public void Execute_RespondStep_RendersTemplateWithVariables()
    {
        var def = BuildDef(new WorkflowStep
        {
            Id       = "r1",
            Type     = WorkflowStepType.Respond,
            Template = "Merhaba {customerName}, hosgeldin!"
        });

        var result = _sut.Execute(def, "selam",
            new Dictionary<string, string> { ["customerName"] = "Ali" });

        result.Success.Should().BeTrue();
        result.FinalResponse.Should().Be("Merhaba Ali, hosgeldin!");
    }

    [Fact]
    public void Execute_InputPattern_ExtractsVariableFromUserInput()
    {
        var def = BuildDef(new WorkflowStep
        {
            Id       = "r1",
            Type     = WorkflowStepType.Respond,
            Template = "Siparis: {orderId}"
        });
        def.InputPatterns["orderId"] = @"(\d{4,})";

        var result = _sut.Execute(def, "merhaba 1042 hakkinda bilgi alabilir miyim?");

        result.FinalResponse.Should().Be("Siparis: 1042");
        result.FinalVariables.Should().ContainKey("orderId").WhoseValue.Should().Be("1042");
    }

    // ─── Branch (graph-based navigation) ────────────────────────────────────

    [Fact]
    public void Execute_BranchFalse_GoesToOnFalseStep()
    {
        // orderId bulunamayınca OnFalse → r_notfound adımına gider
        var def = BuildDef(
            new WorkflowStep
            {
                Id        = "br1",
                Type      = WorkflowStepType.Branch,
                Condition = "orderId exists",
                OnTrue    = "r_found",
                OnFalse   = "r_notfound"
            },
            new WorkflowStep
            {
                Id       = "r_found",
                Type     = WorkflowStepType.Respond,
                Template = "yakalandı"
            },
            new WorkflowStep
            {
                Id       = "r_notfound",
                Type     = WorkflowStepType.Respond,
                Template = "son"
            }
        );

        var result = _sut.Execute(def, "no order id here");

        result.FinalResponse.Should().Be("son");
    }

    [Fact]
    public void Execute_BranchTrue_GoesToOnTrueStep()
    {
        var def = BuildDef(
            new WorkflowStep
            {
                Id        = "br1",
                Type      = WorkflowStepType.Branch,
                Condition = "orderId exists",
                OnTrue    = "r1",
                OnFalse   = null
            },
            new WorkflowStep
            {
                Id       = "r1",
                Type     = WorkflowStepType.Respond,
                Template = "var: {orderId}"
            }
        );
        def.InputPatterns["orderId"] = @"(\d{4,})";

        var result = _sut.Execute(def, "siparis 1030 hakkinda");

        result.FinalResponse.Should().Be("var: 1030");
    }

    [Fact]
    public void Execute_BranchFalse_OnFalseNull_WorkflowEnds()
    {
        // OnFalse = null → workflow sona erer, FinalResponse boş
        var def = BuildDef(
            new WorkflowStep
            {
                Id        = "br1",
                Type      = WorkflowStepType.Branch,
                Condition = "orderId exists",
                OnTrue    = "r1",
                OnFalse   = null
            },
            new WorkflowStep
            {
                Id       = "r1",
                Type     = WorkflowStepType.Respond,
                Template = "bulundu"
            }
        );

        var result = _sut.Execute(def, "siparis no yok");

        result.Success.Should().BeTrue();
        result.FinalResponse.Should().BeNullOrEmpty();
    }

    // ─── Sequential steps (Next bağlantısı) ──────────────────────────────────

    [Fact]
    public void Execute_SetVariable_RendersAndStores()
    {
        var def = BuildDef(
            new WorkflowStep
            {
                Id            = "sv1",
                Type          = WorkflowStepType.SetVariable,
                VariableName  = "greeting",
                VariableValue = "Sayın {input}",
                Next          = "r1"
            },
            new WorkflowStep
            {
                Id       = "r1",
                Type     = WorkflowStepType.Respond,
                Template = "{greeting}!"
            }
        );

        var result = _sut.Execute(def, "Müsteri");

        result.FinalResponse.Should().Be("Sayın Müsteri!");
    }

    [Fact]
    public void Execute_MultipleRespondSteps_ConcatenatesWithNewline()
    {
        var def = BuildDef(
            new WorkflowStep { Id = "r1", Type = WorkflowStepType.Respond, Template = "Birinci", Next = "r2" },
            new WorkflowStep { Id = "r2", Type = WorkflowStepType.Respond, Template = "İkinci" }
        );

        var result = _sut.Execute(def, "");

        result.FinalResponse.Should().Contain("Birinci");
        result.FinalResponse.Should().Contain("İkinci");
    }

    // ─── Lookup ──────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_LookupForbiddenTool_StepHasError()
    {
        var def = BuildDef(new WorkflowStep
        {
            Id   = "lk1",
            Type = WorkflowStepType.Lookup,
            Tool = WellKnown.ToolNames.OrderPlacement
        });

        var result = _sut.Execute(def, "test");

        result.Success.Should().BeFalse();
        result.StepTraces[0].Error.Should().Contain("HITL");
    }

    [Fact]
    public void Execute_LookupOrderStatus_KnownOrderSucceeds()
    {
        var def = BuildDef(new WorkflowStep
        {
            Id         = "lk1",
            Type       = WorkflowStepType.Lookup,
            Tool       = WellKnown.ToolNames.OrderStatus,
            Parameters = new Dictionary<string, string> { ["orderId"] = "$orderId" },
            StoreAs    = "lookup"
        });
        def.InputPatterns["orderId"] = @"(\d{4,})";

        var result = _sut.Execute(def, "siparis 1030 durumu");

        result.Success.Should().BeTrue();
        result.FinalVariables.Should().ContainKey("lookup.success");
        result.FinalVariables["lookup.success"].Should().Be("true");
    }

    // ─── Cycle detection ─────────────────────────────────────────────────────

    [Fact]
    public void Execute_CyclicGraph_StopsWithError()
    {
        // s1 → s2 → s1 (döngü)
        var def = BuildDef(
            new WorkflowStep { Id = "s1", Type = WorkflowStepType.Respond, Template = "A", Next = "s2" },
            new WorkflowStep { Id = "s2", Type = WorkflowStepType.Respond, Template = "B", Next = "s1" }
        );

        var result = _sut.Execute(def, "");

        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    // ─── EvaluateCondition unit tests ────────────────────────────────────────

    [Theory]
    [InlineData("foo == bar", "foo", "bar", true)]
    [InlineData("foo == bar", "foo", "baz", false)]
    [InlineData("foo != bar", "foo", "baz", true)]
    [InlineData("foo exists", "foo", "x", true)]
    [InlineData("foo exists", "foo", "", false)]
    [InlineData("foo missing", "other", "x", true)]
    public void EvaluateCondition_VariousOps(string expr, string varName, string varValue, bool expected)
    {
        var vars = new Dictionary<string, string> { [varName] = varValue };
        WorkflowExecutor.EvaluateCondition(expr, vars).Should().Be(expected);
    }

    [Fact]
    public void Render_UnknownPlaceholder_LeavesAsIs()
    {
        var rendered = WorkflowExecutor.Render("Hello {missing}!", new Dictionary<string, string>());
        rendered.Should().Be("Hello {missing}!");
    }
}
