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
