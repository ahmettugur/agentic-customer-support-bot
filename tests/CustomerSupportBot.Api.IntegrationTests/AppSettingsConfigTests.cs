// Infrastructure/AppSettingsConfigTests.cs
//
// appsettings.json ↔ appsettings.Development.json SÜRÜKLENME KORUMASI.
//
// ASP.NET Core, Development ortamında appsettings.Development.json'ı appsettings.json'ın
// ÜZERİNE yazar (config layering). Bir ayarı yalnızca base dosyada düzeltip Development
// kopyasını unutmak, derleyici/test hatası vermeden sessizce eski (yanlış) davranışı
// canlı tutar — tam olarak burada test edilen olay gerçekten yaşandı: Sla.Approvals.OnBreach
// base dosyada AutoReject'ten None'a çekildi (bkz. SlaOptions.cs), ama
// appsettings.Development.json'daki AYRI kopyası "AutoReject" olarak kaldı. dotnet run
// varsayılan olarak Development ortamında çalıştığı için (launchSettings.json), gerçek
// çalışan davranış hâlâ eskisiydi: her bekleyen HITL onayı 60 saniyede sessizce reddediliyordu
// — base'deki düzeltmeye rağmen.
//
// appsettings.Development.json .gitignore'dadır (**/appsettings.Development.json) — her
// geliştiricinin/CI'ın makinesinde bu dosya hiç bulunmayabilir. Aşağıdaki Development kontrolü
// bu yüzden OPSİYONELDİR: dosya yoksa (temiz checkout/CI) ya da Sla.Approvals.OnBreach'i
// AYRICA tanımlamıyorsa (base'den miras alıyorsa — artık önerilen durum, kalıcı çözüm bu
// anahtarı dev dosyasından tamamen kaldırmaktı) test sessizce geçer; yalnızca dosya VARSA ve
// override VARSA yanlış değeri yakalar.
using System.Text.Json;

namespace CustomerSupportBot.Api.IntegrationTests;

public class AppSettingsConfigTests
{
    [Fact]
    public void SlaApprovalsOnBreach_IsNotAutoRejectInBaseAppSettings()
    {
        // Bloklamayan HITL modelinde (ApprovalGateService.ExecuteWithApprovalGateAsync) bir
        // onay admin karar verene ya da ApprovalOptions.StalePendingHours (72 saat) aşılana
        // kadar kuyrukta kalmalı. Sla.Approvals.OnBreach=AutoReject, bunu 60 saniyeye
        // indirip tasarımı geçersiz kılar.
        var json = File.ReadAllText(RepoPath("src/CustomerSupportBot.Api/appsettings.json"));
        using var doc = JsonDocument.Parse(json);
        var onBreach = doc.RootElement
            .GetProperty("Sla").GetProperty("Approvals").GetProperty("OnBreach").GetString();

        onBreach.Should().NotBe("AutoReject",
            "appsettings.json → Sla:Approvals:OnBreach=AutoReject, bloklamayan HITL modelinde " +
            "her bekleyen onayı admin bakmasa bile ~60 saniyede sessizce reddeder");
    }

    [Fact]
    public void SlaApprovalsOnBreach_DevelopmentOverride_DoesNotReintroduceAutoReject()
    {
        var devPath = RepoPathIfExists("src/CustomerSupportBot.Api/appsettings.Development.json");
        if (devPath is null) return; // Bu makinede dosya yok (gitignored) — kontrol edilecek bir şey yok.

        using var doc = JsonDocument.Parse(File.ReadAllText(devPath));
        // Anahtarların herhangi biri yoksa dev dosyası bu ayarı hiç override etmiyor demektir —
        // base'den miras alır, bu güvenli/önerilen durumdur.
        if (!doc.RootElement.TryGetProperty("Sla", out var sla)) return;
        if (!sla.TryGetProperty("Approvals", out var approvals)) return;
        if (!approvals.TryGetProperty("OnBreach", out var onBreachEl)) return;

        onBreachEl.GetString().Should().NotBe("AutoReject",
            "appsettings.Development.json, Sla:Approvals:OnBreach'i AYRICA tanımlıyor ve " +
            "AutoReject'e sürüklenmiş — dotnet run (varsayılan Development ortamı) bunu " +
            "base'deki düzeltmeye rağmen çalıştırır. En kalıcı çözüm bu anahtarı dev " +
            "dosyasından tamamen kaldırıp base'den miras almasını sağlamaktır.");
    }

    private static string RepoPath(string repoRelativePath)
    {
        var path = RepoPathIfExists(repoRelativePath);
        path.Should().NotBeNull($"repo kökü bulunamadı ({repoRelativePath} aranıyordu)");
        return path!;
    }

    private static string? RepoPathIfExists(string repoRelativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, repoRelativePath)))
            dir = dir.Parent;

        return dir is null ? null : Path.Combine(dir.FullName, repoRelativePath);
    }
}
