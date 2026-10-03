// Infrastructure/SseForwarder.cs
// Thread-safe SSE event forwarding with automatic lock management.
// Encapsulates the SemaphoreSlim pattern used across chat endpoints.

namespace CustomerSupportBot.Api.Infrastructure;

using CustomerSupportBot.Application.Ports.Inbound;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Thread-safe SSE event writer that manages concurrent access to the HTTP response.
/// Automatically handles SemaphoreSlim acquisition/release and cancellation checks.
/// </summary>
public sealed class SseForwarder : IDisposable
{
    private readonly HttpResponse _response;
    private readonly CancellationToken _cancellationToken;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public SseForwarder(HttpResponse response, CancellationToken cancellationToken, ILogger? logger = null)
    {
        _response = response ?? throw new ArgumentNullException(nameof(response));
        _cancellationToken = cancellationToken;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Writes an SSE event to the response stream in a thread-safe manner.
    /// Silently handles client disconnections and cancellations.
    /// </summary>
    public async Task WriteAsync(string eventType, object? data)
    {
        if (_disposed || _cancellationToken.IsCancellationRequested)
            return;

        await _lock.WaitAsync(_cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_cancellationToken.IsCancellationRequested)
            {
                await SseWriter.WriteEventAsync(_response, eventType, data, _cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected - expected, no action needed
        }
        catch (Exception ex)
        {
            // Write failures (client closed connection) are expected in SSE
            // Swallow to avoid breaking the stream processing — but leave a trace: a write
            // that fails for any OTHER reason (e.g. a payload that cannot be serialized)
            // would otherwise silently drop events.
            _logger.LogDebug(ex, "SSE write failed | event={EventType}", eventType);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Writes the done event and flushes the response.
    /// </summary>
    public async Task WriteDoneAsync(string sessionId)
    {
        await WriteAsync(StreamEventTypes.Done, new { sessionId });
    }

    /// <summary>
    /// Writes an error event to the client.
    /// </summary>
    public async Task WriteErrorAsync(string message)
    {
        await WriteAsync(StreamEventTypes.Error, new { message });
    }

    /// <summary>
    /// Writes the initial session event.
    /// </summary>
    public async Task WriteSessionAsync(string sessionId)
    {
        await WriteAsync(StreamEventTypes.Session, new { sessionId });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lock.Dispose();
    }
}
