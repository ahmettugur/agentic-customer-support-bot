namespace CustomerSupportBot.Web.Services;

/// <summary>
/// İki ayrı kimlik alanı: staff (Admin/Agent) ve Customer. Aynı tarayıcıda ikisi de
/// aynı anda aktif olabilir — token'ları ayrı localStorage anahtarlarında tutulur.
/// </summary>
public enum AuthScope
{
    Staff,
    Customer
}

public static class AuthScopeRouter
{
    /// <summary>
    /// Route'un hangi kimlik alanına ait olduğunu belirler. Sadece "/" (chat) ve
    /// "/customer-login" müşteri alanıdır; geri kalan her şey (admin, login, traces,
    /// replay, sla, knowledge) staff alanıdır.
    /// </summary>
    public static AuthScope Resolve(string relativePath)
    {
        var trimmed = relativePath.TrimStart('/');
        return trimmed.Length == 0 || trimmed.StartsWith("customer-login", StringComparison.OrdinalIgnoreCase)
            ? AuthScope.Customer
            : AuthScope.Staff;
    }
}
