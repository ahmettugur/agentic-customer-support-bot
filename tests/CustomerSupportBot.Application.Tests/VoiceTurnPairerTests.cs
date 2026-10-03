// Native sesli modda tur yarılarının (transkript ↔ asistan yanıtı) eşleştirilmesi.

using CustomerSupportBot.Application.Services.Realtime;

namespace CustomerSupportBot.Application.Tests;

public class VoiceTurnPairerTests
{
    [Fact]
    public void TranscriptBeforeResponse_PairsImmediately()
    {
        var p = new VoiceTurnPairer();
        p.AudioCommitted("U1");
        p.ResponseCreated();
        p.TranscriptArrived("U1", "soru").Should().BeEmpty();

        p.ResponseCompleted("cevap", legacyUserSide: null)
            .Should().Equal(new VoiceTurn("soru", "cevap"));
    }

    [Fact]
    public void TranscriptAfterResponse_WaitsAndThenPairs()
    {
        var p = new VoiceTurnPairer();
        p.AudioCommitted("U1");
        p.ResponseCreated();

        p.ResponseCompleted("cevap", legacyUserSide: "ÖNCEKİ SORU").Should().BeEmpty(
            "transkript henüz gelmedi — son transkriptle (bir önceki tur) eşleştirilmemeli");
        p.TranscriptArrived("U1", "soru").Should().Equal(new VoiceTurn("soru", "cevap"));
    }

    [Fact]
    public void TurnsAreReleasedInOrder_EvenWhenALaterTranscriptArrivesFirst()
    {
        var p = new VoiceTurnPairer();
        p.AudioCommitted("U1");
        p.ResponseCreated();
        p.ResponseCompleted("cevap1", null).Should().BeEmpty();

        p.AudioCommitted("U2");
        p.ResponseCreated();
        p.TranscriptArrived("U2", "soru2").Should().BeEmpty();
        p.ResponseCompleted("cevap2", null).Should().BeEmpty("U1 hâlâ transkript bekliyor — sıra korunmalı");

        p.TranscriptArrived("U1", "soru1").Should().Equal(
            new VoiceTurn("soru1", "cevap1"), new VoiceTurn("soru2", "cevap2"));
    }

    [Fact]
    public void ToolFollowUpResponse_BelongsToTheSameUserTurn()
    {
        var p = new VoiceTurnPairer();
        p.AudioCommitted("U1");
        p.ResponseCreated().Should().BeFalse();
        p.ToolCallsDispatched(followUpExpected: true);

        p.AudioCommitted("U2");   // takip yanıtı başlamadan kullanıcı yeniden konuştu
        p.ResponseCreated().Should().BeTrue();
        p.TranscriptArrived("U1", "soru1");

        p.ResponseCompleted("tool sonrası cevap", null)
            .Should().Equal(new VoiceTurn("soru1", "tool sonrası cevap"));
    }

    [Fact]
    public void FailedTranscription_ReleasesTheTurnWithPlaceholder()
    {
        var p = new VoiceTurnPairer();
        p.AudioCommitted("U1");
        p.ResponseCreated();
        p.ResponseCompleted("cevap", null);

        p.TranscriptFailed("U1").Should().Equal(new VoiceTurn(VoiceTurnPairer.Placeholder, "cevap"));
    }

    [Fact]
    public void RejectedTranscript_DropsTheTurn_AndUnblocksTheQueue()
    {
        var p = new VoiceTurnPairer();
        p.AudioCommitted("U1");
        p.ResponseCreated();
        p.ResponseCompleted("cevap1", null);
        p.AudioCommitted("U2");
        p.ResponseCreated();
        p.TranscriptArrived("U2", "soru2");
        p.ResponseCompleted("cevap2", null).Should().BeEmpty();

        p.TranscriptRejected("U1").Should().Equal(new VoiceTurn("soru2", "cevap2"));
    }

    [Fact]
    public void LostTranscript_DoesNotBlockHistoryForever()
    {
        var p = new VoiceTurnPairer();
        p.AudioCommitted("U1");
        p.ResponseCreated();
        p.ResponseCompleted("cevap1", null);   // U1'in transkripti hiç gelmeyecek

        var released = new List<VoiceTurn>();
        for (var i = 2; i <= VoiceTurnPairer.MaxPendingTurns + 1; i++)
        {
            p.AudioCommitted($"U{i}");
            p.ResponseCreated();
            p.TranscriptArrived($"U{i}", $"soru{i}");
            released.AddRange(p.ResponseCompleted($"cevap{i}", null));
        }

        released.Should().NotBeEmpty();
        released[0].Should().Be(new VoiceTurn(VoiceTurnPairer.Placeholder, "cevap1"));
        released.Select(t => t.BotText).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void WithoutItemIds_FallsBackToLastTranscript()
    {
        var p = new VoiceTurnPairer();
        p.ResponseCreated();

        p.ResponseCompleted("cevap", legacyUserSide: "son transkript")
            .Should().Equal(new VoiceTurn("son transkript", "cevap"));
    }

    [Fact]
    public void DrainAll_ReleasesWaitingTurnsWithPlaceholder()
    {
        var p = new VoiceTurnPairer();
        p.AudioCommitted("U1");
        p.ResponseCreated();
        p.ResponseCompleted("cevap", null);

        p.DrainAll().Should().Equal(new VoiceTurn(VoiceTurnPairer.Placeholder, "cevap"));
        p.DrainAll().Should().BeEmpty();
    }
}
