// Performans ekranı görüntü yardımcıları: yüzde/süre biçimi, Türkçe etiketler, oran tonları.

using CustomerSupportBot.Web.Helpers;

namespace CustomerSupportBot.Web.Tests;

public class PerformanceViewTests
{
    [Theory]
    [InlineData(0.5, "%50")]
    [InlineData(0.0, "%0")]
    [InlineData(1.0, "%100")]
    [InlineData(0.333, "%33")]
    [InlineData(null, "—")]
    public void Percent(double? rate, string expected) => PerformanceView.Percent(rate).Should().Be(expected);

    [Theory]
    [InlineData(null, "—")]
    [InlineData(45.0, "45 sn")]
    [InlineData(180.0, "3 dk")]
    [InlineData(5400.0, "1 sa 30 dk")]
    [InlineData(7200.0, "2 sa")]
    [InlineData(200000.0, "2 gün 7 sa")]
    public void Duration(double? seconds, string expected) => PerformanceView.Duration(seconds).Should().Be(expected);

    [Theory]
    [InlineData(850.0, "850 ms")]
    [InlineData(2500.0, "2,5 sn")]
    [InlineData(90000.0, "1 dk 30 sn")]
    [InlineData(0.0, "—")]
    public void DurationMs(double ms, string expected) => PerformanceView.DurationMs(ms).Should().Be(expected);

    [Theory]
    [InlineData("PlanningAgent", "Planlama")]
    [InlineData("OrderAgent", "Sipariş")]
    [InlineData("HumanHandoffAgent", "Temsilciye aktarım")]
    [InlineData("YeniAjan", "YeniAjan")]
    public void AgentLabel(string name, string expected) => PerformanceView.AgentLabel(name).Should().Be(expected);

    [Theory]
    [InlineData("order_cancel_tool", "Sipariş iptali")]
    [InlineData("complaint_status_tool", "Şikayet durumu")]
    [InlineData("bilinmeyen_tool", "bilinmeyen_tool")]
    public void ToolLabel(string tool, string expected) => PerformanceView.ToolLabel(tool).Should().Be(expected);

    [Theory]
    [InlineData("escalation_needed", "Temsilciye aktarıldı")]
    [InlineData("awaiting_user_input", "Müşteriden bilgi bekleniyor")]
    [InlineData("unknown", "Bilinmiyor")]
    [InlineData("baska", "baska")]
    public void TerminationLabel(string reason, string expected) => PerformanceView.TerminationLabel(reason).Should().Be(expected);

    [Theory]
    [InlineData(0.9, "good")]
    [InlineData(0.8, "good")]
    [InlineData(0.6, "warn")]
    [InlineData(0.2, "bad")]
    [InlineData(null, "")]
    public void SuccessTone(double? rate, string expected) => PerformanceView.SuccessTone(rate).Should().Be(expected);

    [Theory]
    [InlineData(0, 0.0, "")]
    [InlineData(1, 0.05, "warn")]
    [InlineData(3, 0.5, "bad")]
    public void FailureTone(int failures, double rate, string expected) => PerformanceView.FailureTone(failures, rate).Should().Be(expected);

    [Fact]
    public void Ranges_DefaultIsSevenDays()
    {
        PerformanceView.Ranges.Select(r => r.Days).Should().Equal(1, 7, 30, 90);
        PerformanceView.DefaultRangeDays.Should().Be(7);
    }

    [Theory]
    [InlineData(4.5, "4,5")]
    [InlineData(4.0, "4,0")]
    [InlineData(3.333, "3,3")]
    public void Rating_UsesCommaRegardlessOfCulture(double stars, string expected)
    {
        // Blazor WASM'da Türkçe kültür verisi yüklü olmayabilir — biçim kültürden bağımsız olmalı.
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        try { PerformanceView.Rating(stars).Should().Be(expected); }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }
}
