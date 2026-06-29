---
name: verify_patient_demographics
description: Reads back a patient's current demographics (name, email, phone, date of birth, SMS consent) so they can be confirmed before changes. Patients see their own record; Staff/Admin can look up by patientName or email.
---
# verify_patient_demographics

Use this read-only tool to confirm a patient's information on file before registering changes or opening an encounter.

**Role-based behavior:**
- **Patient role:** Returns your own record automatically — no parameters needed.
- **Staff / Admin role:** Provide `patientName` or `email` to look up a specific patient.

**After the tool responds:**
- Present the returned demographics clearly and ask the user whether anything needs updating.
- If something is wrong, hand off to `register_patient` to correct it (after confirmation).
