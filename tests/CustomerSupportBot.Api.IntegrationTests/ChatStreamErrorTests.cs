// /chat/stream'de TUR ORTASINDA oluşan hata.
//
// SSE'de ilk olay yazıldığında yanıt başlamış olur; sonrasında HTTP durum kodu/ProblemDetails
// yazılamaz (DomainExceptionHandler HasStarted'da devreden çıkar). Eskiden exception yukarı
// fırlıyor ve bağlantı kopuyordu: istemci ne bir hata olayı ne de "done" görüyor, yarım bir
// balonla kalıyordu. Artık hata akış içinde güvenli bir "error" olayı olarak bildirilir.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Domain.Exceptions;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomerSupportBot.Api.IntegrationTests;

public class ChatStreamErrorTests
{
    private sealed class FailingChatPort(Exception error, bool failBeforeFirstEvent) : IChatPort
    {
        public Task<ChatResponse> HandleAsync(ChatRequest request, CancellationToken ct = default) =>
            throw error;

        public async IAsyncEnumerable<StreamEvent> HandleStreamAsync(
            ChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            if (failBeforeFirstEvent) throw error;

            yield return new StreamEvent(StreamEventTypes.Session, new SessionEventPayload("s-stream-err"));
            yield return new StreamEvent(StreamEventTypes.ResponseStart, new { });
            throw error;
        }
    }

    private sealed class Factory(Exception error, bool failBeforeFirstEvent) : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s =>
                s.Replace(ServiceDescriptor.Scoped<IChatPort>(_ => new FailingChatPort(error, failBeforeFirstEvent))));
        }
    }

    private static HttpClient CustomerClient(TestWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>();
        var user = new UserInfo(
            Id: "user-1001", Username: "1001@example.com", PasswordHash: "", Role: "Customer",
            LinkedAgentId: null, IsActive: true, CreatedAt: DateTime.UtcNow, LastLoginAt: null,
            LinkedCustomerId: "1001");

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", provider.GenerateAccessToken(user, DateTime.UtcNow).Token);
        return client;
    }

    [Fact]
    public async Task FailureAfterStreamStarted_EmitsSafeErrorEventAndDone()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new Factory(
            new InvalidOperationException("Npgsql: connection to 10.0.0.12 refused"), failBeforeFirstEvent: false);

        var response = await CustomerClient(factory).PostAsJsonAsync("/chat/stream", new { query = "merhaba" }, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        body.Should().Contain("event: session");
        body.Should().Contain("event: error");
        body.Should().Contain("event: done", "istemci akışın bittiğini görmeli");
        body.Should().NotContain("10.0.0.12", "iç hata ayrıntısı istemciye sızmamalı");
    }

    [Fact]
    public async Task ClientFacingDomainFailure_ShowsItsMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new Factory(new EntityNotFoundException("Order", "ORD-9"), failBeforeFirstEvent: false);

        var response = await CustomerClient(factory).PostAsJsonAsync("/chat/stream", new { query = "merhaba" }, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        body.Should().Contain("event: error");
        body.Should().Contain("ORD-9", "4xx domain mesajları HTTP yanıtında olduğu gibi gösterilir");
    }

    /// <summary>
    /// SSE JSON'u Türkçe karakterleri olduğu gibi taşır ama HTML'e duyarlı karakterleri kaçırır
    /// (bkz. ApiJsonEncoder) — istemci veriyi bir gün HTML bağlamına koyarsa enjeksiyon olmasın.
    /// </summary>
    [Fact]
    public async Task StreamJson_KeepsTurkishReadable_ButEscapesHtml()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new Factory(
            new EntityNotFoundException("Sipariş", "<script>alert(1)</script>"), failBeforeFirstEvent: false);

        var response = await CustomerClient(factory).PostAsJsonAsync("/chat/stream", new { query = "merhaba" }, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        body.Should().Contain("Sipariş", "Türkçe karakterler \\uXXXX'e çevrilmemeli");
        body.Should().NotContain("<script>");
        body.Should().Contain("\\u003Cscript\\u003E");
    }

    [Fact]
    public async Task FailureBeforeAnyEvent_IsLeftToTheGlobalHandler()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new Factory(new EntityNotFoundException("Order", "ORD-9"), failBeforeFirstEvent: true);

        var response = await CustomerClient(factory).PostAsJsonAsync("/chat/stream", new { query = "merhaba" }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "yanıt başlamadıysa doğru HTTP durum kodu hâlâ yazılabilir");
    }
}
