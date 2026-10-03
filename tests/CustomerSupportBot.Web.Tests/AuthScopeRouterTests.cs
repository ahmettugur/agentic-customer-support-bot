// Tests/AuthScopeRouterTests.cs
//
// Route → kimlik alanı eşlemesi. Girdi NavigationManager.ToBaseRelativePath(Uri)'dir ve sorgu
// dizesi ile parçayı içerir; karar yalnızca yol bölümüne göre verilmeli.

using CustomerSupportBot.Web.Services;

namespace CustomerSupportBot.Web.Tests;

public class AuthScopeRouterTests
{
    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("?session=abc")]
    [InlineData("/?session=abc")]
    [InlineData("#bottom")]
    [InlineData("customer-login")]
    [InlineData("customer-login?return=https%3A%2F%2Fapp%2F")]
    [InlineData("/Customer-Login/")]
    [InlineData("customer-login#x")]
    public void CustomerRoutes_ResolveToCustomer(string relativePath) =>
        AuthScopeRouter.Resolve(relativePath).Should().Be(AuthScope.Customer);

    [Theory]
    [InlineData("admin")]
    [InlineData("admin?tab=approvals")]
    [InlineData("login?return=x")]
    [InlineData("traces/abc")]
    [InlineData("replay?trace=customer-login")]
    [InlineData("customer-loginx")]
    [InlineData("knowledge#customer-login")]
    public void StaffRoutes_ResolveToStaff(string relativePath) =>
        AuthScopeRouter.Resolve(relativePath).Should().Be(AuthScope.Staff);
}
