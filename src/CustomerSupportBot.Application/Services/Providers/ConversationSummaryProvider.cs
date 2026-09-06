// Application/Services/Providers/ConversationSummaryProvider.cs
// Uzun konuşma geçmişini LLM ile özetleyerek token tasarrufu sağlar.
// Özet ARTIMLI üretilir: her turda tüm eski geçmiş yeniden özetlenmez, yalnızca son
// özetten bu yana DÜŞEN (yeni sınırın dışında kalan) mesajlar mevcut özetle katlanır
// (fold). Bkz. FoldAsync.

using CustomerSupportBot.Application.Ports.Outbound;
using System.Text;

using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Locking;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Providers;

/// <summary>
/// Konuşma geçmişi belirli bir eşiği aştığında, eski mesajları
/// LLM kullanarak özetler ve özet + son mesajları bağlam olarak sunar.
/// </summary>
public class ConversationSummaryProvider : IContextProvider
{
    private readonly IGeneralChatClient _chatClient;
    private readonly ISessionManager _sessionRepository;
    private readonly IAppDistributedLock _distributedLock;
    private readonly ILogger<ConversationSummaryProvider> _logger;

    private const int SummaryThreshold = 8;
    private const int RecentMessageCount = 4;

    /// <summary>
    /// Provider adı — <c>WorkflowMessageBuilder</c> geçmişi kırpıp kırpmayacağına karar
    /// verirken bu adla "özet bu tur gerçekten prompt'a girdi mi?" diye sorar, o yüzden
    /// serbest metin değil sabit.
    /// </summary>
    public const string ProviderName = "ConversationSummary";

    public string Name => ProviderName;
    public int Order => 5;

    private readonly IContextSanitizer _sanitizer;

    public ConversationSummaryProvider(
        IGeneralChatClient chatClient,
        ISessionManager sessionRepository,
        IAppDistributedLock distributedLock,
        IContextSanitizer sanitizer,
        ILogger<ConversationSummaryProvider> logger)
    {
        _chatClient = chatClient;
        _sessionRepository = sessionRepository;
        _distributedLock = distributedLock;
        _sanitizer = sanitizer;
        _logger = logger;
    }

    public async Task<string?> GetContextAsync(AgentSession session, string currentQuery, CancellationToken ct = default)
    {
        var history = await _sessionRepository.GetHistoryAsync(session.SessionId, ct);
        if (history.Count < SummaryThreshold)
            return null;

        // Özetlenecek yeni sınır: son RecentMessageCount mesaj hep ham gönderilir, öncesi
        // özete taşınabilir aday havuzudur.
        var boundary = history.Count - RecentMessageCount;
        if (boundary <= 0)
            return null;

        // Hızlı yol (kilitsiz): mevcut özet zaten bu sınırı kapsıyorsa iş yok.
        if (!string.IsNullOrWhiteSpace(session.State.ConversationSummary) && boundary <= session.State.SummarizedMessageCount)
            return FormatSummaryContext(session.State.ConversationSummary);

        try
        {
            // Aynı session için eşzamanlı iki istek (çift-submit, çoklu sekme) burada
            // yarışabilir — ikisi de aynı "eski özet"i okuyup farklı deltaları katlarsa
            // biri sessizce kaybolur (bkz. PostgresSessionManager.MutateStateAsync'teki
            // aynı desen). Kilit bu yarışı serileştirir.
            await using var handle = await _distributedLock
                .AcquireAsync($"session:{session.SessionId}", ct: ct)
                .ConfigureAwait(false);

            // Kilit beklerken aynı süreçte başka bir çağrı zaten katlamış olabilir
            // (PostgresSessionManager aynı sessionId için hep aynı AgentSession referansını
            // döndürür) — tekrar kontrol ederek gereksiz LLM çağrısından kaçınılır.
            if (!string.IsNullOrWhiteSpace(session.State.ConversationSummary) && boundary <= session.State.SummarizedMessageCount)
                return FormatSummaryContext(session.State.ConversationSummary);

            var priorCount = string.IsNullOrWhiteSpace(session.State.ConversationSummary)
                ? 0 : Math.Clamp(session.State.SummarizedMessageCount, 0, boundary);
            var newMessages = history.Skip(priorCount).Take(boundary - priorCount).ToList();
            if (newMessages.Count == 0)
            {
                return session.State.ConversationSummary is null
                    ? null
                    : FormatSummaryContext(session.State.ConversationSummary);
            }

            var summary = await FoldAsync(session.State.ConversationSummary, newMessages, ct);
            if (string.IsNullOrWhiteSpace(summary))
            {
                _logger.LogWarning("Conversation summary was empty; preserving history boundary.");
                return null;
            }
            session.State.ConversationSummary = summary;
            // Prompt kurulurken geçmişin ilk bu kadar mesajı atlanacak — özet onların yerine
            // geçer. Bu sayı yazılmazsa özet tasarruf değil ek yük olur (bkz. SessionState).
            session.State.SummarizedMessageCount = boundary;
            await _sessionRepository.UpdateAsync(session, ct);

            _logger.LogInformation(
                "Konuşma özeti güncellendi: {NewCount} yeni mesaj katlandı → {SummaryLength} karakter",
                newMessages.Count, summary.Length);

            return FormatSummaryContext(summary);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Konuşma özetleme başarısız oldu");
            return null;
        }
    }

    /// <summary>
    /// Yeni mesajları özete katlar. <paramref name="existingSummary"/> <c>null</c>ise ilk
    /// özetleme (sıfırdan), doluysa artımlı katlama (<c>yeni_özet = f(mevcut_özet, yeni
    /// mesajlar)</c>) yapılır — ikisi de tek bir LLM çağrısı, önceki mesajlar tekrar
    /// gönderilmez.
    /// </summary>
    private async Task<string> FoldAsync(string? existingSummary, List<ConversationMessage> newMessages, CancellationToken ct)
    {
        var conversationText = new StringBuilder();
        foreach (var msg in newMessages)
        {
            var role = msg.Role == ConversationRoles.User ? "Müşteri" : "Asistan";
            conversationText.AppendLine($"{role}: {msg.Text}");
        }

        var systemPrompt = existingSummary is null
            ? "Aşağıdaki müşteri destek konuşmasını kısa ve öz bir şekilde özetle. " +
              "Önemli bilgileri koru: müşteri kimliği, sipariş numaraları, yapılan işlemler, " +
              "çözülmemiş sorunlar. Türkçe yaz. Maksimum 150 kelime."
            : "Sana mevcut bir konuşma özeti ve konuşmanın DEVAMINDAN yeni mesajlar veriliyor. " +
              "Mevcut özeti, yeni mesajlardaki bilgileri de katarak GÜNCELLE — yeniden baştan " +
              "yazma, üzerine inşa et. Önemli bilgileri koru: müşteri kimliği, sipariş " +
              "numaraları, yapılan işlemler, çözülmemiş sorunlar. Eski bilgi yeni mesajlarla " +
              "çelişiyorsa (ör. bir sorun çözüldü) yeni durumu yansıt. Türkçe yaz. Maksimum " +
              "150 kelime.";

        var userContent = existingSummary is null
            ? conversationText.ToString()
            : $"Mevcut özet:\n{existingSummary}\n\nYeni mesajlar:\n{conversationText}";

        var prompt = new List<ConversationMessage>
        {
            new(ConversationRoles.System, systemPrompt),
            new(ConversationRoles.User, userContent)
        };

        return await _chatClient.CompleteAsync(prompt, ct);
    }

    /// <summary>
    /// Özeti <c>&lt;retrieved_data&gt;</c> çitiyle sarar — semantik bellek içerikleriyle AYNI
    /// muamele.
    ///
    /// <para>
    /// Özet, kullanıcının kendi yazdıklarından üretilir ve tüm bağlam gibi <b>System</b>
    /// rolüyle prompt'a girer. Çitsiz hâlde bu, kullanıcı metnine system yetkisi vermek
    /// demekti: konuşmasına talimat benzeri cümleler serpiştiren biri, bunların özete
    /// taşınmasını sağlayıp dolaylı prompt injection deneyebilirdi. Ajan promptlarındaki
    /// "retrieved veri kuralı" bu etiketi zaten tanır ve içeriği talimat saymaz.
    /// </para>
    ///
    /// <para>
    /// <c>WrapRetrieved</c> ayrıca kapanış etiketini nötralize eder, yani özet metni çiti
    /// kırıp dışarı çıkamaz.
    /// </para>
    /// </summary>
    private string FormatSummaryContext(string summary)
        => "[Konuşma Özeti]\n" + _sanitizer.WrapRetrieved(summary, "conversation_summary");
}
