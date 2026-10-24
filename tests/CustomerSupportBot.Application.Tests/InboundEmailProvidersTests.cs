// Sağlayıcı yüklerinin (Postmark JSON, SendGrid form) normalize e-postaya çevrilmesi ve gönderici kimlik doğrulaması.
// Kural: "From" başlığı sahte olabilir → yalnızca gönderen alanıyla HİZALI DKIM/SPF geçişi kimlik sayılır.

using System.Text;
using System.Text.Json;
using CustomerSupportBot.Application.Services.Channels;

namespace CustomerSupportBot.Application.Tests;

public class InboundEmailProvidersTests
{
    // ── SenderAuthentication ───────────────────────────────────────────────────

    [Theory]
    [InlineData("ayse@example.com", "mx.x; dkim=pass header.d=example.com header.s=s1", true)]
    [InlineData("ayse@mail.example.com", "mx.x; dkim=pass header.d=example.com", true)]          // alt alan, gevşek hizalama
    [InlineData("ayse@example.com", "mx.x; dkim=pass header.d=attacker.com", false)]             // başka alanın imzası
    [InlineData("ayse@example.com", "mx.x; dkim=fail header.d=example.com", false)]
    [InlineData("ayse@example.com", "mx.x; spf=pass smtp.mailfrom=ayse@example.com", true)]
    [InlineData("ayse@example.com", "mx.x; spf=pass smtp.mailfrom=bounce@attacker.com", false)]
    [InlineData("ayse@example.com", "mx.x; spf=softfail smtp.mailfrom=ayse@example.com", false)]
    [InlineData("ayse@example.com", "", false)]
    [InlineData("ayse@example.com", null, false)]
    [InlineData("not-an-address", "dkim=pass header.d=example.com", false)]
    public void AuthenticationResults_MustBeAlignedWithTheSender(string from, string? header, bool expected) =>
        SenderAuthentication.IsAligned(from, header).Should().Be(expected);

    [Fact]
    public void ALookAlikeDomain_IsNotAligned() =>
        SenderAuthentication.IsAligned("ayse@example.com", "dkim=pass header.d=notexample.com").Should().BeFalse();

    // ── Postmark ───────────────────────────────────────────────────────────────

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private const string PostmarkOk = """
    {
      "FromFull": { "Email": "ayse@example.com", "Name": "Ayşe" },
      "From": "Ayşe <ayse@example.com>",
      "Subject": "Siparişim",
      "TextBody": "Siparişim nerede?",
      "MessageID": "pm-123",
      "Headers": [
        { "Name": "Authentication-Results", "Value": "mx.postmarkapp.com; dkim=pass header.d=example.com" },
        { "Name": "Auto-Submitted", "Value": "no" }
      ],
      "Attachments": [
        { "Name": "kupa.jpg", "ContentType": "image/jpeg", "Content": "AQID" },
        { "Name": "bozuk.png", "ContentType": "image/png", "Content": "###not-base64###" }
      ]
    }
    """;

    [Fact]
    public void Postmark_MapsTheFields()
    {
        var mail = PostmarkInbound.Parse(Json(PostmarkOk))!;

        mail.From.Should().Be("ayse@example.com");
        mail.Subject.Should().Be("Siparişim");
        mail.Text.Should().Be("Siparişim nerede?");
        mail.MessageId.Should().Be("pm-123");
        mail.Authenticated.Should().BeTrue();
        mail.AutoSubmitted.Should().Be("no");
    }

    [Fact]
    public void Postmark_DecodesAttachments_AndSkipsBrokenOnes()
    {
        var mail = PostmarkInbound.Parse(Json(PostmarkOk))!;

        var a = mail.Attachments.Should().ContainSingle().Subject;
        a.ContentType.Should().Be("image/jpeg");
        a.Data.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Postmark_WithoutAlignedAuthentication_IsNotAuthenticated()
    {
        var raw = PostmarkOk.Replace("header.d=example.com", "header.d=attacker.com");

        PostmarkInbound.Parse(Json(raw))!.Authenticated.Should().BeFalse();
    }

    [Fact]
    public void Postmark_FallsBackToTheFromString_WhenFromFullIsMissing()
    {
        var raw = """{ "From": "Ayşe <ayse@example.com>", "TextBody": "x", "Headers": [] }""";

        PostmarkInbound.Parse(Json(raw))!.From.Should().Be("ayse@example.com");
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{ "TextBody": "x" }""")]
    [InlineData("""[1]""")]
    public void Postmark_AnUnusablePayload_IsRejected(string raw) => PostmarkInbound.Parse(Json(raw)).Should().BeNull();

    // ── SendGrid ───────────────────────────────────────────────────────────────

    private static Dictionary<string, string> SendGridFields(string dkim = "{@example.com : pass}", string spf = "none") => new()
    {
        ["from"] = "Ayşe <ayse@example.com>",
        ["subject"] = "Siparişim",
        ["text"] = "Siparişim nerede?",
        ["dkim"] = dkim,
        ["SPF"] = spf,
        ["envelope"] = """{"to":["destek@x.com"],"from":"ayse@example.com"}""",
        ["headers"] = "Received: x\nMessage-ID: <sg-1@example.com>\nAuto-Submitted: auto-replied\nSubject: Siparişim\n"
    };

    [Fact]
    public void SendGrid_MapsTheFields()
    {
        var mail = SendGridInbound.Parse(SendGridFields(), [])!;

        mail.From.Should().Be("ayse@example.com");
        mail.Text.Should().Be("Siparişim nerede?");
        mail.MessageId.Should().Be("<sg-1@example.com>");
        mail.AutoSubmitted.Should().Be("auto-replied");
        mail.Authenticated.Should().BeTrue();
    }

    [Theory]
    [InlineData("{@attacker.com : pass}", "none", false)]
    [InlineData("{@example.com : fail}", "none", false)]
    [InlineData("{@attacker.com : pass, @example.com : pass}", "none", true)]
    [InlineData("none", "pass", true)]          // SPF geçti ve zarf göndereni hizalı
    [InlineData("none", "fail", false)]
    public void SendGrid_AuthenticationIsAlignedDkimOrAlignedSpf(string dkim, string spf, bool expected) =>
        SendGridInbound.Parse(SendGridFields(dkim, spf), [])!.Authenticated.Should().Be(expected);

    [Fact]
    public void SendGrid_SpfPass_WithAMisalignedEnvelope_IsNotAuthenticated()
    {
        var fields = SendGridFields("none", "pass");
        fields["envelope"] = """{"from":"bounce@attacker.com"}""";

        SendGridInbound.Parse(fields, [])!.Authenticated.Should().BeFalse();
    }

    [Fact]
    public void SendGrid_PassesTheAttachmentFilesThrough()
    {
        var mail = SendGridInbound.Parse(SendGridFields(), [new InboundAttachment("kupa.jpg", "image/jpeg", [1, 2, 3])])!;

        mail.Attachments.Should().ContainSingle().Which.Data.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void SendGrid_AMissingFrom_IsRejected()
    {
        var f = SendGridFields(); f.Remove("from");

        SendGridInbound.Parse(f, []).Should().BeNull();
    }
}
