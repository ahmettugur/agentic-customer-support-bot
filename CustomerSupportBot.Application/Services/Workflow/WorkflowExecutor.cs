// Application/Services/Workflow/WorkflowExecutor.cs
// Low-Code Workflow Designer — Bir WorkflowDefinition'ı deterministik olarak yürütür.
// LLM çağrısı YAPMAZ. Sadece: input regex → variable, tool lookup, template substitution,
// Conditional branch. Yan etkili tool'lar (order_placement, complaint) izin verilmez.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Workflow;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Workflow;

public partial class WorkflowExecutor
{
    private readonly ILogger<WorkflowExecutor> _logger;
    private readonly CustomerSupportToolsService _tools;

    public WorkflowExecutor(CustomerSupportToolsService tools, ILogger<WorkflowExecutor> logger)
    {
        _tools = tools;
        _logger = logger;
    }

    /// <summary>
    /// Yan etkili tool'lar workflow içinden ÇAĞRILAMAZ.
    /// (HITL gate'i bypass etmemek için.)
    /// </summary>
    private static readonly HashSet<string> ForbiddenTools = new(StringComparer.OrdinalIgnoreCase)
    {
        WellKnown.ToolNames.OrderPlacement,
        WellKnown.ToolNames.OrderCancel,
        WellKnown.ToolNames.ReturnRequest,
        WellKnown.ToolNames.ComplaintRegistration,
        WellKnown.ToolNames.HumanHandoff
    };

    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant)]
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
            StartedAt = DateTime.UtcNow
        };

        if (!definition.IsActive)
        {
            result.Error = "Workflow pasif.";
            result.DurationMs = sw.ElapsedMilliseconds;
            return result;
        }

        // ─── Variables init ───
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["input"] = userInput ?? ""
        };
        if (initialVariables != null)
        {
            foreach (var kv in initialVariables) vars[kv.Key] = kv.Value;
        }

        // ─── Input pattern extraction ───
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
            catch (RegexMatchTimeoutException) { /* skip */ }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Workflow {Id}: invalid regex for variable {Var}", definition.Id, varName);
            }
        }

        // ─── Step execution ───
        var output = new StringBuilder();
        for (int i = 0; i < definition.Steps.Count; i++)
        {
            var step = definition.Steps[i];
            var trace = new WorkflowStepTrace
            {
                StepId = step.Id,
                Type = step.Type,
                Label = step.Label
            };

            try
            {
                switch (step.Type)
                {
                    case WorkflowStepType.Respond:
                        var rendered = Render(step.Template ?? "", vars);
                        if (output.Length > 0) output.AppendLine();
                        output.Append(rendered);
                        trace.Output = rendered;
                        break;

                    case WorkflowStepType.Lookup:
                        trace.Output = ExecuteLookup(step, vars);
                        break;

                    case WorkflowStepType.Branch:
                        var pass = EvaluateCondition(step.Condition ?? "", vars);
                        trace.Output = $"condition={pass}";
                        if (!pass)
                        {
                            // Sonraki SkipNext adımı atla
                            i += Math.Max(0, step.SkipNext);
                        }
                        break;

                    case WorkflowStepType.SetVariable:
                        if (!string.IsNullOrWhiteSpace(step.VariableName))
                        {
                            var value = Render(step.VariableValue ?? "", vars);
                            vars[step.VariableName] = value;
                            trace.Output = $"{step.VariableName}={value}";
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                trace.Error = ex.Message;
                _logger.LogWarning(ex, "Workflow {Id} step {StepId} failed", definition.Id, step.Id);
            }

            result.StepTraces.Add(trace);
        }

        result.Success = !result.StepTraces.Any(t => !string.IsNullOrEmpty(t.Error));
        result.FinalResponse = output.ToString().TrimEnd();
        result.FinalVariables = vars;
        result.DurationMs = sw.ElapsedMilliseconds;
        return result;
    }

    /// <summary>{varName} placeholder'larını variables'tan substitute eder.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> vars)
    {
        if (string.IsNullOrEmpty(template)) return "";
        return TemplateRegex().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            return vars.TryGetValue(key, out var v) ? v : match.Value;
        });
    }

    /// <summary>"varName == value" / "!=" / "exists" / "missing".</summary>
    public static bool EvaluateCondition(string expression, IReadOnlyDictionary<string, string> vars)
    {
        if (string.IsNullOrWhiteSpace(expression)) return true;
        var trimmed = expression.Trim();

        // "varName exists"
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

        // "name == value" / "name != value"
        foreach (var op in new[] { "==", "!=" })
        {
            var idx = trimmed.IndexOf(op, StringComparison.Ordinal);
            if (idx < 0) continue;
            var left = trimmed[..idx].Trim();
            var right = trimmed[(idx + op.Length)..].Trim().Trim('"', '\'');
            var leftValue = vars.TryGetValue(left, out var lv) ? lv : "";
            var equals = string.Equals(leftValue, right, StringComparison.Ordinal);
            return op == "==" ? equals : !equals;
        }

        return false;
    }

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
                    GetStringParam(resolved, "productName") ?? GetStringParam(resolved, "product_name") ?? ""),
            WellKnown.ToolNames.ProductList =>
                _tools.ProductListTool(
                    GetStringParam(resolved, "category")),
            WellKnown.ToolNames.OrderStatus =>
                _tools.OrderStatusTool(
                    GetStringParam(resolved, "orderId") ?? GetStringParam(resolved, "order_id") ?? ""),
            WellKnown.ToolNames.GetLastOrder =>
                _tools.GetLastOrderTool(
                    GetStringParam(resolved, "customerId") ?? GetStringParam(resolved, "customer_id") ?? ""),
            WellKnown.ToolNames.GetAllOrders =>
                _tools.GetAllOrdersTool(
                    GetStringParam(resolved, "customerId") ?? GetStringParam(resolved, "customer_id") ?? ""),
            _ => throw new InvalidOperationException($"Bilinmeyen veya desteklenmeyen tool: {step.Tool}")
        };

        if (!string.IsNullOrWhiteSpace(step.StoreAs))
        {
            vars[$"{step.StoreAs}.message"] = tr.Message;
            vars[$"{step.StoreAs}.success"] = tr.Success ? "true" : "false";
            if (tr.Data != null)
            {
                try
                {
                    var json = JsonSerializer.Serialize(tr.Data);
                    vars[$"{step.StoreAs}.data"] = json;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Lookup data serialize edilemedi.");
                }
            }
            // Geriye dönük kolaylık: doğrudan storeAs adını da Message olarak set et
            vars[step.StoreAs] = tr.Message;
        }

        return $"{step.Tool} → success={tr.Success}, msg={Truncate(tr.Message, 120)}";
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

    private static string? GetStringParam(IReadOnlyDictionary<string, string> p, string key) =>
        p.TryGetValue(key, out var v) ? v : null;

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";
}
