// Sesli oturumda transkripsiyon ayarının model ailesine göre biçimi.
//
// Model appsettings'ten seçilir; aileler arasındaki fark gönderilen alanlardır. Yeni nesil
// modeller (gpt-transcribe, gpt-live-transcribe) tek değerli `language` yerine `languages`
// listesi ister ve ikisinin birlikte gönderilmesini reddeder — yanlış biçim oturum kurulumunu
// bozar.

using System.Text.Json;
using CustomerSupportBot.Adapters.AI.Realtime;

namespace CustomerSupportBot.Adapters.AI.Tests;

public class RealtimeTranscriptionConfigTests
{
    private static RealtimeOptions Options(
        string model, List<string>? keywords = null, string? delay = null) => new()
    {
        TranscriptionModel = model,
        TranscriptionLanguage = "tr",
        TranscriptionPrompt = "Müşteri destek görüşmesi.",
        TranscriptionKeywords = keywords ?? [],
        TranscriptionDelay = delay
    };

    private static JsonElement Json(Dictionary<string, object?> config) =>
        JsonSerializer.SerializeToElement(config);

    [Theory]
    [InlineData("gpt-4o-transcribe", "Legacy")]
    [InlineData("gpt-4o-mini-transcribe-2025-12-15", "Legacy")]
    [InlineData("whisper-1", "Legacy")]
    [InlineData("gpt-transcribe", "Transcribe")]
    [InlineData("gpt-transcribe-2026-07-29", "Transcribe")]
    [InlineData("GPT-Transcribe", "Transcribe")]
    [InlineData("gpt-live-transcribe", "LiveTranscribe")]
    [InlineData(" gpt-live-transcribe-2026-07-29 ", "LiveTranscribe")]
    public void ModelFamily_IsInferredFromTheName(string model, string expected) =>
        RealtimeTranscriptionConfig.Classify(model).ToString().Should().Be(expected);

    [Fact]
    public void Legacy_SendsSingleLanguage_AndIgnoresNewOnlySettings()
    {
        var (config, warnings) = RealtimeTranscriptionConfig.Build(
            Options("gpt-4o-transcribe", keywords: ["iade"], delay: "high"));
        var json = Json(config);

        json.GetProperty("model").GetString().Should().Be("gpt-4o-transcribe");
        json.GetProperty("language").GetString().Should().Be("tr");
        json.GetProperty("prompt").GetString().Should().Be("Müşteri destek görüşmesi.");
        json.TryGetProperty("languages", out _).Should().BeFalse();
        json.TryGetProperty("keywords", out _).Should().BeFalse();
        json.TryGetProperty("delay", out _).Should().BeFalse();
        warnings.Should().HaveCount(2, "uygulanamayan ayarlar sessizce kaybolmamalı");
    }

    [Fact]
    public void Transcribe_SendsLanguageList_AndKeywords_ButNotDelay()
    {
        var (config, warnings) = RealtimeTranscriptionConfig.Build(
            Options("gpt-transcribe", keywords: ["iade", " kargo ", "IADE", ""], delay: "medium"));
        var json = Json(config);

        json.TryGetProperty("language", out _).Should().BeFalse("language ile languages birlikte gönderilmemeli");
        json.GetProperty("languages").EnumerateArray().Select(e => e.GetString()).Should().Equal("tr");
        json.GetProperty("keywords").EnumerateArray().Select(e => e.GetString()).Should().Equal("iade", "kargo");
        json.GetProperty("prompt").GetString().Should().NotBeNullOrEmpty();
        json.TryGetProperty("delay", out _).Should().BeFalse();
        warnings.Should().ContainSingle().Which.Should().Contain("TranscriptionDelay");
    }

    [Fact]
    public void LiveTranscribe_SendsLanguageList_Keywords_AndDelay()
    {
        var (config, warnings) = RealtimeTranscriptionConfig.Build(
            Options("gpt-live-transcribe", keywords: ["şikayet"], delay: "High"));
        var json = Json(config);

        json.GetProperty("languages").EnumerateArray().Select(e => e.GetString()).Should().Equal("tr");
        json.GetProperty("keywords").EnumerateArray().Select(e => e.GetString()).Should().Equal("şikayet");
        json.GetProperty("delay").GetString().Should().Be("high");
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void LiveTranscribe_InvalidDelay_IsOmittedWithAWarning()
    {
        var (config, warnings) = RealtimeTranscriptionConfig.Build(Options("gpt-live-transcribe", delay: "fast"));

        config.Should().NotContainKey("delay");
        warnings.Should().ContainSingle().Which.Should().Contain("geçersiz");
    }

    [Fact]
    public void EmptyOptionalSettings_AreNotSent()
    {
        var options = Options("gpt-transcribe");
        options.TranscriptionLanguage = null;
        options.TranscriptionPrompt = "";

        var (config, warnings) = RealtimeTranscriptionConfig.Build(options);

        config.Keys.Should().Equal("model");
        warnings.Should().BeEmpty();
    }
}
