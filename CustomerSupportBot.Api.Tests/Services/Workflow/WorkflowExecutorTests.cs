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
        def.InputPatterns["orderId"] = @"(\d{4,})";

        var result = _sut.Execute(def, "merhaba 1042 hakkında bilgi alabilir miyim?");

        result.FinalResponse.Should().Be("Sipariş: 1042");
        result.FinalVariables.Should().ContainKey("orderId").WhoseValue.Should().Be("1042");
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
        def.InputPatterns["orderId"] = @"(\d{4,})";

        var result = _sut.Execute(def, "sipariş 1030 hakkında");

        result.FinalResponse.Should().Be("var: 1030");
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
        // Fake repo'de 1030 var (Northwind seed).
        var def = BuildDef(new WorkflowStep
        {
            Type = WorkflowStepType.Lookup,
            Tool = WellKnown.ToolNames.OrderStatus,
            Parameters = new Dictionary<string, string> { ["orderId"] = "$orderId" },
            StoreAs = "lookup"
        });
        def.InputPatterns["orderId"] = @"(\d{4,})";

        var result = _sut.Execute(def, "sipariş 1030 durumu");

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
