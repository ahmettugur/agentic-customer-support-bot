// Tests/AdminTabsTests.cs
//
// Admin panelinin adresli bölümleri (/admin?tab=…): kenar çubuğu ve Admin sayfası aynı kuralı kullanır.
// Temsilci, adresi elle yazsa bile yönetici bölümünü açamamalı.

using CustomerSupportBot.Web.Helpers;

namespace CustomerSupportBot.Web.Tests;

public class AdminTabsTests
{
    [Theory]
    [InlineData("?tab=chats", "chats")]
    [InlineData("tab=History", "history")]
    [InlineData("?x=1&tab=analytics&y=2", "analytics")]
    [InlineData("?TAB=replies", "replies")]
    public void FromQuery_reads_tab_value(string query, string expected)
        => AdminTabs.FromQuery(query).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("?")]
    [InlineData("?tab=")]
    [InlineData("?other=1")]
    public void FromQuery_returns_null_when_missing(string? query)
        => AdminTabs.FromQuery(query).Should().BeNull();

    [Theory]
    [InlineData(null, false, "approvals")]
    [InlineData("unknown", false, "approvals")]
    [InlineData("improvements", false, "improvements")]
    [InlineData(null, true, "escalations")]
    [InlineData("chats", true, "chats")]
    [InlineData("approvals", true, "escalations")]
    [InlineData("analytics", true, "escalations")]
    public void Normalize_applies_role_and_default(string? tab, bool isAgent, string expected)
        => AdminTabs.Normalize(tab, isAgent).Should().Be(expected);
}
