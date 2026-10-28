// Tests/EchoChatClient.cs
// LLM'siz, deterministik IChatClient: kullanıcı metnini "echo: ..." diye geri verir. Entegrasyon testleri GERÇEK modele
// gitmemeli (maliyet, ağ bağımlılığı, belirsiz çıktı) — ölçülen şey ajan zekâsı değil, sözleşme/protokol uyumudur.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using ChatResponse = Microsoft.Extensions.AI.ChatResponse;

namespace CustomerSupportBot.Api.IntegrationTests;

internal sealed class EchoChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
    {
        var text = string.Join(" ", m.Where(x => x.Role == ChatRole.User).Select(x => x.Text));
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"echo: {text}")));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> m, ChatOptions? o = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var r = await GetResponseAsync(m, o, ct);
        foreach (var msg in r.Messages) yield return new ChatResponseUpdate(msg.Role, msg.Text);
    }

    public object? GetService(Type t, object? k = null) => null;
    public void Dispose() { }
}
