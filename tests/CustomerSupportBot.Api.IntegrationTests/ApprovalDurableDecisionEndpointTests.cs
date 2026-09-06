using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomerSupportBot.Api.IntegrationTests;

public class ApprovalDurableDecisionEndpointTests
{
    [Theory]
    [InlineData("/approvals/id/approve")]
    [InlineData("/agent/approvals/id/approve")]
    public async Task ApprovalEndpoint_UsesDurableRequest_AndPreservesReasonRequirement(string path)
    {
        var queue = Substitute.For<IApprovalQueue>();
        queue.GetAsync("id", Arg.Any<CancellationToken>()).Returns(new ApprovalRequest
            { Id = "id", CustomerId = "1001", ToolName = WellKnown.ToolNames.OrderCancel });
        queue.DecideAsync("id", true, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(true);
        using var root = new TestWebApplicationFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IApprovalQueue>();
            services.AddSingleton(queue);
        }));
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>().GenerateAccessToken(
            new UserInfo("admin", "admin", "", "Admin", "agent", true, DateTime.UtcNow, null), DateTime.UtcNow).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var missingReason = await client.PostAsJsonAsync(path, new { }, TestContext.Current.CancellationToken);
        missingReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        queue.ReceivedCalls().Should().NotContain(c => c.GetMethodInfo().Name == "DecideAsync");
        var approved = await client.PostAsJsonAsync(path, new { reason = "verified request" }, TestContext.Current.CancellationToken);
        approved.StatusCode.Should().Be(HttpStatusCode.OK);
        queue.DidNotReceive().Get(Arg.Any<string>());
    }
}
