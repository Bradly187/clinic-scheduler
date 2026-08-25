# Data Retention Policy

**Last Updated:** [DATE]

This policy establishes retention periods for data maintained by ClinicScheduler, in accordance with HIPAA requirements and applicable state laws.

---

## 1. Retention Schedule

| Data Type | Retention Period | Basis |
|-----------|-----------------|-------|
| Patient health records | 6 years after last date of service | HIPAA minimum; state law may require longer |
| Appointment records | 6 years after appointment date | Part of patient health record |
| Treatment plans | 6 years after plan end date | Part of patient health record |
| Clinical notes | 6 years after creation | Part of patient health record |
| Invoices and payments | 7 years after transaction date | IRS record-keeping requirements |
| Insurance claims/superbills | 7 years after generation | IRS + payer audit requirements |
| Audit logs | 6 years | HIPAA Security Rule documentation |
| Application logs | 14 days (rolling) | Operational troubleshooting |
| User accounts | Until deletion requested + 30-day grace period | Operational need |
| Backups | 30 days (encrypted, rolling) | Disaster recovery |
| Breach documentation | 6 years from last action date | HIPAA Breach Notification Rule |

---

## 2. PHI in Minors' Records

For patients who are minors, the retention clock starts at either:
- 6 years after last date of service, OR
- Until the patient reaches age of majority + 3 years

Whichever is longer.

---

## 3. Destruction Methods

| Data Location | Destruction Method |
|--------------|-------------------|
| PostgreSQL database | Logical deletion (soft-delete) with hard purge after retention expires |
| Encrypted fields | Key destruction renders data unrecoverable |
| Application logs | Automatic rotation (14-day TTL) |
| Backup storage | Automatic expiration per backup retention policy |
| Paper records (if any) | Cross-cut shredding |
| Portable media | NIST SP 800-88 compliant wiping or physical destruction |

---

## 4. Retention Exceptions

Records may be retained beyond the standard period when:
- Litigation hold is in effect
- Regulatory investigation is ongoing
- Patient has an active complaint or appeal
- State law mandates a longer retention period

---

## 5. Patient Requests for Deletion

Under HIPAA, covered entities are generally NOT required to delete PHI upon patient request (unlike GDPR). However:
- Patients may request restriction of certain uses
- De-identified data may be retained indefinitely
- If the clinic voluntarily agrees to delete, document the request and confirmation

---

## 6. Backup Verification

| Activity | Frequency |
|----------|-----------|
| Backup integrity check | Weekly (automated) |
| Restore test | Quarterly |
| Encryption verification | Monthly |
| Offsite backup rotation | Per cloud provider SLA |

---

## 7. Review

This policy shall be reviewed annually and updated as regulations or organizational needs change.
