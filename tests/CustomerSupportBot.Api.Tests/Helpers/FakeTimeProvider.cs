namespace CustomerSupportBot.Api.Tests.Helpers;

/// <summary>
/// Elle ilerletilebilen TimeProvider — zamana bağlı pencereleri (ör.
/// <c>SideEffectIdempotencyCache</c>) gerçekten beklemeden test etmek için.
/// Microsoft.Extensions.TimeProvider.Testing paketine bağımlılık eklemeden
/// aynı işi görür; yalnızca GetUtcNow kullanılıyor.
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FakeTimeProvider(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}
