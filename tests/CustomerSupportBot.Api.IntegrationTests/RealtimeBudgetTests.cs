// Harcama limiti aşıldıysa yeni sesli bağlantı açılmaz (her bağlantı gerçek bir Realtime oturumu açar).

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomerSupportBot.Api.IntegrationTests;

public class RealtimeBudgetTests
{
    private sealed class Factory : TestWebApplicationFactory
    {
        public ILlmSpendGuard Guard { get; } = Substitute.For<ILlmSpendGuard>();
        public IRealtimeNativeBridge Bridge { get; } = Substitute.For<IRealtimeNativeBridge>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILlmSpendGuard>();
                services.AddSingleton(Guard);
                services.RemoveAll<IRealtimeNativeBridge>();
                services.AddSingleton(Bridge);
            });
        }
    }

    [Fact]
    public async Task OverBudget_TheSocketIsRefusedWith503()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new Factory();
        factory.Guard.CheckAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new LlmBudgetExceeded(LlmBudgetScope.Daily, 50m, 51m));
        string token;
        using (var scope = factory.Services.CreateScope())
        {
            token = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>().GenerateAccessToken(
                new UserInfo(Guid.NewGuid().ToString("N"), "musteri", "", "Customer", null, true, DateTime.UtcNow, null, "1001"),
                DateTime.UtcNow).Token;
        }
        var client = factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = r => r.Headers.Authorization = $"Bearer {token}";

        var connect = () => client.ConnectAsync(new Uri(factory.Server.BaseAddress, "/chat/realtime-native/rt-budget-1"), ct);

        (await connect.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("503");
        factory.Bridge.ReceivedCalls().Should().BeEmpty("soket açılmadı, Realtime oturumu başlamadı");
    }
}
