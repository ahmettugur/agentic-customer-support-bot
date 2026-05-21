namespace CustomerSupportBot.Application.Ports.Driving;

public interface IEvaluationPort
{
    Task<EvaluationRunResult> RunAsync(List<EvaluationScenario> scenarios, CancellationToken ct = default);
    Task<ScenarioResult> RunScenarioAsync(EvaluationScenario scenario, CancellationToken ct = default);
}
