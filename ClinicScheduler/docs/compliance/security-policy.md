# Security Policy — Technical Safeguards

**Last Updated:** [DATE]

This document describes the technical, administrative, and physical safeguards implemented in ClinicScheduler to protect Protected Health Information (PHI) in accordance with the HIPAA Security Rule (45 CFR Part 164, Subpart C).

---

## 1. Encryption

### At Rest
- Sensitive entity fields (Patient.Phone, Patient.Notes, Appointment.Notes, AppointmentRequest.Notes, WaitlistEntry.Notes) are encrypted using AES-256 via ASP.NET Core Data Protection API with `EncryptedStringConverter`
- Implementation: `ClinicScheduler.Infrastructure/Data/ClinicDbContext.cs` (OnModelCreating — HasConversion with EncryptedStringConverter)
- Database: PostgreSQL with filesystem-level encryption on managed cloud deployments

### In Transit
- TLS 1.2+ enforced for all connections
- HSTS enabled with 1-year max-age, subdomains included (`Program.cs` — AddHsts)
- Security headers: X-Content-Type-Options, X-Frame-Options, Referrer-Policy
- HTTP automatically redirected to HTTPS in production

---

## 2. Authentication and Access Control

### Identity Management
- ASP.NET Core Identity with password policy enforcement:
  - Minimum 8 characters (10 in production)
  - Requires digit, uppercase, and non-alphanumeric character
  - Account lockout after repeated failed attempts
- Implementation: `Program.cs` — AddIdentity configuration

### Multi-Factor Authentication
- TOTP-based 2FA with authenticator apps
- Recovery codes for account recovery
- Implementation: `AccountController.cs` — LoginTwoFactor endpoint, `TwoFactorSetup.razor`

### Role-Based Access Control
- 6 roles: Admin, ClinicManager, Staff, Therapist, Patient, Auditor
- Composite policies: StaffOrAbove, AdminOrManager, AdminOrAuditor
- Implementation: `RoleNames.cs`, `[Authorize(Roles = ...)]` attributes on controllers

### Session Management
- Cookie-based sessions with 8-hour sliding expiration
- HttpOnly, SameSite=Lax, Secure cookie policy
- JWT Bearer tokens for API clients (HS256, 30-second clock skew)
- Dual authentication scheme: CookieOrJwt policy selector

### Tenant Isolation
- Clinic claim stamped on the identity principal via `ClinicClaimsPrincipalFactory`
- Per-entity ClinicId for data isolation (multi-tenant architecture)

---

## 3. Audit Logging

### Entity-Level Audit Trail
- Every Create, Modify, Delete operation on clinical entities is automatically logged
- Captures: entity name, entity ID, action, change summary, user ID, timestamp
- Sensitive field values are REDACTED in change summaries
- Implementation: `ClinicDbContext.SaveChangesAsync()` override with `CreateAuditLogEntries()`
- Excluded from audit: Identity framework tables (roles, claims, tokens), AuditLog itself, Notifications

### Access Logging
- Structured logging via Serilog with compact JSON format
- File rotation: 14-day retention, 50MB per file, rolling on size
- Log levels configurable per namespace

### Observability
- OpenTelemetry tracing: ASP.NET Core, HttpClient, EF Core, custom business logic
- Prometheus metrics: appointment scheduling, billing, capacity rejections
- OTLP export when endpoint configured

---

## 4. Network Security

### Rate Limiting
- Login endpoints: 10 requests per minute per IP (fixed window)
- Booking chat: Rate-limited to prevent abuse
- Implementation: `Program.cs` — AddRateLimiter with "login" policy

### CORS
- Production: Only explicitly configured origins (AllowedOrigins in appsettings)
- Development: Permissive for local testing

### Forwarded Headers
- ALB/reverse proxy support: X-Forwarded-For, X-Forwarded-Proto
- Implementation: `ForwardedHeadersOptions` in Program.cs

---

## 5. Data Protection

### Optimistic Concurrency
- PostgreSQL `xmin` system column used as row version on Appointment, Patient, Therapist, TreatmentPlan, Invoice
- Prevents lost updates from concurrent modifications

### Input Validation
- Data annotations on all API request models (Required, Range, StringLength)
- Domain entity constructors validate business rules
- Parameterized queries via EF Core (SQL injection prevention)

### Backup and Recovery
- Database: PostgreSQL managed service with point-in-time recovery
- Application: Containerized deployment with infrastructure-as-code
- Auto-migration on startup ensures schema consistency

---

## 6. Vulnerability Management

### Dependencies
- NuGet packages pinned to specific versions
- Regular dependency audits recommended (dotnet list package --vulnerable)

### Security Headers
- X-Content-Type-Options: nosniff
- X-Frame-Options: DENY
- Referrer-Policy: strict-origin-when-cross-origin

---

## 7. Incident Response

See `breach-notification-plan.md` for the full incident response procedure.

---

## 8. Review Schedule

This security policy and its referenced implementations shall be reviewed:
- Annually (minimum)
- After any security incident
- After significant architectural changes
- Before deployment to new environments
