# Kimlik Doğrulama ve Zeka (Intelligence) Uç Noktaları

- **Kaynaklar:**
  - `CustomerSupportBot.Api/Endpoints/AuthEndpoints.cs`
  - `CustomerSupportBot.Api/Endpoints/MemoryEndpoints.cs`
  - `CustomerSupportBot.Api/Endpoints/PersonalizationEndpoints.cs`
  - `CustomerSupportBot.Api/Endpoints/ImprovementsEndpoints.cs`
- **Namespace:** `CustomerSupportBot.Api.Endpoints`

## 1. Ne İşe Yarar

Dört farklı "zeka" bileşenini HTTP'ye açar: JWT tabanlı kimlik doğrulama (staff + müşteri),
semantik hafıza (`Qdrant`) dashboard/debug uçları + bilgi tabanı makale CRUD'u, müşteri
kişiselleştirme profilleri ve kendi kendini iyileştiren (self-improving) ders/öneri döngüsü.

## 2. Hangi Amaçla Kullanılır

- **AuthEndpoints:** Admin/Agent panel girişleri (`/auth/login`) ve **müşteri chat girişi**
  (`/auth/customer/login`, `/auth/customer/register`) — ikisi de aynı `ITokenService.IssueAsync`
  ile JWT üretir, farkı hangi `IUserService`/`ICustomerAuthService` ile doğrulandığıdır.
- **MemoryEndpoints:** Admin panelinde "hafıza" sekmesinin arkasındaki veri — kaç doküman
  indekslendi, belirli bir sorgu için ne bulunuyor, KB'yi elle yeniden tarama, bilgi tabanı
  makalelerinin panelden düzenlenmesi.
- **PersonalizationEndpoints:** Bir müşterinin geçmiş davranışından çıkarılan profilin (iletişim
  tarzı tercihi, sık sorulan konular vb.) admin tarafından görüntülenmesi/yenilenmesi.
- **ImprovementsEndpoints:** Geçmiş konuşmalardan otomatik çıkarılan "ders" (lesson) önerilerinin
  admin tarafından onaylanıp prompt'lara/davranışa dahil edilmesi.

## 3. Sorumlulukları

- Yalnızca HTTP yüzeyi + girdi doğrulama (`MemoryEndpoints.Validate` gibi) + ilgili port'a
  delegasyon.
- `AuthEndpoints`: **JWT token üretimi/yenileme/iptalini** tetikler ama token'ın kendisini
  imzalamaz — bu iş `ITokenService`/`JwtAccessTokenProvider`'ındır.
- `MemoryEndpoints`: makale CRUD'unda düzenleyen kimliğini (`ActorOf`) **her zaman doğrulanmış
  `ClaimsPrincipal`'dan** okur, istek gövdesinden değil — audit trail sahteciliğini engellemek
  için.
- **Üstlenmediği:** embedding üretimi, vektör arama, ders madenciliği algoritması — bunlar
  Application/Adapters katmanlarında.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IUserService`, `ITokenService`, `ICustomerAuthService` — `CustomerSupportBot.Application.Ports.Inbound.Auth`.
  `ICustomerAuthService` müşteri e-posta+şifre kaydı/girişini yönetir; `IUserService` staff
  (Admin/Agent) girişini yönetir — **iki ayrı kullanıcı havuzu**, aynı token altyapısını paylaşır.
- `IMemoryPort`, `IKnowledgeBasePort` — semantik hafıza ve bilgi tabanı port'ları.
- `IPersonalizationPort`, `IImprovementsPort` — kişiselleştirme ve öz-iyileştirme port'ları.
- [Models/Auth/AuthDtos](../Models/AuthDtos.md) — `LoginRequest`, `RefreshRequest`,
  `CustomerRegisterRequest`, `CustomerLoginRequest` gibi istek/yanıt kayıtları.
- `Program.cs` — `/auth/login|refresh|customer/*` `AllowAnonymous`, `/auth/logout`
  `RequireAuthorization()`; hepsi `RequireRateLimiting("auth")` politikasına tabidir (kaba kuvvet
  girişim denemelerini sınırlamak için — bkz. [AuthServicesExtensions](../Extensions/AuthServicesExtensions.md)).
  Diğer üç sınıf admin/agent yetki gruplarına dahildir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

- **Müşteri auth'u, staff auth'undan AYRI bir servis (`ICustomerAuthService`) ve ayrı route
  grubu (`/auth/customer/*`) olarak tasarlandı**, tek bir `IUserService`'e role parametresi
  eklenerek DEĞİL — böylece müşteri kaydı/girişi yetkilendirme politikaları (Admin/Agent
  rolleriyle karışma riski) ve doğrulama kuralları (email format, şifre politikası) staff
  tarafından tamamen bağımsız evrilebilir.
- **`auth` rate-limit policy'si tüm `/auth` grubuna uygulanır** (login dahil customer uçları da)
  — kimlik doğrulama uçları kaba kuvvet saldırısına en açık yüzeydir.
- **Makale düzenleyen kimliği gövdeden değil `ClaimsPrincipal`'dan okunur:** aksi halde bir
  istemci "düzenleyen: admin" diye sahte bir isim gönderip audit log'u yanıltabilirdi.
- **Başlık/kategori uzunluk sınırları (`TitleMaxLength`, `CategoryMaxLength`) endpoint
  seviyesinde de kontrol edilir**, yalnızca veritabanı kolonuna güvenilmez — aksi halde sınırı
  aşan bir istek, anlaşılır bir `400` yerine ham bir DB hatasına düşerdi.

## 6. Metotlar / Üyeler

### `AuthEndpoints` (`/auth`, `RequireRateLimiting("auth")`)

| Route | Açıklama |
|---|---|
| `POST /auth/login` *(anon)* | Staff girişi: `LoginRequest { Username, Password }` → `IUserService.AuthenticateAsync` → JWT. |
| `POST /auth/refresh` *(anon)* | `RefreshRequest { RefreshToken }` ile access token yeniler. |
| `POST /auth/logout` *(auth gerekir)* | Refresh token'ı iptal eder. |
| `POST /auth/customer/register` *(anon)* | `CustomerRegisterRequest { Email, Password, CustomerId }` → `ICustomerAuthService.RegisterAsync` → JWT. |
| `POST /auth/customer/login` *(anon)* | `CustomerLoginRequest { Email, Password }` → JWT. |

### `MemoryEndpoints` (`/memory`)

| Route | Açıklama |
|---|---|
| `GET /memory/stats` | Her koleksiyon (episodic/lesson/knowledge) için nokta sayısı + config. |
| `GET /memory/search?kind=&q=&topK=` | Semantik arama; `kind` = `episodic\|lesson\|knowledge`. |
| `POST /memory/ingest` | Bilgi tabanını elle yeniden tarar/indeksler. |
| `GET /memory/articles` \| `/{id}` | Bilgi tabanı makalelerini listeler/tekil görüntüler. |
| `POST /memory/articles` | Yeni makale oluşturur (`KnowledgeArticleInput`), otomatik indekslenir. |
| `PUT /memory/articles/{id}` | Makaleyi günceller, yeniden indeksler. |
| `DELETE /memory/articles/{id}` | Makaleyi siler. |

### `PersonalizationEndpoints` (`/customers`)

| Route | Açıklama |
|---|---|
| `GET /customers?take=` | Tüm müşteri profillerini listeler. |
| `GET /customers/{id}/profile` | Tek müşteri profili. |
| `POST /customers/{id}/profile/refresh` | Profili geçmiş verilerden yeniden hesaplar. |
| `PUT /customers/{id}/profile/note` | Admin notu ekler/günceller. |
| `DELETE /customers/{id}/profile` | Profili siler. |

### `ImprovementsEndpoints` (`/improvements`)

| Route | Açıklama |
|---|---|
| `POST /improvements/mine` | Geçmiş konuşmalardan yeni ders/öneri madenciliği tetikler. |
| `GET /improvements?status=` | Dersleri (opsiyonel durum filtresiyle) listeler. |
| `GET /improvements/proposed` | Yalnızca `Proposed` durumundaki dersler. |
| `GET /improvements/{id}` | Tek ders. |
| `POST /improvements/{id}/approve` | Dersi onaylar. |
| `POST /improvements/{id}/reject` | Dersi reddeder. |

## 7. Bağımlılıklar

Constructor injection yok — her endpoint lambda'sı ilgili port'u (`IUserService`, `ITokenService`,
`ICustomerAuthService`, `IMemoryPort`, `IKnowledgeBasePort`, `IPersonalizationPort`,
`IImprovementsPort`) minimal API parametre injection'ı ile alır.

## Bağlantılar

- [../README.md](../README.md) — Katman indeksi
- [Admin ve Agent uç noktaları](AdminAndHitl.md)
