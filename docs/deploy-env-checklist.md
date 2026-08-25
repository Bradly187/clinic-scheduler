# Public Demo / Deploy Environment Checklist

**Purpose:** confirm the public EC2 demo box is not running on the committed default
credentials. Several defaults in [`docker-compose.yml`](../docker-compose.yml) are public (they're
in this repo), so any of them left unset on a public box is an exposure. Work top-down; the 🔴
items are the ones that let a stranger take over the demo.

> Replace `DEMO_HOST` with the demo's hostname/IP in the commands below. These checks are
> read-only and safe to run against your own box.

## Environment variables that MUST be overridden

| Variable | Committed default if unset | Risk if left default |
|---|---|---|
| `SEED_ADMIN_PASSWORD` → `SeedAdmin__Password` | `Admin@1986` | **Anyone can log in as `admin@clinic.com`.** |
| `Jwt__SigningKey` | dev fallback `dev-only-…-changeme` (used only outside Production) | **Anyone can forge admin JWTs.** |
| `POSTGRES_PASSWORD` | `devpassword` | DB reachable with known creds **if port 5432 is open**. |
| `GEMINI_API_KEY` → `Gemini__ApiKey` | empty | AI chat silently fails (functional, not security). |
| `ASPNETCORE_ENVIRONMENT` | `Production` (compose default) | If **not** Production, the public dev JWT key is used → forgeable tokens. |

## 🔴 Critical checks

### 1. Seed admin is not on the default password
```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST https://DEMO_HOST/api/auth/token \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@clinic.com","password":"Admin@1986"}'
```
- `401` → good, the default was overridden.
- `200` → **the public default password is live. Rotate immediately** (see "Rotating the admin
  password" — note that changing the env var alone does **not** fix an already-seeded admin).

### 2. Production env + a real JWT signing key
The app **throws on startup in Production if `Jwt__SigningKey` is unset** — so if it's up in
Production, a key is set. Confirm both:
```bash
# Should report Production
ssh ec2-user@DEMO_HOST 'grep -E "ASPNETCORE_ENVIRONMENT|Jwt__SigningKey" ~/clinic-scheduler/.env'
```
- Ensure `ASPNETCORE_ENVIRONMENT=Production` **and** `Jwt__SigningKey` is a long random value
  (≥ 32 bytes, e.g. `openssl rand -base64 48`), not the dev fallback.
- If the box runs in Development/Staging, it is using the **public** dev signing key — switch to
  Production with a real key.

### 3. PostgreSQL is not publicly reachable with default creds
`docker-compose.yml` publishes `5432:5432`. On a public box that's only safe if the security group
blocks 5432 **and/or** the password is non-default.
```bash
# From your machine (NOT the box) — should hang/refuse, not connect:
nc -zv DEMO_HOST 5432
```
- Connection refused/timeout → good (port closed at the security group).
- Open → ensure `POSTGRES_PASSWORD` is non-default, then preferably remove the public `5432`
  mapping (the app reaches the DB over the internal Docker network regardless).

## 🟡 Secondary checks

- **HTTPS on:** confirm the site serves over TLS and `Security__RequireHttps=true` (or TLS
  terminates at a load balancer / reverse proxy in front).
- **Health endpoint reachable:** `curl -s -o /dev/null -w "%{http_code}\n" https://DEMO_HOST/health/live` → `200`.
- **Jaeger UI not exposed:** compose publishes `16686`. Confirm the security group blocks it, or it's
  bound to localhost — otherwise your traces are publicly browsable.
- **Synthetic data only:** the seeder uses fake demo patients — confirm no real PHI was ever entered
  into the public box.
- **DataProtection keys persist:** keys live in the `clinic_keys_data` volume. Don't wipe it — losing
  the keys makes the encrypted PHI columns (patient notes/phone, etc.) unreadable and breaks
  antiforgery/auth cookies.

## Rotating the admin password

⚠️ **Important gotcha:** `DatabaseSeeder` only *creates* the admin if it's missing — it does **not**
reset an existing admin's password. So if the box already seeded `admin@clinic.com` with
`Admin@1986`, changing `SEED_ADMIN_PASSWORD` and restarting will **not** change it. To rotate:

1. Change the password in-app while signed in as admin (Account → change password), **or**
2. Reset it administratively (e.g. a one-off `UserManager` script / `dotnet` admin tool), **or**
3. As a last resort on a throwaway demo: set a strong `SEED_ADMIN_PASSWORD`, delete the existing
   `admin@clinic.com` row, and restart so the seeder recreates it with the new password.

After rotating, re-run check #1 and confirm it returns `401` for `Admin@1986`.
