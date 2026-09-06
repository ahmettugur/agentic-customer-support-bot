using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Adapters.Agents;

internal static class TerminationProtocol
{
    internal const string Prefix = WellKnown.Termination.Marker + ": reason=";

    internal static int FindStart(string text)
    {
        var lineStart = 0;
        while (lineStart < text.Length)
        {
            var start = lineStart;
            while (start < text.Length && text[start] is ' ' or '\t') start++;
            if (text.AsSpan(start).StartsWith(Prefix, StringComparison.Ordinal))
                return lineStart;
            var newline = text.IndexOf('\n', start);
            if (newline < 0) break;
            lineStart = newline + 1;
        }
        return -1;
    }
}
