// Ports/Outbound/IShutdownSignal.cs
// Uygulamanın kapanmakta olup olmadığı. Host kapanırken (deploy) iptal edilen workflow turu ile istemcinin vazgeçtiği
// tur aynı iptal sinyaliyle gelir; ikisini ayırt etmek için gerekir.

namespace CustomerSupportBot.Application.Ports.Outbound;

public interface IShutdownSignal
{
    /// <summary>Host durdurma sürecine girdi (SIGTERM / rolling update).</summary>
    bool IsStopping { get; }
}
