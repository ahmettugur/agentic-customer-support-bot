// Endpoints/EvaluationEndpoints.cs
// Evaluation runner için HTTP endpoint'leri.
// - GET  /eval/scenarios          : Mevcut senaryoları listeler (YAML'dan)
// - POST /eval/run                : Tüm senaryoları koşturur
// - POST /eval/run/{id}           : Tek senaryoyu koşturur
// - POST /eval/gate               : Tüm senaryoları koşturur ve CI kapısı kararını verir (geçti 200 / kaldı 422)

using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Services.Evaluation;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Endpoints;

public static class EvaluationEndpoints
{
    public static IEndpointRouteBuilder MapEvaluationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/eval/scenarios", HandleListScenarios);
        app.MapPost("/eval/run", HandleRunAll);
        app.MapPost("/eval/run/{id}", HandleRunOne);
        app.MapPost("/eval/gate", HandleGate);
        return app;
    }

    /// <summary>YAML dosyasındaki senaryoları listeler.</summary>
    private static IResult HandleListScenarios(IWebHostEnvironment env)
    {
        var path = ResolveScenarioPath(env);
        if (path == null) return Results.NotFound(new { error = "evaluation-scenarios.yaml bulunamadı" });

        try
        {
            var file = ScenarioLoader.LoadScenarios(path);
            return Results.Json(new
            {
                version = file.Version,
                totalScenarios = file.Scenarios.Count,
                scenarios = file.Scenarios.Select(s => new
                {
                    s.Id,
                    s.Category,
                    s.Query,
                    s.ExpectedIntent,
                    s.ExpectedBehavior,
                    s.ExpectedAgents,
                    s.ExpectedTools,
                    criteriaCount = s.SuccessCriteria.Count,
                    s.KnownFailureMode
                })
            });
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    /// <summary>Tüm senaryoları (veya limit kadarını) koşturur.</summary>
    private static async Task<IResult> HandleRunAll(
        IEvaluationPort runner,
        IWebHostEnvironment env,
        HttpContext ctx,
        int? limit = null)
    {
        var path = ResolveScenarioPath(env);
        if (path == null) return Results.NotFound(new { error = "evaluation-scenarios.yaml bulunamadı" });

        var file = ScenarioLoader.LoadScenarios(path);
        var scenarios = limit.HasValue ? file.Scenarios.Take(limit.Value).ToList() : file.Scenarios;

        var result = await runner.RunAsync(scenarios, ctx.RequestAborted);
        return Results.Json(result);
    }

    /// <summary>
    /// CI kapısı: tüm senaryoları koşturur, <see cref="EvaluationGate"/> ile karar verir. Geçerse 200, kalırsa 422 —
    /// <c>curl --fail</c> gibi araçlar durum koduyla başarısız olabilir. Yanıt metinleri dönmez: müşteri verisi
    /// içerebilen model çıktısı CI günlüğüne yazılmamalı (ayrıntı için /eval/run/{id}).
    /// </summary>
    private static async Task<IResult> HandleGate(
        IEvaluationPort runner,
        IOptions<EvaluationGateOptions> gateOptions,
        IWebHostEnvironment env,
        HttpContext ctx)
    {
        var path = ResolveScenarioPath(env);
        if (path == null) return Results.NotFound(new { error = "evaluation-scenarios.yaml bulunamadı" });

        var file = ScenarioLoader.LoadScenarios(path);
        var run = await runner.RunAsync(file.Scenarios, ctx.RequestAborted);
        var verdict = EvaluationGate.Evaluate(run, gateOptions.Value);

        return Results.Json(new
        {
            passed = verdict.Passed,
            passRate = verdict.PassRate,
            evaluated = verdict.Evaluated,
            passing = verdict.Passing,
            totalScenarios = run.TotalScenarios,
            reasons = verdict.Reasons,
            scenarios = verdict.Scenarios,
            startedAt = run.StartedAt,
            completedAt = run.CompletedAt
        }, statusCode: verdict.Passed ? StatusCodes.Status200OK : StatusCodes.Status422UnprocessableEntity);
    }

    /// <summary>Tek bir senaryoyu (id ile) koşturur.</summary>
    private static async Task<IResult> HandleRunOne(
        string id,
        IEvaluationPort runner,
        IWebHostEnvironment env,
        HttpContext ctx)
    {
        var path = ResolveScenarioPath(env);
        if (path == null) return Results.NotFound(new { error = "evaluation-scenarios.yaml bulunamadı" });

        var file = ScenarioLoader.LoadScenarios(path);
        var scenario = file.Scenarios.FirstOrDefault(s =>
            s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        if (scenario == null) return Results.NotFound(new { error = $"scenario '{id}' bulunamadı" });

        var result = await runner.RunScenarioAsync(scenario, ctx.RequestAborted);
        return Results.Json(result);
    }

    /// <summary>Evaluation-scenarios.yaml konumunu bulur (bkz. <see cref="ScenarioLoader.Locate"/>).</summary>
    private static string? ResolveScenarioPath(IWebHostEnvironment env) =>
        ScenarioLoader.Locate(WellKnown.Evaluation.ScenarioFileName, env.ContentRootPath);
}

