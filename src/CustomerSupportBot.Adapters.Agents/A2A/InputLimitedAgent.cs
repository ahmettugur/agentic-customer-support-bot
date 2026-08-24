// Adapters.Agents/A2A/InputLimitedAgent.cs
// Dış kanaldan gelen girdiye boyut/karmaşıklık sınırı koyan ajan sarmalayıcısı.

using System.Runtime.CompilerServices;
using CustomerSupportBot.Application.Services.A2A;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents.A2A;

/// <summary>
/// Ajanı çalıştırmadan ÖNCE girdinin boyutunu ve parça sayısını denetler; sınır aşılırsa
/// LLM'e hiç gitmeden açıklayıcı bir yanıt döner.
///
/// <para>
/// <b>Neden istek sayısı sınırı yetmiyor:</b> partner başına dakikalık limit "kaç kez"
/// sorusunu sınırlar, "ne kadar" sorusunu değil. Hakkı olan istek sayısını çok büyük
/// metinlerle kullanan bir çağıran, token maliyetini ve çağrı süresini serbestçe büyütebilir.
/// A2A güvenlik rehberi de girdi ve istek karmaşıklığı sınırlarını ayrıca önerir.
/// </para>
///
/// <para>
/// <b>Neden ajan seviyesinde:</b> aynı ajan üç ayrı yoldan çağrılır — JSON-RPC, HTTP+JSON ve
/// streaming. Kontrolü endpoint'e koymak üç yerde tekrar (ve birinde unutma) demekti; burada
/// tek bir yerde, üçü için birden uygulanır. Gövde boyutu sınırı bunun yerine geçmez, onu
/// tamamlar: gövde sınırı ham baytı, bu sınır LLM'e gidecek METNİ ölçer.
/// </para>
///
/// <para>
/// Sınır aşıldığında istisna fırlatılmaz: A2A çağıranı bir protokol hatası değil, anlaşılır
/// bir ajan yanıtı alır — ne olduğunu ve ne yapması gerektiğini söyleyen. İstisna, köprü
/// tarafından jenerik bir sunucu hatasına çevrilirdi ve çağıran nedenini bilemezdi.
/// </para>
/// </summary>
public sealed class InputLimitedAgent(AIAgent inner, A2AOptions options) : DelegatingAIAgent(inner)
{
    private readonly A2AOptions _options = options;

    protected override Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var rejection = Check(messages);
        return rejection is null
            ? base.RunCoreAsync(messages, session, options, cancellationToken)
            : Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, rejection)));
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var rejection = Check(messages);
        if (rejection is not null)
        {
            yield return new AgentResponseUpdate(ChatRole.Assistant, rejection);
            yield break;
        }

        await foreach (var update in base
            .RunCoreStreamingAsync(messages, session, options, cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return update;
        }
    }

    /// <summary>Sınır aşıldıysa kullanıcıya dönecek metni, aşılmadıysa <c>null</c> döner.</summary>
    private string? Check(IEnumerable<ChatMessage> messages)
    {
        var maxChars = Math.Max(1, _options.MaxMessageChars);
        var maxParts = Math.Max(1, _options.MaxParts);

        var parts = 0;
        var chars = 0;

        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                parts++;
                chars += ContentCharWeight(content);
            }

            // Contents boş ama Text dolu olabilen taşıyıcılar için emniyet.
            if (message.Contents.Count == 0)
            {
                parts++;
                chars += message.Text?.Length ?? 0;
            }
        }

        if (parts > maxParts)
            return $"İstek çok fazla parça içeriyor ({parts}). En fazla {maxParts} parça "
                 + "gönderebilirsiniz; lütfen sorunuzu tek bir mesajda toparlayın.";

        if (chars > maxChars)
            return $"İstek çok uzun ({chars} karakter). En fazla {maxChars} karakter "
                 + "gönderebilirsiniz; lütfen sorunuzu kısaltın.";

        return null;
    }

    /// <summary>
    /// Bir içerik parçasının karakter bütçesine katkısını ölçer.
    ///
    /// <para>
    /// 🐞 <b>Eskiden yalnızca <see cref="TextContent"/> sayılıyordu</b> — <see cref="DataContent"/>
    /// (base64 kodlu resim/dosya eki) yalnızca <c>parts</c> sayacına giriyor, <c>chars</c>'a hiç
    /// katkı yapmıyordu. Sonuç: metin bütçesi (<c>MaxMessageChars</c>, varsayılan 4000) tamamen
    /// baypas edilip, çok daha büyük bir base64 yükü LLM'e gidiyordu — parça sayısı sınırı
    /// (<c>MaxParts</c>) tek başına bunu engellemez, çünkü tek bir büyük <c>DataContent</c> tek
    /// bir parçadır. <see cref="DataContent.Uri"/> her zaman geçerli bir data URI string'idir
    /// (ham bayt dizisinden oluşturulmuş olsa bile) — bu yüzden uzunluğu, gerçek yükün metin
    /// eşdeğeri bir yaklaşık ölçüsü olarak kullanılabilir. Kestrel gövde sınırı
    /// (<c>A2AOptions.MaxRequestBytes</c>, varsayılan 64 KB) zaten ayrı bir savunma katmanıdır —
    /// bu ikisi birbirinin YERİNE geçmez, tamamlar (bkz. sınıf XML dokümanı).
    /// </para>
    /// </summary>
    private static int ContentCharWeight(AIContent content) => content switch
    {
        TextContent text => text.Text?.Length ?? 0,
        DataContent data => data.Uri.Length,
        UriContent uri => uri.Uri.ToString().Length,
        _ => 0
    };
}
