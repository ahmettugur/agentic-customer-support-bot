// Hazır yanıtlar: doğrulama, kısayol benzersizliği, arama.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Services.SavedReplies;

namespace CustomerSupportBot.Application.Tests;

public class SavedReplyServiceTests
{
    private static SavedReplyService Build() => new(new InMemorySavedReplyStore());

    [Fact]
    public async Task Create_TrimsAndNormalizesTheShortcut()
    {
        var svc = Build();

        var r = await svc.CreateAsync(new SavedReplyInput("  Kargo gecikmesi ", "Siparişiniz yolda, yarın elinizde.", " Kargo-Gecikme "), "admin", TestContext.Current.CancellationToken);

        r.Status.Should().Be(SavedReplyStatus.Ok);
        r.Reply!.Title.Should().Be("Kargo gecikmesi");
        r.Reply.Shortcut.Should().Be("kargo-gecikme");
        r.Reply.CreatedBy.Should().Be("admin");
    }

    [Theory]
    [InlineData(null, "metin", null, "Başlık")]
    [InlineData("başlık", " ", null, "Metin")]
    [InlineData("başlık", "metin", "boşluk var", "Kısayol")]
    public async Task Create_RejectsInvalidInput(string? title, string? body, string? shortcut, string expectedField)
    {
        var r = await Build().CreateAsync(new SavedReplyInput(title, body, shortcut), "admin", TestContext.Current.CancellationToken);

        r.Status.Should().Be(SavedReplyStatus.Invalid);
        r.Error.Should().Contain(expectedField);
    }

    [Fact]
    public async Task Create_RejectsTooLongText()
    {
        var r = await Build().CreateAsync(new SavedReplyInput(new string('a', 101), new string('b', 2001), null), "admin", TestContext.Current.CancellationToken);

        r.Status.Should().Be(SavedReplyStatus.Invalid);
    }

    [Fact]
    public async Task DuplicateShortcut_IsRejected_CaseInsensitive_ButAReplyKeepsItsOwn()
    {
        var svc = Build();
        var first = await svc.CreateAsync(new SavedReplyInput("A", "metin", "iade"), "admin", TestContext.Current.CancellationToken);

        (await svc.CreateAsync(new SavedReplyInput("C", "metin", "IADE"), "admin", TestContext.Current.CancellationToken))
            .Status.Should().Be(SavedReplyStatus.DuplicateShortcut);
        (await svc.CreateAsync(new SavedReplyInput("D", "metin", "İADE"), "admin", TestContext.Current.CancellationToken))
            .Status.Should().Be(SavedReplyStatus.DuplicateShortcut, "I ve İ, kısayolda i sayılır");
        (await svc.UpdateAsync(first.Reply!.Id, new SavedReplyInput("A2", "metin", "iade"), TestContext.Current.CancellationToken))
            .Status.Should().Be(SavedReplyStatus.Ok, "kendi kısayolunu koruyabilir");
    }

    [Fact]
    public async Task Update_Missing_IsNotFound_AndDelete_ReturnsFalse()
    {
        var svc = Build();

        (await svc.UpdateAsync("yok", new SavedReplyInput("A", "b", null), TestContext.Current.CancellationToken)).Status.Should().Be(SavedReplyStatus.NotFound);
        (await svc.DeleteAsync("yok", TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task List_SearchesTitleBodyAndShortcut_SortedByTitle()
    {
        var svc = Build();
        await svc.CreateAsync(new SavedReplyInput("Kapanış", "İyi günler dileriz.", "kapanis"), "admin", TestContext.Current.CancellationToken);
        await svc.CreateAsync(new SavedReplyInput("Kargo gecikmesi", "Kargonuz yolda.", "kargo"), "admin", TestContext.Current.CancellationToken);
        await svc.CreateAsync(new SavedReplyInput("İade süreci", "İade 14 gün içinde yapılır.", null), "admin", TestContext.Current.CancellationToken);

        (await svc.ListAsync(ct: TestContext.Current.CancellationToken)).Select(r => r.Title).Should().Equal("İade süreci", "Kapanış", "Kargo gecikmesi");
        (await svc.ListAsync("KARGO", ct: TestContext.Current.CancellationToken)).Select(r => r.Title).Should().Equal("Kargo gecikmesi");
        (await svc.ListAsync("14 gün", ct: TestContext.Current.CancellationToken)).Select(r => r.Title).Should().Equal("İade süreci");
    }
}
