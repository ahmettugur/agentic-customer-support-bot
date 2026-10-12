// Şikayet uçları: yetki, kapsam ve durum kodu eşlemesi. İş mantığı ComplaintManagementServiceTests'te.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Auth;
using CustomerSupportBot.Domain.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomerSupportBot.Api.IntegrationTests;

public class ComplaintEndpointTests : IClassFixture<ComplaintEndpointTests.Factory>
{
    public sealed class Factory : TestWebApplicationFactory
    {
        public IComplaintManagementPort Port { get; } = Substitute.For<IComplaintManagementPort>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IComplaintManagementPort>();
                services.AddSingleton(Port);
            });
        }
    }

    private readonly Factory _factory;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ComplaintInfo C(string status, string? assigned = null) => new()
    {
        OrderId = "1030", CustomerId = "1001", Complaint = "kırık", Status = status, AssignedAgentId = assigned, Version = 4,
        CreatedAt = DateTime.UtcNow.AddHours(-1), UpdatedAt = DateTime.UtcNow
    };

    public ComplaintEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Port.ClearReceivedCalls();
        _factory.Port.ListAsync(Arg.Any<ComplaintQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ComplaintPage([new ComplaintListItem("1006", C(WellKnown.ComplaintStatuses.Pending))], 1));
        _factory.Port.CountsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new ComplaintCounts(2, 3, 1));
        _factory.Port.GetAsync("1006", Arg.Any<CancellationToken>()).Returns(new ComplaintDetail("1006",
            C(WellKnown.ComplaintStatuses.InProgress, "agent-1"),
            new OrderInfo { CustomerId = "1001", Status = "Teslim Edildi", Lines = [new OrderLine("Kahve", 1)] },
            ["att-1"], [new ComplaintEvent(DateTime.UtcNow, "ayse", ComplaintEventKinds.Note, null, null, null, "iç not")]));
        _factory.Port.GetAsync("404", Arg.Any<CancellationToken>()).Returns((ComplaintDetail?)null);
        _factory.Port.ExecuteAsync(Arg.Any<string>(), Arg.Any<ComplaintActor>(), Arg.Any<ComplaintCommand>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<string>(0) switch
            {
                "404" => new ComplaintCommandResult(ComplaintCommandStatus.NotFound),
                "409" => new ComplaintCommandResult(ComplaintCommandStatus.Conflict, C(WellKnown.ComplaintStatuses.InProgress, "b2"), "güncellendi"),
                "403" => new ComplaintCommandResult(ComplaintCommandStatus.Forbidden, C(WellKnown.ComplaintStatuses.InProgress, "b2"), "Bu şikayet size atanmamış."),
                "400" => new ComplaintCommandResult(ComplaintCommandStatus.Invalid, C(WellKnown.ComplaintStatuses.InProgress), "Açıklama zorunludur."),
                _ => new ComplaintCommandResult(ComplaintCommandStatus.Updated, C(WellKnown.ComplaintStatuses.InProgress, "agent-1"))
            });
    }

    private HttpClient ClientFor(string role, string? linkedAgentId = "agent-1")
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>().GenerateAccessToken(
            new UserInfo(Guid.NewGuid().ToString("N"), "ayse", "", role,
                role == "Agent" ? linkedAgentId : null, true, DateTime.UtcNow, null, role == "Customer" ? "1001" : null),
            DateTime.UtcNow).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Admin_List_ReturnsItemsWithActionsAndSla()
    {
        var json = await ClientFor("Admin").GetFromJsonAsync<JsonElement>("/complaints?status=open", Ct);

        json.GetProperty("total").GetInt32().Should().Be(1);
        var item = json.GetProperty("items")[0];
        item.GetProperty("complaintId").GetString().Should().Be("1006");
        item.GetProperty("actions").EnumerateArray().Select(a => a.GetString()).Should().Equal("assign", "note");
        item.GetProperty("sla").GetString().Should().Be("ok");
        await _factory.Port.Received(1).ListAsync(Arg.Is<ComplaintQuery>(q => q.Status == "open" && q.VisibleToAgentId == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Agent_List_IsScopedToUnassignedAndMine()
    {
        await ClientFor("Agent").GetFromJsonAsync<JsonElement>("/agent/complaints?mine=true", Ct);

        await _factory.Port.Received(1).ListAsync(
            Arg.Is<ComplaintQuery>(q => q.VisibleToAgentId == "agent-1" && q.AssignedAgentId == "agent-1"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Agent_Claim_PassesActor()
    {
        var response = await ClientFor("Agent").PostAsJsonAsync("/agent/complaints/1006/claim", new { expectedVersion = 4 }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Port.Received(1).ExecuteAsync("1006",
            Arg.Is<ComplaintActor>(a => a.Role == ComplaintActorRole.Agent && a.AgentId == "agent-1" && a.Name == "ayse"),
            Arg.Is<ComplaintCommand>(c => c.Action == ComplaintAction.Claim && c.ExpectedVersion == 4), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Agent_CannotResolveSomeoneElses()
        => (await ClientFor("Agent").PostAsJsonAsync("/agent/complaints/403/resolve", new { text = "x" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

    [Fact]
    public async Task StatusCodes()
    {
        var admin = ClientFor("Admin");
        (await admin.PostAsJsonAsync("/complaints/404/resolve", new { text = "x" }, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsJsonAsync("/complaints/400/resolve", new { text = "" }, Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var conflict = await admin.PostAsJsonAsync("/complaints/409/assign", new { agentId = "agent-1" }, Ct);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await conflict.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("currentStatus").GetString().Should().Be(WellKnown.ComplaintStatuses.InProgress);
        body.GetProperty("version").GetInt32().Should().Be(4);
        (await admin.GetAsync("/complaints/404", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Detail_IncludesEventsAndPhotos()
    {
        var json = await ClientFor("Agent").GetFromJsonAsync<JsonElement>("/agent/complaints/1006", Ct);

        json.GetProperty("attachmentIds")[0].GetString().Should().Be("att-1");
        json.GetProperty("events")[0].GetProperty("note").GetString().Should().Be("iç not");
        json.GetProperty("order").GetProperty("linesSummary").GetString().Should().Be("Kahve x1");
        json.GetProperty("complaint").GetProperty("actions").EnumerateArray().Select(a => a.GetString())
            .Should().Equal("resolve", "reject", "note");
    }

    [Fact]
    public async Task Counts()
    {
        var json = await ClientFor("Agent").GetFromJsonAsync<JsonElement>("/agent/complaints/counts", Ct);
        json.GetProperty("assignedToMe").GetInt32().Should().Be(1);
        await _factory.Port.Received(1).CountsAsync("agent-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorization()
    {
        (await ClientFor("Agent").GetAsync("/complaints", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ClientFor("Agent").PostAsJsonAsync("/complaints/1006/assign", new { agentId = "x" }, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ClientFor("Customer").GetAsync("/agent/complaints", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ClientFor("Agent", linkedAgentId: null).PostAsJsonAsync("/agent/complaints/1006/claim", new { }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
