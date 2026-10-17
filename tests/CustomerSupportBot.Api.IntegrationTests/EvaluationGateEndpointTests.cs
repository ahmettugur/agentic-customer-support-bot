// POST /eval/gate — CI'ın çağırdığı kapı. Değerlendirme (LLM) burada sahte bir port ile değiştirilir; asıl
// karar mantığı EvaluationGateTests'te. Burada: HTTP durumu (geçti 200 / kaldı 422), yetki ve yanıt şekli.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomerSupportBot.Api.IntegrationTests;

public class EvaluationGateEndpointTests : IClassFixture<EvaluationGateEndpointTests.Factory>
{
    public static volatile bool AllPass = true;

    private sealed class FakeEvaluation : IEvaluationPort
    {
        public Task<EvaluationRunResult> RunAsync(List<EvaluationScenario> scenarios, CancellationToken ct = default)
        {
            // Her senaryo için tek otomatik kriter; AllPass=false iken hepsi kalır.
            var results = scenarios.Select(s => new ScenarioResult
            {
                ScenarioId = s.Id,
                TotalCriteria = 1,
                PassedCriteria = AllPass ? 1 : 0,
                CriteriaResults = [new CriterionResult { Criterion = "fake", Passed = AllPass, Evaluation = AllPass ? "ok" : "kötü" }],
                Response = "GİZLİ-YANIT-METNİ"
            }).ToList();
            return Task.FromResult(new EvaluationRunResult { TotalScenarios = results.Count, Results = results });
        }

        public Task<ScenarioResult> RunScenarioAsync(EvaluationScenario scenario, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    public sealed class Factory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IEvaluationPort, FakeEvaluation>()));
        }
    }

    private readonly Factory _factory;
    public EvaluationGateEndpointTests(Factory factory) => _factory = factory;

    private HttpClient Client(string role)
    {
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>();
        var user = new UserInfo(
            Id: Guid.NewGuid().ToString("N"), Username: $"u-{role}", PasswordHash: "", Role: role,
            LinkedAgentId: null, IsActive: true, CreatedAt: DateTime.UtcNow, LastLoginAt: null,
            LinkedCustomerId: role == "Customer" ? "1001" : null);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", provider.GenerateAccessToken(user, DateTime.UtcNow).Token);
        return client;
    }

    [Fact]
    public async Task PassingRun_Returns200_WithVerdict_AndWithoutResponseText()
    {
        AllPass = true;
        var ct = TestContext.Current.CancellationToken;

        var response = await Client("Admin").PostAsync("/eval/gate", null, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("passed").GetBoolean().Should().BeTrue();
        json.GetProperty("passRate").GetDouble().Should().Be(1.0);
        json.GetProperty("scenarios").GetArrayLength().Should().BeGreaterThan(0);
        body.Should().NotContain("GİZLİ-YANIT-METNİ", "müşteri verisi içerebilen yanıt metni CI günlüğüne dökülmemeli");
    }

    [Fact]
    public async Task FailingRun_Returns422_WithReasons()
    {
        AllPass = false;
        var ct = TestContext.Current.CancellationToken;

        var response = await Client("Admin").PostAsync("/eval/gate", null, ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var json = (await response.Content.ReadFromJsonAsync<JsonElement>(ct));
        json.GetProperty("passed").GetBoolean().Should().BeFalse();
        json.GetProperty("reasons").GetArrayLength().Should().BeGreaterThan(0);
        AllPass = true;
    }

    [Theory]
    [InlineData("Agent")]
    [InlineData("Customer")]
    public async Task NonAdmins_AreRejected(string role)
    {
        var response = await Client(role).PostAsync("/eval/gate", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Anonymous_IsRejected()
    {
        var response = await _factory.CreateClient().PostAsync("/eval/gate", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
