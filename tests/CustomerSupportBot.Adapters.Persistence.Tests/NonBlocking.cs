// Tests/NonBlocking.cs
// "Çağrı, DB beklerken çağıranın thread'ini bloklıyor mu?" ölçümü — tek yerde.

namespace CustomerSupportBot.Adapters.Persistence.Tests;

internal static class NonBlocking
{
    /// <summary>
    /// <paramref name="call"/>'ın SENKRON kısmını (Task dönene kadar geçen süre) ayrı bir
    /// thread'de çalıştırır ve kapı kapalıyken makul sürede dönüp dönmediğini ölçer. Gerçekten
    /// asenkron bir çağrı, DB beklenirken TAMAMLANMAMIŞ bir Task ile hemen döner;
    /// senkron-üstü-asenkron bir çağrı ise kapı açılana kadar asılı kalır. Kapı her durumda
    /// açıldığı için test asılı kalmaz.
    /// </summary>
    public static async Task<bool> ReturnsWhileDbIsBlockedAsync(GatedDbContextFactory gate, Func<Task> call)
    {
        var ct = TestContext.Current.CancellationToken;
        Task? inner = null;
        var syncPart = Task.Run(() => { inner = call(); }, ct);
        var returnedPromptly = await Task.WhenAny(syncPart, Task.Delay(TimeSpan.FromSeconds(2), ct)) == syncPart;
        gate.OpenGate();
        await syncPart;
        await inner!;
        return returnedPromptly;
    }
}
