# Katman Bazlı Dokümantasyon Üretim Kuralları

Bu skill, bir AI asistanın katmanlı (layered) mimariyle yazılmış bir yazılım
projesini analiz edip proje mimarisini kolayca anlaşılır ve sürdürülebilir
hale getirecek seviyede detaylı Markdown dokümantasyonu üretmesi için
izlemesi gereken kuralları tanımlar.

## Ne Zaman Kullanılır

Kullanıcı bir uygulamanın/çözümün katmanlarını (örn. Core, Contracts, Data,
Infrastructure, Api, Web ya da benzer bir katmanlama) dokümante etmeni
istediğinde bu kuralları uygula.

## Kurallar

### 1. Katman Bazlı Klasörleme

- Uygulamayı katman bazında detaylı olarak analiz et.
- `docs/` klasörü altında, **her katman için katman adıyla ayrı bir klasör**
  oluştur (örn. `docs/AICenter.Core/`, `docs/AICenter.Api/`).
- Klasör yapısı, ilgili katmanın kaynak kod klasör/namespace yapısını
  mirror'lamalıdır (örn. `Entities/`, `Enums/`, `Services/` alt klasörleri).

### 2. Bileşen Analizi

Her katmanda bulunan şu bileşenleri incele:

- Class'lar
- Interface'ler
- Enum'lar
- Method'lar
- Diğer bileşenler (record, DTO, middleware, extension method vb.)

### 3. Her Class İçin Ayrı Dosya

- **Her class/interface/enum için ayrı bir Markdown (.md) dosyası oluştur.**
  Birden fazla bileşeni tek dosyada birleştirme.
- Dosya adı, bileşenin adıyla birebir eşleşmeli (örn. `SkillUploadService.md`).

### 4. Her Dosyada Bulunması Gereken İçerik

Her `.md` dosyası şu başlıkları eksiksiz içermelidir:

1. **Ne işe yaradığı** — Bileşenin temel işlevinin kısa, net açıklaması.
2. **Hangi amaçla kullanıldığı** — Sistemde hangi senaryoda/akışta devreye girdiği.
3. **Sorumlulukları** — Bileşenin üstlendiği ve üstlenmediği işler (tek
   sorumluluk ilkesine referansla).
4. **Diğer katman ve bileşenlerle ilişkileri** — Hangi interface'i implemente
   ettiği, hangi bileşenleri DI ile inject ettiği/edildiği, hangi katmanlara
   bağımlı olduğu veya hangi katmanlardan çağrıldığı.
5. **Kullanılma nedeni ve tasarım yaklaşımı** — Neden bu şekilde tasarlandığı,
   hangi mimari prensibe hizmet ettiği (DRY, Repository Pattern, thin
   controller/service ayrımı vb.).
6. **Metotlar / Üyeler** — Her public metot veya property için imza ve
   1-2 cümlelik açıklama.
7. **Bağımlılıklar** — Constructor injection ile alınan servisler/repository'ler.

### 5. Genel Kalite Kriteri

- Dokümantasyon, projeye hiç aşina olmayan bir geliştiricinin mimariyi
  kod okumadan anlayabileceği seviyede detaylı olmalı.
- Aynı bilgi birden fazla dosyada tekrar edilmemeli; ilişkili bileşenlere
  bağlantı (relative markdown link) verilmeli.
- Her katman klasöründe, o katmandaki tüm dosyalara link veren bir
  `README.md` (özet/indeks) bulunmalı.
