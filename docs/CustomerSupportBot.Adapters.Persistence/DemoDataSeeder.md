# DemoDataSeeder

> 💡 **Analiz notu:** Uygulama ilk başlatıldığında boş DB'ye demo verisi yazar — `NorthwindSeedData`'dan ürün/kategori/müşteri/sipariş verilerini seed eder. Development ortamında hızlı başlangıç sağlar.

## Ne İşe Yarar

Boş bir veritabanında hızlı başlamak için demo/deneme verisi seed eden `IHostedService` implementasyonudur.

## Hangi Amaçla Kullanılır

Uygulama ilk başlatıldığında, veritabanı tabloları boşsa default admin kullanıcı, agent'lar, Northwind ürün/müşteri/sipariş verisi ve demo müşteri login hesabı oluşturur.

## Sorumlulukları

- Default admin kullanıcısı oluşturmak (konfigürasyondaki `Auth:DefaultAdmin` bölümünden).
- Default human agent'lar oluşturmak.
- Northwind tarzı demo ürün, kategori, müşteri ve sipariş verisi seed etmek (`NorthwindSeedData` kullanır).
- Demo müşteri login hesabı oluşturmak.
- Tüm seed işlemlerini idempotent yapmak (tablo boşsa/kayıt yoksa çalışır).

## Diğer Katman ve Bileşenlerle İlişkileri

- **Implements**: `IHostedService` — startup'ta çalışır.
- **DI ile inject edilen**: `IServiceProvider`, `IConfiguration`.
- **İlişkili sınıf**: [NorthwindSeedData](NorthwindSeedData.md) — statik seed veri kaynağı.
- **Bilinçli ayrım**: `PersistenceHydrator`'dan ayrıdır — DemoDataSeeder üretimde kapatılabilir/kaldırılabilir, PersistenceHydrator kapatılamaz.

## Kullanılma Nedeni ve Tasarım Yaklaşımı

Development ve demo ortamlarında hızlı başlangıç sağlar. Her seed metodu kendi tablosunun durumunu kontrol eder — idempotent. Üretimde bu servis DI kaydından kaldırılabilir.

## Bağımlılıklar

- `CustomerSupportBotDbContext` — EF Core DbContext.
- `IPasswordHasher` — Admin/müşteri parola hash'leme.
- `IConfiguration` — Default admin credential konfigürasyonu.
- [NorthwindSeedData](NorthwindSeedData.md) — Statik seed verileri.

---

## Güvenlik notu

Seeder demo hesapları **varsayılan şifrelerle** oluşturur. Üretimde ilk başlatmadan önce:

1. `Auth:DefaultAdminPassword` değerini ayarlayın — verilmezse `Admin123!` kullanılır
   (`DemoDataSeeder.cs`). Depodaki `appsettings.json` bunu boş bırakır; `Admin123!` yalnızca
   `appsettings.Development.json` içindedir.
2. `Auth:DefaultAgentPassword` değerini ayarlayın — verilmezse `Agent123!` kullanılır.
3. Oluşturulan agent kullanıcı adları: `john.doe`, `jane.smith` (e-postanın `@` öncesi).

Seeder her iki durumda da log'a rotasyon uyarısı yazar. En temizi, üretimde bu servisi hiç
çalıştırmamaktır — demo verisi üretim ortamına ait değildir.

