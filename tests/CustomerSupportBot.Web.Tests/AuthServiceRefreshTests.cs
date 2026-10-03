// Tests/AuthServiceRefreshTests.cs
//
// Token yenilemenin single-flight garantisi. Refresh token her kullanımda döndürülür
// (rotation) ve sunucu eski token'ın yeniden kullanımını reddeder; bu yüzden aynı scope
// için ikinci bir refresh isteği atmak oturumu düşürür. localStorage (JS interop) ve
// /auth/refresh test ikizleriyle sınanır; sıralama kapılarla belirlenir, zamanlamaya
// bırakılmaz.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerSupportBot.Web.Services;
using Microsoft.JSInterop;

namespace CustomerSupportBot.Web.Tests;

public class AuthServiceRefreshTests
{
    private const string CustomerKey = "cs.auth.customer";

    private static string Stored(string refreshToken) => JsonSerializer.Serialize(
        new AuthTokenData("at-0", refreshToken, "u@example.com", "Customer"),
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    [Fact]
    public async Task ConcurrentCallers_ShareOneRefreshRequest()
    {
        var js = new FakeLocalStorage();
        js.Items[CustomerKey] = Stored("rt-1");
        var server = new FakeRefreshServer("rt-1") { Gate = new() };
        var sut = new AuthService(server.Client(), new AuthTokenStore(js));

        var calls = Enumerable.Range(0, 5).Select(_ => sut.TryRefreshAsync(AuthScope.Customer)).ToList();
        server.Gate.SetResult();
        var results = await Task.WhenAll(calls);

        server.Presented.Should().Equal("rt-1");
        results.Should().OnlyContain(r => r != null && r.AccessToken == results[0]!.AccessToken);
    }

    [Fact]
    public async Task CallerWhoseStorageReadStartedBeforeRotation_DoesNotReuseOldToken()
    {
        var js = new FakeLocalStorage();
        js.Items[CustomerKey] = Stored("rt-1");
        var server = new FakeRefreshServer("rt-1") { Gate = new() };
        var sut = new AuthService(server.Client(), new AuthTokenStore(js));

        // A: rt-1'i okur ve isteği atar (sunucu yanıtı kapıda bekliyor).
        var first = sut.TryRefreshAsync(AuthScope.Customer);
        await server.FirstRequestArrived.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // B: localStorage okuması A'nın rotasyonundan ÖNCE başlıyor, SONRA bitiyor —
        // okuma anındaki (eski) değeri döndürecek.
        var slowRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        js.ReadGate = slowRead.Task;
        var second = sut.TryRefreshAsync(AuthScope.Customer);

        server.Gate.SetResult();
        var a = await first;
        slowRead.SetResult();
        var b = await second;

        server.Presented.Should().Equal(new[] { "rt-1" },
            "döndürülmüş refresh token ikinci kez sunulmamalı — sunucu bunu reddeder");
        b.Should().NotBeNull("ikinci çağıran oturumdan düşürülmemeli");
        b!.AccessToken.Should().Be(a!.AccessToken);
        js.Items[CustomerKey].Should().Contain(a.RefreshToken);
    }

    [Fact]
    public async Task CallAfterCompletion_StartsNewRefreshWithRotatedToken()
    {
        var js = new FakeLocalStorage();
        js.Items[CustomerKey] = Stored("rt-1");
        var server = new FakeRefreshServer("rt-1");
        var sut = new AuthService(server.Client(), new AuthTokenStore(js));

        var first = await sut.TryRefreshAsync(AuthScope.Customer);
        var second = await sut.TryRefreshAsync(AuthScope.Customer);

        server.Presented.Should().Equal("rt-1", first!.RefreshToken);
        second!.AccessToken.Should().NotBe(first.AccessToken);
    }

    [Fact]
    public async Task RejectedRefresh_ClearsStoredTokens()
    {
        var js = new FakeLocalStorage();
        js.Items[CustomerKey] = Stored("rt-revoked");
        var server = new FakeRefreshServer("rt-1");
        var sut = new AuthService(server.Client(), new AuthTokenStore(js));

        (await sut.TryRefreshAsync(AuthScope.Customer)).Should().BeNull();
        js.Items.Should().NotContainKey(CustomerKey);
    }

    [Fact]
    public async Task RejectedBecauseAnotherTabRotated_KeepsThatTabsSession()
    {
        var js = new FakeLocalStorage();
        js.Items[CustomerKey] = Stored("rt-1");
        // Sunucu rt-1'i zaten döndürmüş (diğer sekme) — artık yalnızca rt-2 geçerli.
        var server = new FakeRefreshServer("rt-2") { Gate = new() };
        var sut = new AuthService(server.Client(), new AuthTokenStore(js));

        var refresh = sut.TryRefreshAsync(AuthScope.Customer);
        await server.FirstRequestArrived.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        js.Items[CustomerKey] = Stored("rt-2");   // diğer sekme yeni token'ı yazdı
        server.Gate.SetResult();

        var result = await refresh;

        result.Should().NotBeNull("diğer sekmenin geçerli oturumu kullanılmalı");
        result!.RefreshToken.Should().Be("rt-2");
        js.Items.Should().ContainKey(CustomerKey, "depo silinirse diğer sekme de oturumdan düşer");
    }

    [Fact]
    public async Task NoStoredToken_ReturnsNullWithoutCallingServer()
    {
        var server = new FakeRefreshServer("rt-1");
        var sut = new AuthService(server.Client(), new AuthTokenStore(new FakeLocalStorage()));

        (await sut.TryRefreshAsync(AuthScope.Customer)).Should().BeNull();
        server.Presented.Should().BeEmpty();
    }

    [Fact]
    public async Task Scopes_RefreshIndependently()
    {
        var js = new FakeLocalStorage();
        js.Items[CustomerKey] = Stored("rt-1");
        js.Items["cs.auth.staff"] = Stored("rt-1");
        var server = new FakeRefreshServer("rt-1") { Gate = new(), AcceptAnyToken = true };
        var sut = new AuthService(server.Client(), new AuthTokenStore(js));

        var customer = sut.TryRefreshAsync(AuthScope.Customer);
        var staff = sut.TryRefreshAsync(AuthScope.Staff);
        server.Gate.SetResult();
        await Task.WhenAll(customer, staff);

        server.Presented.Should().HaveCount(2, "staff ve customer ayrı oturumlardır");
    }

    // ─── Test ikizleri ───────────────────────────────────────────────────────

    private sealed class FakeLocalStorage : IJSRuntime
    {
        public Dictionary<string, string> Items { get; } = new();

        /// <summary>Ayarlıysa getItem değeri ÇAĞRI ANINDA okur ama bu kapı açılana dek döndürmez.</summary>
        public Task? ReadGate { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            var key = (string)args![0]!;
            switch (identifier)
            {
                case "localStorage.getItem":
                {
                    string? snapshot;
                    Task? gate;
                    lock (Items)
                    {
                        Items.TryGetValue(key, out snapshot);
                        gate = ReadGate;
                    }
                    if (gate is not null) await gate;
                    else await Task.Yield();
                    return (TValue)(object?)snapshot!;
                }
                case "localStorage.setItem":
                    lock (Items) Items[key] = (string)args[1]!;
                    break;
                case "localStorage.removeItem":
                    lock (Items) Items.Remove(key);
                    break;
            }
            await Task.Yield();
            return default!;
        }
    }

    private sealed class FakeRefreshServer(string initialToken) : HttpMessageHandler
    {
        private string _valid = initialToken;
        private int _issued;
        private readonly List<string> _presented = new();

        public TaskCompletionSource? Gate { get; init; }
        public bool AcceptAnyToken { get; init; }
        public TaskCompletionSource FirstRequestArrived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> Presented { get { lock (_presented) return _presented.ToList(); } }

        public HttpClient Client() => new(this) { BaseAddress = new Uri("http://localhost") };

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var token = body.GetProperty("refreshToken").GetString()!;
            lock (_presented) _presented.Add(token);
            FirstRequestArrived.TrySetResult();

            if (Gate is not null) await Gate.Task;

            lock (_presented)
            {
                if (!AcceptAnyToken && token != _valid)
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);

                var n = Interlocked.Increment(ref _issued);
                _valid = $"rt-{n + 1}";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new AuthTokenData($"at-{n}", _valid, "u@example.com", "Customer"))
                };
            }
        }
    }
}
