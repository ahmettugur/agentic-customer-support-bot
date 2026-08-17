namespace CustomerSupportBot.Application.Ports.Outbound;

public enum BrowserMessageKind { Text, Binary, Closed }

public sealed record BrowserMessage(BrowserMessageKind Kind, byte[]? Data)
{
    public string AsText() => Kind == BrowserMessageKind.Text && Data is not null
        ? System.Text.Encoding.UTF8.GetString(Data)
        : string.Empty;
}

/// <summary>
/// Tarayıcı ile çift yönlü mesajlaşma kanalı için secondary (driven) port.
/// WebSocket framing, JSON serileştirme ve bağlantı durum yönetimi bu port'un arkasında gizlenir.
/// </summary>
public interface IBrowserChannel
{
    bool IsOpen { get; }
    IAsyncEnumerable<BrowserMessage> ReceiveMessagesAsync(CancellationToken ct);
    Task SendJsonAsync(object payload, CancellationToken ct);
    Task SendBinaryAsync(byte[] data, CancellationToken ct);
    Task CloseAsync(string reason, CancellationToken ct);
}
