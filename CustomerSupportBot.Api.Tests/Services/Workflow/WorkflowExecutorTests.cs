// Tests/Services/Workflow/WorkflowExecutorTests.cs

using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Models.Workflow;
using CustomerSupportBot.Api.Services.Workflow;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Api.Tests.Services.Workflow;

public class WorkflowExecutorTests
{
    private readonly WorkflowExecutor _sut = new(NullLogger<WorkflowExecutor>.Instance);

    private static WorkflowDefinition BuildDef(params WorkflowStep[] steps) => new()
    {
        Id = "wf-test",
        Name = "Test",
        IsActive = true,
        Steps = steps.ToList()
    };

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
            Type = WorkflowStepType.Respond,
            Template = "Merhaba {customerName}, hoşgeldin!"
        });

        var result = _sut.Execute(def, "selam",
            new Dictionary<string, string> { ["customerName"] = "Ali" });

        result.Success.Should().BeTrue();
        result.FinalResponse.Should().Be("Merhaba Ali, hoşgeldin!");
    }

    [Fact]
    public void Execute_InputPattern_ExtractsVariableFromUserInput()
    {
        var def = BuildDef(new WorkflowStep
        {
            Type = WorkflowStepType.Respond,
            Template = "Sipariş: {orderId}"
        });
        def.InputPatterns["orderId"] = @"(ORD-\d+)";

        var result = _sut.Execute(def, "merhaba ORD-42 hakkında bilgi alabilir miyim?");

        result.FinalResponse.Should().Be("Sipariş: ORD-42");
        result.FinalVariables.Should().ContainKey("orderId").WhoseValue.Should().Be("ORD-42");
    }

    [Fact]
    public void Execute_BranchFalse_SkipsNextStep()
    {
        var def = BuildDef(
            new WorkflowStep { Type = WorkflowStepType.Branch, Condition = "orderId exists" },
            new WorkflowStep { Type = WorkflowStepType.Respond, Template = "yakalandı" }, // skip edilmeli
            new WorkflowStep { Type = WorkflowStepType.Respond, Template = "son" }
        );

        var result = _sut.Execute(def, "no order id here");

        result.FinalResponse.Should().Be("son");
    }

    [Fact]
    public void Execute_BranchTrue_RunsNextStep()
    {
        var def = BuildDef(
            new WorkflowStep { Type = WorkflowStepType.Branch, Condition = "orderId exists" },
            new WorkflowStep { Type = WorkflowStepType.Respond, Template = "var: {orderId}" }
        );
        def.InputPatterns["orderId"] = @"(ORD-\d+)";

        var result = _sut.Execute(def, "ORD-1 hakkında");

        result.FinalResponse.Should().Be("var: ORD-1");
    }

    [Fact]
    public void Execute_SetVariable_RendersAndStores()
    {
        var def = BuildDef(
            new WorkflowStep
            {
                Type = WorkflowStepType.SetVariable,
                VariableName = "greeting",
                VariableValue = "Sayın {input}"
            },
            new WorkflowStep { Type = WorkflowStepType.Respond, Template = "{greeting}!" }
        );

        var result = _sut.Execute(def, "Müşteri");

        result.FinalResponse.Should().Be("Sayın Müşteri!");
    }

    [Fact]
    public void Execute_LookupForbiddenTool_StepHasError()
    {
        var def = BuildDef(new WorkflowStep
        {
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
        // FakeDatabase'de ORD-1 var (OrdersDb).
        var def = BuildDef(new WorkflowStep
        {
            Type = WorkflowStepType.Lookup,
            Tool = WellKnown.ToolNames.OrderStatus,
            Parameters = new Dictionary<string, string> { ["orderId"] = "$orderId" },
            StoreAs = "lookup"
        });
        def.InputPatterns["orderId"] = @"(ORD-\d+)";

        var result = _sut.Execute(def, "ORD-1 durumu");

        result.Success.Should().BeTrue();
        result.FinalVariables.Should().ContainKey("lookup.success");
        result.FinalVariables["lookup.success"].Should().Be("true");
    }

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
