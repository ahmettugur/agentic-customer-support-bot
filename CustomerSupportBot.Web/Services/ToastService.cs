namespace CustomerSupportBot.Web.Services;

public enum ToastType { Success, Error, Warning, Info }

public sealed record ToastMessage(string Text, ToastType Type, Guid Id = default)
{
    public Guid Id { get; init; } = Id == default ? Guid.NewGuid() : Id;
}

public sealed class ToastService
{
    public event Action<ToastMessage>? OnShow;

    public void ShowSuccess(string text) => OnShow?.Invoke(new ToastMessage(text, ToastType.Success));
    public void ShowError(string text)   => OnShow?.Invoke(new ToastMessage(text, ToastType.Error));
    public void ShowWarning(string text) => OnShow?.Invoke(new ToastMessage(text, ToastType.Warning));
    public void ShowInfo(string text)    => OnShow?.Invoke(new ToastMessage(text, ToastType.Info));
}
