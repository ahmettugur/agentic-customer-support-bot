using Microsoft.JSInterop;

namespace CustomerSupportBot.Web.Services;

public sealed class ThemeService
{
    private readonly IJSRuntime _js;
    private bool _isDark;
    private bool _initialized;

    public event Action? OnChange;
    public bool IsDark => _isDark;

    public ThemeService(IJSRuntime js) => _js = js;

    public async ValueTask EnsureInitAsync()
    {
        if (_initialized) return;
        var theme = await _js.InvokeAsync<string>("csbTheme.get");
        _isDark = theme == "dark";
        _initialized = true;
    }

    public async Task ToggleAsync()
    {
        var next = await _js.InvokeAsync<string>("csbTheme.toggle");
        _isDark = next == "dark";
        OnChange?.Invoke();
    }
}
