// Application/Services/Providers/CustomerIdentityHintBuilder.cs
// Login'li müşterinin adı + bugünün tarihinden oluşan system mesajını üretir.
//
// Neden ortak bir servis: aynı cümle İKİ kanalda gerekiyor — yazılı workflow
// (WorkflowMessageBuilder, mesaj listesinin başına system mesajı olarak) ve sesli native mod
// (RealtimeNativeService → ConfigureNativeSessionAsync instructions'ına eklenerek). Metin iki
// yerde kopyalanırsa biri güncellenip diğeri kalır; sesli kanalın kimliği/tarihi hiç görmemesi
// tam olarak böyle bir sapmayla oluşmuştu.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Providers;

public sealed class CustomerIdentityHintBuilder
{
    private readonly ICustomerRepository _customers;
    private readonly TimeProvider _clock;

    public CustomerIdentityHintBuilder(ICustomerRepository customers, TimeProvider? clock = null)
    {
        _customers = customers;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Kimlik/tarih system mesajı. İsim <see cref="SessionState.AuthenticatedCustomerId"/>
    /// (JWT'den) üzerinden çözülür — LLM'in metinden çıkardığı <see cref="SessionState.CustomerId"/>
    /// KULLANILMAZ, aksi halde kullanıcı "ben 1008'im" diyerek ajanı başka birinin adıyla
    /// hitap etmeye ikna edebilirdi. İsim çözülemezse yalnızca tarih döner (asla boş değil —
    /// tarih her zaman faydalı: "yarın", "bu ay" gibi göreli ifadeler bunun üzerinden yorumlanır).
    /// </summary>
    public async Task<string> BuildAsync(AgentSession? session, CancellationToken ct = default)
    {
        var today = _clock.GetLocalNow().ToString(
            "d MMMM yyyy, dddd", new System.Globalization.CultureInfo("tr-TR"));

        var fullName = await ResolveFullNameAsync(session, ct);

        return string.IsNullOrWhiteSpace(fullName)
            ? $"Bugünün tarihi: {today}."
            : $"Şu an sizinle görüşen, kimliği doğrulanmış müşteri: {fullName}. Bugünün tarihi: {today}. " +
              "Uygun olduğunda müşteriye adıyla hitap edebilir, tarihe bağlı taleplerde bu tarihi esas alabilirsiniz.";
    }

    private async Task<string?> ResolveFullNameAsync(AgentSession? session, CancellationToken ct)
    {
        var authenticatedId = session?.State.AuthenticatedCustomerId;
        if (string.IsNullOrWhiteSpace(authenticatedId)) return null;
        if (!long.TryParse(authenticatedId, out var customerId)) return null;

        return await _customers.GetFullNameAsync(customerId, ct);
    }
}
