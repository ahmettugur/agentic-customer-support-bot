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
    ///
    /// <para>
    /// Girdi genellikle <c>NavigationManager.ToBaseRelativePath(Uri)</c>'dir ve sorgu dizesini
    /// ile parçayı (<c>?…</c>, <c>#…</c>) İÇERİR. Karar yalnızca yol bölümüne bakılarak verilir:
    /// eskiden <c>"?session=…"</c> boş yol sayılmıyor, sohbet sayfası sorgu parametresiyle
    /// açıldığında staff alanı seçiliyordu — müşteri isteğine personel token'ı ekleniyor,
    /// 401'de müşteri personel giriş sayfasına yönleniyordu. Karşılaştırma da önek değil tam
    /// segment eşleşmesidir (<c>customer-loginx</c> müşteri alanı değildir).
    /// </para>
    /// </summary>
    public static AuthScope Resolve(string relativePath)
    {
        var path = relativePath ?? "";
        var end = path.IndexOfAny(['?', '#']);
        if (end >= 0) path = path[..end];

        var firstSegment = path.Trim('/').Split('/')[0];
        return firstSegment.Length == 0
               || string.Equals(firstSegment, "customer-login", StringComparison.OrdinalIgnoreCase)
            ? AuthScope.Customer
            : AuthScope.Staff;
    }
}
