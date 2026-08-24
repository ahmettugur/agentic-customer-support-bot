// Tests/Agents/ExceptionTranslatorTests.cs
//
// Bulgu 1.3 + 2.1: WorkflowRunner'ın streaming hata yolları eskiden ham exception.Message'ı
// (framework/HTTP iç detayları içerebilir) doğrudan istemciye StreamEvent(Error, ...) olarak
// gönderiyordu. Düzeltme: her yerde ExceptionTranslator.Translate(...).Message kullanılıyor —
// bu, context her zaman verildiğinde context'in kazandığını ve exception türünden bağımsız
// çalıştığını garanti eden bu testlerle doğrulanır.

using CustomerSupportBot.Domain.Exceptions;

namespace CustomerSupportBot.Adapters.Agents.Tests;

public class ExceptionTranslatorTests
{
    [Fact]
    public void Translate_WithContext_ContextAlwaysWinsOverRawMessage()
    {
        var sensitive = new InvalidOperationException(
            "Connection to internal-llm-proxy.corp:8443 failed, api_key=sk-abc123");

        var result = ExceptionTranslator.Translate(sensitive, "Genel bir hata oluştu.");

        result.Message.Should().Be("Genel bir hata oluştu.");
        result.Message.Should().NotContain("internal-llm-proxy");
        result.Message.Should().NotContain("sk-abc123");
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(HttpRequestException))]
    public void Translate_AnyExceptionType_WithContext_ProducesSameSafeMessage(Type exceptionType)
    {
        // Eskiden burada tür bazlı bir switch vardı ama context her zaman kazandığı için hiçbir
        // dal gözlemlenebilir bir fark yaratmıyordu — bu test o davranışın (basitleştirmeden
        // sonra da) korunduğunu kanıtlar: hangi exception türü verilirse verilsin, context
        // doluysa sonuç metni birebir context olmalı.
        var ex = (Exception)Activator.CreateInstance(exceptionType, "detaylı iç mesaj")!;

        var result = ExceptionTranslator.Translate(ex, "Sabit güvenli mesaj.");

        result.Message.Should().Be("Sabit güvenli mesaj.");
    }

    [Fact]
    public void Translate_ReturnsExternalServiceException_WithAgentWorkflowServiceName()
    {
        var result = ExceptionTranslator.Translate(new Exception("x"), "ctx");

        result.Should().BeOfType<ExternalServiceException>();
        ((ExternalServiceException)result).ServiceName.Should().Be("AgentWorkflow");
    }

    [Fact]
    public void Translate_PreservesInnerException_ForTraceDebugging()
    {
        var original = new InvalidOperationException("orijinal detay");

        var result = ExceptionTranslator.Translate(original, "güvenli mesaj");

        result.InnerException.Should().BeSameAs(original);
    }

    [Fact]
    public void Translate_WithoutContext_FallsBackToRawMessage()
    {
        // context verilmezse (repo genelinde bugün hiçbir çağıran böyle yapmıyor, ama sözleşme
        // budur) ham mesaj kullanılabilir olmalı — API sınırında zaten DomainExceptionHandler
        // ExternalServiceException için detay göstermeme kararını ayrıca verir.
        var result = ExceptionTranslator.Translate(new Exception("ham detay"));

        result.Message.Should().Contain("ham detay");
    }
}
