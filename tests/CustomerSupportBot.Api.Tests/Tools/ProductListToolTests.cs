using System.Globalization;
using CustomerSupportBot.Api.Tests.Infrastructure;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Tools;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Api.Tests.Tools;

/// <summary>
/// <c>ProductListTool</c>'un kategori seçim akışı.
///
/// <para>
/// Bu testlerin çoğu, tool'un LLM'e <b>doğruyu</b> söyleyip söylemediğine bakar; çıktı biçimine
/// değil. Üç ayrı hata bu yüzden gözden kaçmıştı: ipucu düşse bile "ekran gösterildi" denmesi,
/// boş kategorilerin seçenek olarak sunulması ve "kategori yok" ile "kategori boş"un aynı
/// mesaja inmesi. Üçü de derleme hatası vermez, yalnızca modeli yanlış yönlendirir.
/// </para>
/// </summary>
[Collection("PostgresCatalog")]
public class ProductListToolTests
{
    private readonly PostgresCatalogFixture _fixture;

    // Seed'de gerçekten var ama hiç ürünü yok — kullanıcıya seçenek olarak sunulmamalı.
    private const string EmptyCategory = "Çorbalar";
    private const string PopulatedCategory = "İçecekler";

    public ProductListToolTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    private ProductToolsService CreateSut(bool pickerReaches, out IUiHintEmitter emitter)
    {
        emitter = Substitute.For<IUiHintEmitter>();
        emitter.Emit(Arg.Any<StreamEvent>()).Returns(pickerReaches);
        return new ProductToolsService(_fixture.ProductRepo, emitter);
    }

    // ═══ Repo sözleşmesi ═══

    [Fact]
    public void GetSelectableCategories_ExcludesEmptyCategories()
    {
        var selectable = _fixture.ProductRepo.GetSelectableCategories();

        selectable.Should().NotBeEmpty();
        selectable.Should().NotContain(EmptyCategory);
        selectable.Should().Contain(PopulatedCategory);
    }

    [Fact]
    public void GetByCategory_UnknownName_ReportsCategoryMissing()
    {
        var result = _fixture.ProductRepo.GetByCategory("ZZZ-boyle-kategori-yok");

        result.CategoryExists.Should().BeFalse();
        result.CanonicalName.Should().BeNull();
        result.Products.Should().BeEmpty();
    }

    /// <summary>Ayrımın bütün mesele olduğu durum: kategori VAR, sadece içi boş.</summary>
    [Fact]
    public void GetByCategory_ExistingButEmpty_ReportsCategoryPresent()
    {
        var result = _fixture.ProductRepo.GetByCategory(EmptyCategory);

        result.CategoryExists.Should().BeTrue();
        result.CanonicalName.Should().Be(EmptyCategory);
        result.Products.Should().BeEmpty();
    }

    /// <summary>Kolon case-insensitive collation'lı; kanonik yazım geri dönmeli.</summary>
    [Fact]
    public void GetByCategory_DifferentCasing_ReturnsCanonicalName()
    {
        var result = _fixture.ProductRepo.GetByCategory(PopulatedCategory.ToLowerInvariant());

        result.CategoryExists.Should().BeTrue();
        result.CanonicalName.Should().Be(PopulatedCategory);
    }

    // ═══ Picker dalı ═══

    [Fact]
    public void NoCategory_PickerReachesScreen_TellsModelScreenWasShown()
    {
        var sut = CreateSut(pickerReaches: true, out var emitter);

        var r = sut.ProductListTool();

        r.Success.Should().BeTrue();
        emitter.Received(1).Emit(Arg.Any<StreamEvent>());
        r.Message.Should().Contain("gösterildi");
    }

    /// <summary>
    /// Sesli kanalın davranışı: ipucu düşer, ekran yoktur. Tool "gösterildi" DEMEMELİ,
    /// aksi halde model kullanıcıyı olmayan bir ekrana yönlendirir.
    /// </summary>
    [Fact]
    public void NoCategory_PickerDropped_TellsModelToReadCategoriesAloud()
    {
        var sut = CreateSut(pickerReaches: false, out _);

        var r = sut.ProductListTool();

        r.Success.Should().BeTrue();
        r.Message.Should().NotContain("gösterildi");
        // Model seçenekleri kendisi iletebilsin diye kategoriler mesajda olmalı.
        r.Message.Should().Contain(PopulatedCategory);
    }

    [Fact]
    public void NoCategory_PickerNeverOffersEmptyCategories()
    {
        var sut = CreateSut(pickerReaches: false, out _);

        var r = sut.ProductListTool();

        r.Message.Should().NotContain(EmptyCategory);
    }

    // ═══ Kategori dalı ═══

    [Fact]
    public void UnknownCategory_ReturnsCategoryNotFound_WithValidOptions()
    {
        var sut = CreateSut(pickerReaches: true, out _);

        var r = sut.ProductListTool("ZZZ-boyle-kategori-yok");

        r.Success.Should().BeFalse();
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.CategoryNotFound);
        // Model kendini düzeltebilsin diye geçerli adlar mesajda olmalı.
        r.Message.Should().Contain(PopulatedCategory);
    }

    /// <summary>
    /// Var olan ama boş kategori, "yok" ile aynı koda düşmemeli — burada tekrar denemek
    /// anlamsızdır, yanlış adda ise doğrusu tekrar denemektir.
    /// </summary>
    [Fact]
    public void EmptyCategory_ReturnsProductNotFound_NotCategoryNotFound()
    {
        var sut = CreateSut(pickerReaches: true, out _);

        var r = sut.ProductListTool(EmptyCategory);

        r.Success.Should().BeFalse();
        r.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.ProductNotFound);
        r.Error.Code.Should().NotBe(WellKnown.ToolErrorCodes.CategoryNotFound);
        r.Message.Should().Contain(EmptyCategory);
    }

    [Fact]
    public void PopulatedCategory_ListsProducts()
    {
        var sut = CreateSut(pickerReaches: true, out _);

        var r = sut.ProductListTool(PopulatedCategory);

        r.Success.Should().BeTrue();
        r.Message.Should().NotBeEmpty();
    }

    /// <summary>Payload içinde iki farklı yazım dolaşmasın: echo kanonik olmalı.</summary>
    [Fact]
    public void PopulatedCategory_EchoesCanonicalNameNotCallerSpelling()
    {
        var sut = CreateSut(pickerReaches: true, out _);

        var r = sut.ProductListTool(PopulatedCategory.ToLowerInvariant());

        r.Success.Should().BeTrue();
        var category = r.Data!.GetType().GetProperty("category")!.GetValue(r.Data) as string;
        category.Should().Be(PopulatedCategory);
    }

    // ═══ Para birimi ═══

    [Fact]
    public void PopulatedCategory_PricesAreFormattedAsTurkishLira()
    {
        var sut = CreateSut(pickerReaches: true, out _);

        var r = sut.ProductListTool(PopulatedCategory);

        r.Message.Should().Contain(" TL");
        r.Message.Should().NotContain("$");
    }

    /// <summary>
    /// Biçim sunucunun <c>CurrentCulture</c>'ına bağlı olmamalı — container'larda genelde
    /// invariant kültür geçerlidir ve fiyat aynı kod yolundan bir yerde "18,00", başka yerde
    /// "18.00" çıkardı. Kültür tr-TR olarak sabitlendiği için ondalık ayıracı hep virgül.
    /// </summary>
    [Fact]
    public void PriceFormat_IsIndependentOfAmbientCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var sut = CreateSut(pickerReaches: true, out _);

            var r = sut.ProductListTool(PopulatedCategory);

            // "18,00 TL" — invariant kültür altında bile virgül ondalık ayıracı.
            r.Message.Should().MatchRegex(@"\d+,\d{2} TL");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ProductInquiry_PriceIsFormattedAsTurkishLira()
    {
        var sut = CreateSut(pickerReaches: true, out _);
        var product = _fixture.ProductRepo.GetAll().First().Name;

        var r = sut.ProductInquiryTool(product);

        r.Success.Should().BeTrue();
        r.Message.Should().Contain(" TL");
        r.Message.Should().NotContain("$");
    }
}
