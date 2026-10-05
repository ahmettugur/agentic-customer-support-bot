// Sesli görüşme ekranının durumu: JS olayları → kürenin hâli, altyazı, çipler, süre.

using CustomerSupportBot.Web.Models;

namespace CustomerSupportBot.Web.Tests;

public class VoiceCallModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 4, 10, 0, 0, TimeSpan.Zero);

    private static VoiceCallModel Opened()
    {
        var m = new VoiceCallModel();
        m.Apply("open", null, null, T0);
        return m;
    }

    [Fact]
    public void Open_StartsConnecting_AndResetsThePreviousCall()
    {
        var m = Opened();
        m.Apply("state", "listening", null, T0);
        m.Apply("user_delta", "i1", "merhaba", T0);
        m.Apply("muted", "true", null, T0);
        m.Apply("error", "⚠ koptu", null, T0);

        m.Apply("open", null, null, T0);

        m.Open.Should().BeTrue();
        m.Orb.Should().Be(VoiceOrbState.Connecting);
        m.StateLabel.Should().Be("Bağlanıyor…");
        m.UserCaption.Should().BeNull();
        m.Muted.Should().BeFalse();
        m.Error.Should().BeNull();
    }

    [Fact]
    public void ListeningThinkingSpeaking_FollowTheConversation()
    {
        var m = Opened();
        m.Apply("state", "listening", null, T0);
        m.Orb.Should().Be(VoiceOrbState.Listening);

        m.Apply("speech_stopped", null, null, T0);
        m.Orb.Should().Be(VoiceOrbState.Thinking);
        m.StateLabel.Should().Be("Düşünüyor");

        m.Apply("state", "speaking", null, T0);
        m.Orb.Should().Be(VoiceOrbState.Speaking);

        m.Apply("state", "listening", null, T0);
        m.Orb.Should().Be(VoiceOrbState.Listening, "konuşma düşünme hâlini bitirir");
    }

    [Fact]
    public void Muted_IsShownWhileListening_ButSpeakingStillShowsTheAssistant()
    {
        var m = Opened();
        m.Apply("state", "listening", null, T0);
        m.Apply("muted", "true", null, T0);
        m.Orb.Should().Be(VoiceOrbState.Muted);
        m.StateLabel.Should().Be("Mikrofon kapalı");

        m.Apply("state", "speaking", null, T0);
        m.Orb.Should().Be(VoiceOrbState.Speaking);
        m.Muted.Should().BeTrue();

        m.Apply("muted", "false", null, T0);
        m.Apply("state", "listening", null, T0);
        m.Orb.Should().Be(VoiceOrbState.Listening);
    }

    [Fact]
    public void Handoff_AndError_TakePriority()
    {
        var m = Opened();
        m.Apply("state", "speaking", null, T0);
        m.Apply("tool_call", "human_handoff_tool", "Temsilci talebi oluşturuluyor", T0);
        m.Orb.Should().Be(VoiceOrbState.Handoff);
        m.StateLabel.Should().Be("Temsilciye aktarılıyor");

        m.Apply("error", "⚠ Bağlantı koptu", null, T0);
        m.Orb.Should().Be(VoiceOrbState.Error);
        m.Error.Should().Be("⚠ Bağlantı koptu");
    }

    [Fact]
    public void Captions_ShowOnlyTheLatestTurn()
    {
        var m = Opened();
        m.Apply("user_delta", "i1", "Siparişim ", T0);
        m.Apply("user_delta", "i1", "nerede", T0);
        m.UserCaption.Should().Be("Siparişim nerede");
        m.UserCaptionLive.Should().BeTrue();

        m.Apply("assistant_delta", "Hangi ", null, T0);
        m.Apply("assistant_delta", "sipariş?", null, T0);
        m.Apply("response_done", null, null, T0);
        m.Apply("user_final", "i1", "Siparişim nerede?", T0);
        m.UserCaption.Should().Be("Siparişim nerede?");
        m.UserCaptionLive.Should().BeFalse();
        m.AssistantCaption.Should().Be("Hangi sipariş?");

        m.Apply("user_delta", "i2", "10248", T0);
        m.UserCaption.Should().Be("10248");
        m.AssistantCaption.Should().BeEmpty("yeni tur önceki yanıtı ekrandan kaldırır");
    }

    [Fact]
    public void LateFinalOfAnOlderTurn_DoesNotOverwriteTheCurrentCaption()
    {
        var m = Opened();
        m.Apply("user_delta", "i1", "birinci", T0);
        m.Apply("user_delta", "i2", "ikinci", T0);

        m.Apply("user_final", "i1", "birinci cümle", T0);

        m.UserCaption.Should().Be("ikinci");
    }

    [Fact]
    public void WithoutLiveDeltas_ANewResponseReplacesTheOldAssistantCaption()
    {
        var m = Opened();
        m.Apply("assistant_delta", "İlk yanıt.", null, T0);
        m.Apply("response_done", null, null, T0);
        m.Apply("user_final", null, "ikinci soru", T0);

        m.Apply("assistant_delta", "İkinci yanıt.", null, T0);

        m.AssistantCaption.Should().Be("İkinci yanıt.");
        m.UserCaption.Should().Be("ikinci soru");
    }

    [Fact]
    public void Chips_ShowToolsApprovalsAndHandoff()
    {
        var m = Opened();
        m.Apply("state", "listening", null, T0);
        m.Apply("tool_call", "order_status_tool", "Sipariş durumu sorgulanıyor", T0);
        m.Chip.Should().Be("🔎 Sipariş durumu sorgulanıyor");
        m.Orb.Should().Be(VoiceOrbState.Thinking);
        m.Apply("tool_result", "order_status_tool", null, T0);
        m.Chip.Should().BeNull();

        m.Apply("tool_call", "return_request_tool", "İade talebi onaya gönderiliyor", T0);
        m.Chip.Should().Be("⏳ İade talebi onaya gönderiliyor");
        m.Apply("tool_result", "return_request_tool", null, T0);
        m.Chip.Should().Be("⏳ Talebiniz onaya gönderildi");

        m.Apply("tool_call", "human_handoff_tool", "Temsilci talebi oluşturuluyor", T0);
        m.Apply("tool_result", "human_handoff_tool", null, T0);
        m.Chip.Should().Be("👤 Temsilci talebi oluşturuldu");
    }

    [Fact]
    public void Elapsed_CountsFromConnection()
    {
        var m = Opened();
        m.Elapsed(T0.AddSeconds(30)).Should().Be("00:00");

        m.Apply("connected", null, null, T0);

        m.Elapsed(T0.AddSeconds(84)).Should().Be("01:24");
        m.Elapsed(T0.AddMinutes(75)).Should().Be("75:00");
    }

    [Fact]
    public void EndedAndClose()
    {
        var m = Opened();
        m.Apply("ended", "Görüşme sonlandırıldı.", null, T0);
        m.EndedMessage.Should().Be("Görüşme sonlandırıldı.");

        m.Apply("close", null, null, T0);
        m.Open.Should().BeFalse();
    }

    [Fact]
    public void OrbCssClass_IsTheLowercaseStateName()
    {
        var m = Opened();
        m.OrbCssClass.Should().Be("connecting");

        m.Apply("tool_call", "human_handoff_tool", "x", T0);

        m.OrbCssClass.Should().Be("handoff");
    }

    [Fact]
    public void UnknownEvent_IsIgnored()
    {
        var m = Opened();
        var act = () => m.Apply("something_new", "x", "y", T0);
        act.Should().NotThrow();
        m.Orb.Should().Be(VoiceOrbState.Connecting);
    }

    [Theory]
    [InlineData("error")]
    [InlineData("idle")]
    public void ClientErrorOrIdleState_DoesNotPretendToBeListening(string clientState)
    {
        var m = Opened();
        m.Apply("state", "speaking", null, T0);

        m.Apply("state", clientState, null, T0);

        m.Orb.Should().Be(VoiceOrbState.Speaking,
            "istemcinin error/idle durumu bir dinleme durumu değildir; hata ayrıca 'error' olayıyla gelir");
    }

    [Fact]
    public void Notice_ShowsAsChip_WithoutEndingTheCall()
    {
        var m = Opened();
        m.Apply("state", "listening", null, T0);

        m.Apply("notice", "Mesaj işlenemedi.", null, T0);

        m.Chip.Should().Be("⚠ Mesaj işlenemedi.");
        m.Error.Should().BeNull();
        m.Orb.Should().Be(VoiceOrbState.Listening);
    }

    // ─── Teknik borç: aktarım durumu takılı kalmamalı ───────────────────────

    [Fact]
    public void Handoff_EndsOnceTheRequestIsCreatedAndAnnounced()
    {
        var m = Opened();
        m.Apply("state", "listening", null, T0);
        m.Apply("tool_call", "human_handoff_tool", "Temsilci talebi oluşturuluyor", T0);
        m.Apply("tool_result", "human_handoff_tool", null, T0);
        m.Orb.Should().Be(VoiceOrbState.Handoff, "model aktarımı duyururken");

        m.Apply("response_done", null, null, T0);

        m.Orb.Should().Be(VoiceOrbState.Listening, "talep oluştu ve duyuruldu; görüşme sürüyor");
        m.Chip.Should().Be("👤 Temsilci talebi oluşturuldu");
    }

    [Fact]
    public void Handoff_EndsWhenTheCustomerSpeaksAgain()
    {
        var m = Opened();
        m.Apply("state", "listening", null, T0);
        m.Apply("tool_call", "human_handoff_tool", "Temsilci talebi oluşturuluyor", T0);
        m.Apply("tool_result", "human_handoff_tool", null, T0);

        m.Apply("speech_started", null, null, T0);

        m.Orb.Should().Be(VoiceOrbState.Listening);
    }

    [Fact]
    public void Handoff_StaysWhileTheRequestIsStillRunning()
    {
        var m = Opened();
        m.Apply("state", "listening", null, T0);
        m.Apply("tool_call", "human_handoff_tool", "Temsilci talebi oluşturuluyor", T0);

        m.Apply("response_done", null, null, T0);   // araç sonucu henüz gelmedi

        m.Orb.Should().Be(VoiceOrbState.Handoff);
    }

    // ─── Teknik borç: canlı olmayan transkripsiyon modellerinde altyazı ─────

    [Fact]
    public void NonLiveTranscription_TheCaptionFollowsTheCurrentTurn_NotThePreviousOne()
    {
        // gpt-4o-transcribe vb.: canlı parça yok; son transkript yanıttan SONRA gelir.
        var m = Opened();
        m.Apply("speech_stopped", "i1", null, T0);
        m.Apply("assistant_delta", "Hangi sipariş?", null, T0);
        m.Apply("response_done", null, null, T0);
        m.Apply("user_final", "i1", "Siparişim nerede?", T0);
        m.UserCaption.Should().Be("Siparişim nerede?");

        m.Apply("speech_stopped", "i2", null, T0);

        m.UserCaption.Should().BeNullOrEmpty("önceki turun cümlesi yeni turun altyazısında kalmamalı");
        m.AssistantCaption.Should().BeEmpty();

        m.Apply("assistant_delta", "1044 kargoda.", null, T0);
        m.Apply("user_final", "i1", "Siparişim nerede?", T0);   // eski turun geç tekrarı
        m.UserCaption.Should().BeNullOrEmpty();
        m.Apply("user_final", "i2", "1044", T0);
        m.UserCaption.Should().Be("1044");
        m.AssistantCaption.Should().Be("1044 kargoda.");
    }

    [Fact]
    public void LiveTranscription_SpeechStoppedOfTheSameItem_KeepsTheLiveCaption()
    {
        var m = Opened();
        m.Apply("user_delta", "i1", "Siparişim nerede", T0);

        m.Apply("speech_stopped", "i1", null, T0);

        m.UserCaption.Should().Be("Siparişim nerede");
    }
}
