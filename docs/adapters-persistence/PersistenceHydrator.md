# PersistenceHydrator

**Dosya:** `EfCore/PersistenceHydrator.cs`  
**Implements:** `IHostedService`  
**Yaşam döngüsü:** Singleton (Postgres modunda kayıtlı)

## Ne yapar?

Uygulama başladığında çalışan startup kurtarma servisi. Önceki çalışmadan kalan tutarsız kayıtları temizler ve demo verilerini seed eder.

---

## Çalışma zamanı

`StartAsync(CancellationToken)` uygulama başlatma sırasında çağrılır. `StopAsync` işlem yapmaz.

---

## Adımlar

### 1. Stale pending approvals expire

```
hitl.approval_requests
WHERE status = 'Pending'
  AND requested_at < NOW() - INTERVAL '10 seconds'

→ status = 'Expired', decided_at = NOW(), decided_by = 'system'
```

Önceki çalışmada onay isteği gönderilmiş ama yanıt alınmadan uygulama kapanmışsa bu kayıtlar sonsuza kadar Pending kalır. 10 saniyelik eşik, yeni başlangıçta bunları temizler.

---

### 2. In-flight traces terminate

```
observability.reasoning_traces
WHERE completed_at IS NULL

→ termination_reason = 'terminated_by_restart'
   error = 'terminated_by_restart'
   completed_at = NOW()
```

Restart sırasında aktif olan reasoning akışları tamamlanmadan kesilmiştir. Bu step onları kapalı olarak işaretler; istatistikler bozulmaz.

---

### 3. Default admin seed

```
auth.users
WHERE username = @username  (varsayılan: 'admin')

→ yoksa INSERT:
   username='admin', passwordHash=BCrypt('Admin123!'), role='Admin', is_active=true
```

Kullanıcı adı `Auth:DefaultAdminUsername`, şifre `Auth:DefaultAdminPassword` konfigürasyonundan okunur. Tanımlı değilse varsayılanlar: `admin` / `Admin123!`.

---

### 4. Default human agent seed

```
hitl.human_agents
→ tablo boşsa 2 demo agent INSERT:
   - agent-jdoe (John Doe, john.doe@example.com, skills: complaint/refund/vip)
   - agent-jsmith (Jane Smith, jane.smith@example.com, skills: order/product/enterprise)

auth.users
→ her agent için linked user oluştur
   username = email.Split('@')[0]  (john.doe, jane.smith)
   password = Auth:DefaultAgentPassword ?? 'Agent123!'
   role = 'Agent', linked_agent_id = agent.Id
```

Demo ve geliştirme ortamında oturum açıp agent panelini test etmek için hazır temsilci hesapları oluşturulur.

---

### 5. Categories seed

```
catalog.categories
→ tablo boşsa 21 demo kategori INSERT (NorthwindSeedData)
```

---

### 6. Customers seed

```
catalog.customers
→ tablo boşsa 29 demo müşteri INSERT (NorthwindSeedData)
→ sequence reset: setval(pg_get_serial_sequence('catalog.customers', 'id'), MAX(id))
```

---

### 7. Products seed

```
catalog.products
→ tablo boşsa 36 demo ürün INSERT (NorthwindSeedData)
→ sequence reset: setval(pg_get_serial_sequence('catalog.products', 'id'), MAX(id))
```

---

### 8. Orders + OrderDetails seed

```
catalog.orders
→ tablo boşsa 48 demo sipariş INSERT (NorthwindSeedData)
→ sequence reset: setval(pg_get_serial_sequence('catalog.orders', 'code'), MAX(code))

catalog.order_details
→ tablo boşsa sipariş detayları INSERT (NorthwindSeedData)
```

---

### 9. Complaints seed

```
catalog.complaints
→ tablo boşsa 5 demo şikayet INSERT (NorthwindSeedData)
```

Complaints tablosu `ValueGeneratedNever()` kullanır — `Code` alanı seed verilerinde açıkça atanır, sequence yoktur.

### 10. Workflow definitions seed

```
workflow.workflow_definitions
→ SeedDefaultWorkflowsAsync: 3 örnek WorkflowDefinition
   - "siparis-durumu"
   - "urun-kategori-listesi"
   - "urun-bilgisi"
```

Her başlangıçta çalışır (tabloya özgü boş-kontrolü yok). Her örnek için: kayıt hiç yoksa **veya** son güncelleyen `UpdatedBy == "system"` ise upsert edilir. Bir admin bu workflow'lardan birini panelden düzenlerse `UpdatedBy` artık `"system"` olmadığından, sonraki restart'larda üzerine yazılmaz.

---

## Neden sadece Postgres?

InMemory adaptörler her restart'ta sıfırlanır — "kalan kayıt" kavramı yoktur. Hydrator yalnızca Postgres modunda anlamlıdır; `AddPersistenceAdapters()` çağrısında `PersistenceHydrator` otomatik kayıt edilir.

---

## Güvenlik notu

Üretimde ilk başlatmadan sonra:
1. `Auth:DefaultAdminPassword` yapılandırma değerini değiştirin (varsayılan: `Admin123!`)
2. `Auth:DefaultAgentPassword` yapılandırma değerini değiştirin (varsayılan: `Agent123!`)
3. Agent kullanıcı adları: `john.doe`, `jane.smith`
