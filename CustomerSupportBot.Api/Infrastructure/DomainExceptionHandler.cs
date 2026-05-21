// Api/Infrastructure/DomainExceptionHandler.cs
// INBOUND BOUNDARY — domain exception hiyerarşisini HTTP status + ProblemDetails'e çevirir.
// Driven tarafta adapter'lardaki ExceptionTranslator infra hatalarını domain exception'a çevirir;
// bu handler döngünün diğer ucunu kapatır: domain exception → HTTP hata dili.

using CustomerSupportBot.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportBot.Api.Infrastructure;

/// <summary>
/// DomainException alt tiplerini uygun HTTP status koduna ve ProblemDetails gövdesine map eder.
/// Domain-dışı exception'lar için false döner — varsayılan 500 handler devreye girer.
/// </summary>
internal sealed class DomainExceptionHandler : IExceptionHandler
{
    private const string GenericServerDetail =
        "Servis şu anda isteği işleyemiyor; lütfen daha sonra tekrar deneyin.";

    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<DomainExceptionHandler> _logger;

    public DomainExceptionHandler(
        IProblemDetailsService problemDetails,
        ILogger<DomainExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domain)
            return false;

        // Yanıt başlamışsa (ör. SSE/WebSocket stream ortası) status değiştirilemez.
        if (httpContext.Response.HasStarted)
            return false;

        var (status, level, exposeDetail) = Map(domain);

        _logger.Log(level, exception,
            "Domain exception {Code} → HTTP {Status} ({Path})",
            domain.Code, status, httpContext.Request.Path);

        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title  = domain.Code,
            Detail = exposeDetail ? domain.Message : GenericServerDetail
        };
        problem.Extensions["code"] = domain.Code;

        switch (domain)
        {
            case EntityNotFoundException nf:
                problem.Extensions["entityType"] = nf.EntityType;
                problem.Extensions["entityId"]   = nf.EntityId;
                break;
            case ExternalServiceException svc:
                problem.Extensions["service"] = svc.ServiceName;
                break;
        }

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext    = httpContext,
            Exception      = exception,
            ProblemDetails = problem
        });
    }

    /// <summary>
    /// 4xx hatalarda mesaj istemciye gösterilir; 5xx'te iç bağlam (infra context) sızdırılmaz.
    /// </summary>
    private static (int Status, LogLevel Level, bool ExposeDetail) Map(DomainException ex) => ex switch
    {
        EntityNotFoundException      => (StatusCodes.Status404NotFound,           LogLevel.Information, true),
        ConcurrencyConflictException => (StatusCodes.Status409Conflict,           LogLevel.Warning,     true),
        ExternalServiceException     => (StatusCodes.Status503ServiceUnavailable, LogLevel.Error,       false),
        PersistenceException         => (StatusCodes.Status503ServiceUnavailable, LogLevel.Error,       false),
        _                            => (StatusCodes.Status400BadRequest,         LogLevel.Warning,     true)
    };
}
