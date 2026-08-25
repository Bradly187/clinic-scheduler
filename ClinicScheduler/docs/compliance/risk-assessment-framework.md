# Risk Assessment Framework

**Last Updated:** [DATE]

This framework establishes the process for conducting periodic security risk assessments as required by the HIPAA Security Rule (45 CFR 164.308(a)(1)(ii)(A)).

---

## 1. Purpose

Identify and assess risks to the confidentiality, integrity, and availability of electronic Protected Health Information (ePHI) maintained by ClinicScheduler.

---

## 2. Scope

All systems, networks, and processes that create, receive, maintain, or transmit ePHI:
- Web application (Blazor UI + REST API)
- PostgreSQL database
- External integrations (Stripe, Twilio, SMTP, FHIR)
- Cloud infrastructure (compute, storage, networking)
- Administrative processes (user provisioning, backups)

---

## 3. Asset Inventory

| Asset | PHI Content | Location |
|-------|------------|----------|
| Patient records | Name, DOB, email, phone, notes | PostgreSQL (Patients table) |
| Appointment data | Patient-therapist associations, times, clinical notes | PostgreSQL (Appointments table) |
| Treatment plans | Therapy assignments, frequency, duration | PostgreSQL (TreatmentPlans table) |
| Invoices | Service details, billing codes, amounts | PostgreSQL (Invoices table) |
| Insurance policies | Policy numbers, subscriber IDs | PostgreSQL (InsurancePolicies table) |
| Audit logs | User actions on PHI | PostgreSQL (AuditLogs table) |
| Email/SMS content | Appointment reminders, notifications | In transit only (not persisted) |
| Application logs | May contain user identifiers | File system (14-day rotation) |

---

## 4. Threat Identification (STRIDE)

| Threat | Category | Example Scenario |
|--------|----------|-----------------|
| Unauthorized access to patient data | Spoofing | Credential stuffing attack on login |
| Data modification without detection | Tampering | SQL injection altering appointment records |
| Denied actions without accountability | Repudiation | User claims they didn't cancel an appointment |
| PHI exposure to unauthorized parties | Information Disclosure | API returns data for wrong patient |
| System unavailability during patient care | Denial of Service | DDoS on appointment booking |
| Unauthorized privilege escalation | Elevation of Privilege | Patient role accessing admin functions |

---

## 5. Risk Matrix

| Likelihood | Impact: Low | Impact: Medium | Impact: High |
|-----------|-------------|----------------|--------------|
| High | Medium | High | Critical |
| Medium | Low | Medium | High |
| Low | Low | Low | Medium |

---

## 6. Current Mitigations

| Risk | Mitigation | Control Reference |
|------|-----------|------------------|
| Credential stuffing | Rate limiting (10/min/IP), account lockout, 2FA | Program.cs rate limiter, Identity config |
| SQL injection | Parameterized queries via EF Core | All data access through Repository/DbContext |
| Unauthorized data access | Role-based authorization on every endpoint | [Authorize] attributes, RoleNames.cs |
| Data tampering | Audit trail with user attribution | ClinicDbContext.SaveChangesAsync() |
| PHI in transit exposure | TLS 1.2+, HSTS, secure cookies | Program.cs HSTS/cookie configuration |
| PHI at rest exposure | AES-256 encryption on sensitive fields | EncryptedStringConverter in DbContext |
| Session hijacking | HttpOnly cookies, SameSite, sliding expiration | Cookie configuration in Program.cs |
| Privilege escalation | Role validation on every request, tenant isolation | Authorization middleware, ClinicId claims |

---

## 7. Residual Risk Acceptance

Residual risks that cannot be fully mitigated must be documented and accepted by the clinic's HIPAA Security Officer:
- Risk of authorized user misusing their access level
- Risk of zero-day vulnerabilities in third-party dependencies
- Risk of social engineering bypassing technical controls

---

## 8. Assessment Schedule

| Activity | Frequency |
|----------|-----------|
| Full risk assessment | Annually |
| Vulnerability scanning | Quarterly |
| Penetration testing | Annually (or after major changes) |
| Access review | Quarterly |
| Incident review | After each incident |
| Policy review | Annually |
