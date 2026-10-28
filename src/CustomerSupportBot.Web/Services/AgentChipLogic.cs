namespace CustomerSupportBot.Web.Services;

/// <summary>
/// Ajan çiplerinin kimliği (saf). Workflow executor id'si "&lt;AjanAdı&gt;_&lt;guid&gt;" biçiminde gelir;
/// <c>ui_hint</c> etiketi ise düz ajan adını taşır. Aynı ajanın iki çip olmaması için
/// eşleştirme her zaman taban ad üzerinden yapılır.
/// </summary>
public static class AgentChipLogic
{
    public static string BaseName(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        var i = id.IndexOf('_');
        return i < 0 ? id : id[..i];
    }

    public static bool SameAgent(string? a, string? b) =>
        BaseName(a).Length > 0 && string.Equals(BaseName(a), BaseName(b), StringComparison.Ordinal);
}
