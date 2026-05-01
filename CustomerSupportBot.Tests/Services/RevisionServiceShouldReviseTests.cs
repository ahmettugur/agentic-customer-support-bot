using CustomerSupportBot.Models;
using CustomerSupportBot.Services;

namespace CustomerSupportBot.Tests.Services;

public class RevisionServiceShouldReviseTests
{
    [Fact]
    public void Null_ReturnsFalse() =>
        RevisionService.ShouldRevise(null).Should().BeFalse();

    [Fact]
    public void RevisionNeededTrue_ReturnsTrue() =>
        RevisionService.ShouldRevise(new ResponseCritique
        {
            RevisionNeeded = true,
            AddressesUserQuery = true,
            Completeness = 0.9,
            HallucinationRisk = 0.1
        }).Should().BeTrue();

    [Fact]
    public void DoesNotAddressQuery_ReturnsTrue() =>
        RevisionService.ShouldRevise(new ResponseCritique
        {
            RevisionNeeded = false,
            AddressesUserQuery = false,
            Completeness = 0.9,
            HallucinationRisk = 0.1
        }).Should().BeTrue();

    [Fact]
    public void LowCompleteness_ReturnsTrue() =>
        RevisionService.ShouldRevise(new ResponseCritique
        {
            RevisionNeeded = false,
            AddressesUserQuery = true,
            Completeness = 0.3,
            HallucinationRisk = 0.1
        }).Should().BeTrue();

    [Fact]
    public void HighHallucinationRisk_ReturnsTrue() =>
        RevisionService.ShouldRevise(new ResponseCritique
        {
            RevisionNeeded = false,
            AddressesUserQuery = true,
            Completeness = 0.9,
            HallucinationRisk = 0.5
        }).Should().BeTrue();

    [Fact]
    public void AllGood_ReturnsFalse() =>
        RevisionService.ShouldRevise(new ResponseCritique
        {
            RevisionNeeded = false,
            AddressesUserQuery = true,
            Completeness = 0.9,
            HallucinationRisk = 0.1
        }).Should().BeFalse();
}
