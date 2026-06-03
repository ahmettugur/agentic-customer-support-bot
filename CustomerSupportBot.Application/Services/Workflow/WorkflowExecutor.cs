// Application/Services/Workflow/WorkflowExecutor.cs
// Low-Code Workflow Designer — WorkflowDefinition'ı graph traversal ile yürütür.
// LLM çağrısı YAPMAZ: input regex → variable → tool lookup → template substitution → branch.
// Adımlar Next/OnTrue/OnFalse referanslarıyla DAG oluşturur; executor bu grafiği traversal eder.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CustomerSupportBot.Application.Services.Tools;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Workflow;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Workflow;

public partial class WorkflowExecutor
{
    private readonly ILogger<WorkflowExecutor> _logger;
    private readonly CustomerSupportToolsService _tools;

    // Sonsuz döngüye karşı güvenlik — bir workflow bu kadar adımdan fazla çalışamaz.
    private const int MaxStepExecutions = 50;

    public WorkflowExecutor(CustomerSupportToolsService tools, ILogger<WorkflowExecutor> logger)
    {
        _tools = tools;
        _logger = logger;
    }

    /// <summary>
    /// Yan etkili tool'lar workflow içinden çağrılamaz (HITL gate'i bypass etmemek için).
    /// </summary>
    private static readonly HashSet<string> ForbiddenTools = new(StringComparer.OrdinalIgnoreCase)
    {
        WellKnown.ToolNames.OrderPlacement,
        WellKnown.ToolNames.OrderCancel,
        WellKnown.ToolNames.ReturnRequest,
        WellKnown.ToolNames.ComplaintRegistration,
        WellKnown.ToolNames.HumanHandoff
    };

    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_.]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex TemplateRegex();

    public WorkflowExecutionResult Execute(
        WorkflowDefinition definition,
        string userInput,
        Dictionary<string, string>? initialVariables = null)
    {
        var sw = Stopwatch.StartNew();
        var result = new WorkflowExecutionResult
        {
            WorkflowId = definition.Id,
            StartedAt  = DateTime.UtcNow
        };

        if (!definition.IsActive)
        {
            result.Error      = "Workflow pasif.";
            result.DurationMs = sw.ElapsedMilliseconds;
            return result;
        }

        // ─── Variables init ───────────────────────────────────────────────────
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["input"] = userInput ?? ""
        };
        if (initialVariables != null)
            foreach (var kv in initialVariables) vars[kv.Key] = kv.Value;

        // ─── Input pattern extraction ─────────────────────────────────────────
        foreach (var (varName, pattern) in definition.InputPatterns)
        {
            try
            {
                var match = Regex.Match(userInput ?? "", pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var value = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                    vars[varName] = value;
                }
            }
            catch (RegexMatchTimeoutException) { /* zaman aşımı → atla */ }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex,
                    "Workflow {Id}: {Var} için geçersiz regex.", definition.Id, varName);
            }
        }

        // ─── Graph traversal ─────────────────────────────────────────────────
        // Adımları ID'ye göre indeksle — O(1) lookup.
        var stepMap = definition.Steps.ToDictionary(s => s.Id, s => s, StringComparer.Ordinal);

        // Başlangıç adımı: StartStepId varsa onu, yoksa listenin ilk elemanını kullan.
        var currentId = definition.StartStepId
            ?? definition.Steps.FirstOrDefault()?.Id;

        var output      = new StringBuilder();
        var visitedSet  = new HashSet<string>(StringComparer.Ordinal); // döngü tespiti
        int execCount   = 0;

        while (currentId is not null)
        {
            // ── Güvenlik kontrolleri ──────────────────────────────────────────
            if (++execCount > MaxStepExecutions)
            {
                result.Error = $"Maksimum adım sayısına ({MaxStepExecutions}) ulaşıldı. Döngü olabilir.";
                _logger.LogWarning(
                    "Workflow {Id}: {Max} adım limitine ulaşıldı.", definition.Id, MaxStepExecutions);
                break;
            }

            if (!visitedSet.Add(currentId))
            {
                result.Error = $"Döngü tespit edildi: adım '{currentId}' zaten ziyaret edildi.";
                _logger.LogWarning(
                    "Workflow {Id}: döngü → {StepId}.", definition.Id, currentId);
                break;
            }

            if (!stepMap.TryGetValue(currentId, out var step))
            {
                result.Error = $"Adım bulunamadı: '{currentId}'.";
                _logger.LogWarning(
                    "Workflow {Id}: bilinmeyen adım ID'si → {StepId}.", definition.Id, currentId);
                break;
            }

            // ── Adımı yürüt ──────────────────────────────────────────────────
            var trace = new WorkflowStepTrace
            {
                StepId = step.Id,
                Type   = step.Type,
                Label  = step.Label
            };

            string? nextId;
            try
            {
                nextId = ExecuteStep(step, vars, output, trace);
            }
            catch (Exception ex)
            {
                trace.Error = ex.Message;
                _logger.LogWarning(ex,
                    "Workflow {Id} adım {StepId} hata verdi.", definition.Id, step.Id);
                result.StepTraces.Add(trace);
                break; // hata durumunda traversal'ı durdur
            }

            result.StepTraces.Add(trace);
            currentId = nextId;
        }

        result.Success       = result.Error is null
                               && !result.StepTraces.Any(t => t.Error is not null);
        result.FinalResponse  = output.ToString().TrimEnd();
        result.FinalVariables = vars;
        result.DurationMs     = sw.ElapsedMilliseconds;
        return result;
    }

    // ─── Step dispatch ────────────────────────────────────────────────────────

    /// <summary>Adımı yürütür ve bir sonraki adımın ID'sini döner (null = workflow sona erer).</summary>
    private string? ExecuteStep(
        WorkflowStep step,
        Dictionary<string, string> vars,
        StringBuilder output,
        WorkflowStepTrace trace)
    {
        switch (step.Type)
        {
            case WorkflowStepType.Respond:
            {
                var rendered = Render(step.Template ?? "", vars);
                if (output.Length > 0) output.AppendLine();
                output.Append(rendered);
                trace.Output = rendered;
                return step.Next;
            }

            case WorkflowStepType.Lookup:
            {
                trace.Output = ExecuteLookup(step, vars);
                return step.Next;
            }

            case WorkflowStepType.Branch:
            {
                var pass = EvaluateCondition(step.Condition ?? "", vars);
                trace.Output = $"condition={pass}";
                return pass ? step.OnTrue : step.OnFalse;
            }

            case WorkflowStepType.SetVariable:
            {
                if (!string.IsNullOrWhiteSpace(step.VariableName))
                {
                    var value = Render(step.VariableValue ?? "", vars);
                    vars[step.VariableName] = value;
                    trace.Output = $"{step.VariableName}={value}";
                }
                return step.Next;
            }

            default:
                throw new InvalidOperationException($"Bilinmeyen adım tipi: {step.Type}");
        }
    }

    // ─── Lookup ──────────────────────────────────────────────────────────────

    private string ExecuteLookup(WorkflowStep step, Dictionary<string, string> vars)
    {
        if (string.IsNullOrWhiteSpace(step.Tool))
            throw new InvalidOperationException("Lookup adımı için tool adı zorunlu.");

        if (ForbiddenTools.Contains(step.Tool))
            throw new InvalidOperationException(
                $"'{step.Tool}' tool'u workflow içinden çağrılamaz (yan etkili — HITL gate gerekir).");

        var resolved = step.Parameters.ToDictionary(
            kv => kv.Key,
            kv => ResolveParameterValue(kv.Value, vars),
            StringComparer.Ordinal);

        ToolResult tr = step.Tool.ToLowerInvariant() switch
        {
            WellKnown.ToolNames.ProductInquiry =>
                _tools.ProductInquiryTool(
                    GetParam(resolved, "productName") ?? GetParam(resolved, "product_name") ?? ""),

            WellKnown.ToolNames.ProductList =>
                _tools.ProductListTool(GetParam(resolved, "category")),

            WellKnown.ToolNames.OrderStatus =>
                _tools.OrderStatusTool(
                    GetParam(resolved, "orderId") ?? GetParam(resolved, "order_id") ?? ""),

            WellKnown.ToolNames.GetLastOrder =>
                _tools.GetLastOrderTool(
                    GetParam(resolved, "customerId") ?? GetParam(resolved, "customer_id") ?? ""),

            WellKnown.ToolNames.GetAllOrders =>
                _tools.GetAllOrdersTool(
                    GetParam(resolved, "customerId") ?? GetParam(resolved, "customer_id") ?? ""),

            _ => throw new InvalidOperationException(
                     $"Bilinmeyen veya desteklenmeyen tool: '{step.Tool}'")
        };

        if (!string.IsNullOrWhiteSpace(step.StoreAs))
        {
            vars[$"{step.StoreAs}.message"] = tr.Message;
            vars[$"{step.StoreAs}.success"] = tr.Success ? "true" : "false";
            vars[step.StoreAs]              = tr.Message;

            if (tr.Data is not null)
            {
                try { vars[$"{step.StoreAs}.data"] = JsonSerializer.Serialize(tr.Data); }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Lookup data serialize edilemedi.");
                }
            }
        }

        return $"{step.Tool} → success={tr.Success}, msg={Truncate(tr.Message, 120)}";
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>{varName} placeholder'larını variables'tan substitute eder.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> vars)
    {
        if (string.IsNullOrEmpty(template)) return "";
        return TemplateRegex().Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            return vars.TryGetValue(key, out var v) ? v : m.Value;
        });
    }

    /// <summary>Koşul ifadesi değerlendirir. Desteklenen operatörler: ==, !=, exists, missing.</summary>
    public static bool EvaluateCondition(string expression, IReadOnlyDictionary<string, string> vars)
    {
        if (string.IsNullOrWhiteSpace(expression)) return true;
        var trimmed = expression.Trim();

        if (trimmed.EndsWith(" exists", StringComparison.OrdinalIgnoreCase))
        {
            var name = trimmed[..^7].Trim();
            return vars.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v);
        }
        if (trimmed.EndsWith(" missing", StringComparison.OrdinalIgnoreCase))
        {
            var name = trimmed[..^8].Trim();
            return !vars.TryGetValue(name, out var v) || string.IsNullOrWhiteSpace(v);
        }

        foreach (var op in new[] { "==", "!=" })
        {
            var idx = trimmed.IndexOf(op, StringComparison.Ordinal);
            if (idx < 0) continue;
            var left      = trimmed[..idx].Trim();
            var right     = trimmed[(idx + op.Length)..].Trim().Trim('"', '\'');
            var leftValue = vars.TryGetValue(left, out var lv) ? lv : "";
            var equals    = string.Equals(leftValue, right, StringComparison.Ordinal);
            return op == "==" ? equals : !equals;
        }

        return false;
    }

    private static string ResolveParameterValue(string raw, IReadOnlyDictionary<string, string> vars)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        if (raw.StartsWith('$'))
        {
            var name = raw[1..];
            return vars.TryGetValue(name, out var v) ? v : "";
        }
        return Render(raw, vars);
    }

    private static string? GetParam(IReadOnlyDictionary<string, string> p, string key) =>
        p.TryGetValue(key, out var v) ? v : null;

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";
}
