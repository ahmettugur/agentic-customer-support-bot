// xUnit collection definitions are resolved per test ASSEMBLY — PostgresCatalogFixture itself
// lives in CustomerSupportBot.Tests.Shared, but every assembly that uses [Collection("PostgresCatalog")]
// needs its own local [CollectionDefinition] shim pointing at it.

using CustomerSupportBot.Tests.Shared;

namespace CustomerSupportBot.Application.Tests;

[CollectionDefinition("PostgresCatalog")]
public sealed class PostgresCatalogCollection : ICollectionFixture<PostgresCatalogFixture>;
