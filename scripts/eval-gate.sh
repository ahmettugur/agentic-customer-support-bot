#!/usr/bin/env bash
# Değerlendirme kapısı istemcisi: çalışan API'ye admin olarak girer, POST /eval/gate çağırır, özeti yazar.
# Tüm senaryolar koşar (gerçek LLM çağrıları — maliyetlidir); bu yüzden her PR'da değil, gece/elle çalıştırılır
# (bkz. .github/workflows/eval-gate.yml).
#
# Çıkış kodu: 0 = kapı geçti · 1 = kapı kaldı · 2 = altyapı sorunu (giriş/ağ/beklenmeyen yanıt)
#
# Ortam değişkenleri:
#   BASE_URL        (varsayılan http://localhost:5000)
#   ADMIN_USERNAME  (varsayılan admin)
#   ADMIN_PASSWORD  (zorunlu)
#   RESULT_FILE     (varsayılan eval-gate-result.json) — tam yanıt buraya yazılır
set -uo pipefail

BASE_URL="${BASE_URL:-http://localhost:5000}"
ADMIN_USERNAME="${ADMIN_USERNAME:-admin}"
RESULT_FILE="${RESULT_FILE:-eval-gate-result.json}"

if [[ -z "${ADMIN_PASSWORD:-}" ]]; then
  echo "ADMIN_PASSWORD tanımlı değil." >&2
  exit 2
fi

export ADMIN_USERNAME ADMIN_PASSWORD
# Gövde stdin'den verilir: parolada tırnak/özel karakter olsa da kabuk kaçışına takılmaz.
login_response=$(python3 -c 'import json,os; print(json.dumps({"username": os.environ["ADMIN_USERNAME"], "password": os.environ["ADMIN_PASSWORD"]}))' \
  | curl -sS -m 30 -w '\n%{http_code}' -H 'Content-Type: application/json' --data-binary @- "$BASE_URL/auth/login") || {
  echo "Giriş isteği başarısız (ağ): $BASE_URL" >&2; exit 2; }
login_code="${login_response##*$'\n'}"
login_json="${login_response%$'\n'*}"
if [[ "$login_code" != "200" ]]; then
  echo "Giriş başarısız (HTTP $login_code)." >&2
  exit 2
fi
token=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["accessToken"])' <<<"$login_json") || {
  echo "Giriş yanıtında accessToken yok." >&2; exit 2; }

# Tüm koşu uzun sürebilir (senaryo başına birden çok LLM çağrısı).
http_code=$(curl -sS -m "${GATE_TIMEOUT_SECONDS:-1800}" -o "$RESULT_FILE" -w '%{http_code}' \
  -X POST -H "Authorization: Bearer $token" "$BASE_URL/eval/gate") || {
  echo "/eval/gate isteği başarısız (ağ/zaman aşımı)." >&2; exit 2; }

if [[ "$http_code" != "200" && "$http_code" != "422" ]]; then
  echo "/eval/gate beklenmeyen yanıt verdi (HTTP $http_code):" >&2
  head -c 600 "$RESULT_FILE" >&2; echo >&2
  exit 2
fi

python3 - "$RESULT_FILE" <<'PY'
import json, sys
r = json.load(open(sys.argv[1]))
print(f"Kapı: {'GEÇTİ' if r['passed'] else 'KALDI'}  oran=%{r['passRate']*100:.0f}  "
      f"geçen={r['passing']}/{r['evaluated']} (toplam senaryo {r['totalScenarios']})")
for reason in r.get("reasons", []):
    print(f"  - {reason}")
bad = [s for s in r.get("scenarios", []) if s["status"] in ("failed", "error")]
for s in bad:
    detail = s.get("error") or ", ".join(s.get("failedCriteria") or [])
    print(f"  ✗ {s['scenarioId']} [{s['status']}] {detail}")
PY

[[ "$http_code" == "200" ]] && exit 0 || exit 1
