namespace CustomerSupportBot.Application.Ports.Inbound;

public enum InputGuardVerdict { Allow, Sanitize, Reject }

public sealed record InputGuardResult(
    InputGuardVerdict Verdict,
    string SanitizedInput,
    IReadOnlyList<string> Flags,
    string? RejectionReason);

public interface IInputGuard
{
    InputGuardResult Inspect(string? input);
}
