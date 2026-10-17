// Ports/Inbound/IWorkflowRecoveryPort.cs
// Çökme/yeniden başlatma yüzünden yarım kalan workflow turlarını bulup bitiren taramanın sözleşmesi.

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <param name="Resumed">Kaldığı yerden bitirilen ve yanıtı konuşma geçmişine yazılan turlar.</param>
/// <param name="Discarded">Devam ettirilmeden silinenler (çok eski, kullanıcı ilerlemiş, alt görev, bağlamı yok).</param>
/// <param name="Failed">Devam denenip hata veren turlar (tekrar denenmez).</param>
/// <param name="Skipped">Başka bir pod'un kilidi altında olanlar; dokunulmadı.</param>
public sealed record WorkflowRecoveryResult(int Resumed, int Discarded, int Failed, int Skipped);

public interface IWorkflowRecoveryPort
{
    Task<WorkflowRecoveryResult> RecoverStaleTurnsAsync(CancellationToken ct = default);
}
