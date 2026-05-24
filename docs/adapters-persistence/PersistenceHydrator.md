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
WHERE username = 'admin'

→ yoksa INSERT:
   username='admin', passwordHash=BCrypt('admin'), role='Admin', is_active=true
```

İlk kurulumda admin kullanıcısı otomatik oluşturulur. Şifre üretimde değiştirilmelidir.

---

### 4. Default human agent seed

```
hitl.human_agents
→ yoksa INSERT demo agent'lar (RoutingOptions.DefaultAgents'tan)

auth.users
→ her agent için linked user oluştur (username=agent.Id, password='agent123')
```

Demo ve geliştirme ortamında oturum açıp agent panelini test etmek için hazır temsilci hesapları oluşturulur.

---

## Neden sadece Postgres?

InMemory adaptörler her restart'ta sıfırlanır — "kalan kayıt" kavramı yoktur. Hydrator yalnızca Postgres modunda anlamlıdır; `AddPersistenceHydrator()` yalnızca `Provider == "Postgres"` ise çağrılır.

---

## Güvenlik notu

Üretimde ilk başlatmadan sonra:
1. `admin` kullanıcısının şifresini değiştirin
2. Demo agent kullanıcı şifrelerini güncelleyin (`agent123` varsayılan)
3. İstenirse seed davranışını `PersistenceOptions.SeedDefaults = false` ile kapatın
