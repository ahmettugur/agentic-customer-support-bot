using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests.Voice;

internal sealed class OptionsMonitorStub<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
