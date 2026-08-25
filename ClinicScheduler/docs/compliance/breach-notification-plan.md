# Breach Notification Plan

**Last Updated:** [DATE]

This plan establishes the incident response procedure for suspected or confirmed breaches of Protected Health Information (PHI), in accordance with the HIPAA Breach Notification Rule (45 CFR Parts 164.400-414).

---

## 1. Definitions

- **Breach:** Acquisition, access, use, or disclosure of PHI in a manner not permitted under the Privacy Rule that compromises the security or privacy of the PHI.
- **Unsecured PHI:** PHI that is not rendered unusable, unreadable, or indecipherable to unauthorized persons through encryption or destruction.

---

## 2. Detection

### Automated Detection
- Audit log monitoring for unusual access patterns
- Application error alerts (Serilog + alerting)
- Database access anomaly detection
- Failed authentication alerts (rate limiter triggers)

### Manual Detection
- Staff reporting suspected unauthorized access
- Patient reporting unexpected communications
- Third-party notification (Stripe, hosting provider)

---

## 3. Containment (Immediate — within 1 hour of detection)

1. **Isolate the affected system** — disable compromised accounts, revoke API keys
2. **Preserve evidence** — snapshot audit logs, database state, application logs
3. **Assess scope** — determine what PHI was potentially affected
4. **Notify incident lead** — HIPAA Security Officer and IT lead

---

## 4. Investigation (within 48 hours)

1. **Determine if a Breach occurred** using the four-factor risk assessment:
   - Nature and extent of PHI involved (types of identifiers, clinical info)
   - Unauthorized person who used/accessed the PHI
   - Whether PHI was actually acquired or viewed
   - Extent to which risk has been mitigated

2. **Document findings** including:
   - Timeline of events
   - PHI involved (number of records, types of data)
   - Root cause analysis
   - Mitigation steps taken

---

## 5. Notification Timelines

| Recipient | Deadline | Method |
|-----------|----------|--------|
| Affected Individuals | 60 calendar days from discovery | Written notice (email + postal) |
| HHS (< 500 individuals) | 60 days after end of calendar year | HHS Breach Portal |
| HHS (>= 500 individuals) | 60 calendar days from discovery | HHS Breach Portal |
| Media (>= 500 in a state) | 60 calendar days from discovery | Press release to major media |
| Business Associates | 60 calendar days from discovery | Written notice per BAA |

---

## 6. Individual Notification Content

Notification to affected individuals must include:
- Description of what happened (dates, nature of breach)
- Types of PHI involved (name, DOB, diagnosis, SSN, etc.)
- Steps individuals should take to protect themselves
- What the organization is doing to investigate and prevent future breaches
- Contact information for questions (toll-free number, email, postal address)

---

## 7. Remediation

1. **Technical remediation** — patch vulnerability, update credentials, strengthen controls
2. **Policy updates** — revise security policies as needed
3. **Training** — re-train staff on updated procedures
4. **Monitoring** — enhanced monitoring for 90 days post-incident

---

## 8. Documentation Requirements

Maintain breach documentation for 6 years:
- Incident report with full timeline
- Risk assessment (four-factor analysis)
- Notification copies and delivery confirmations
- Remediation actions taken
- Policy changes resulting from the incident

---

## 9. Exceptions (Not a Breach)

The following do not constitute a Breach:
- Unintentional access by an authorized workforce member acting in good faith
- Inadvertent disclosure between authorized persons at the same covered entity
- Recipient unable to reasonably retain the information
- PHI that was properly encrypted at the time of the incident (Safe Harbor)
