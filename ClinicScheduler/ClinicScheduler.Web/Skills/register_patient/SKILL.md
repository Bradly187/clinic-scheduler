---
name: register_patient
description: Registers a new patient or updates an existing patient's demographics and SMS consent. Staff/Admin can register or update any patient; a patient can update their own record but cannot create a new one.
---
# register_patient

Use this tool during intake to create a new patient record or update an existing one. Required fields: `firstName`, `lastName`, and `dateOfBirth` (YYYY-MM-DD). `email` identifies the record for Staff/Admin; `phone` and `smsConsent` are optional.

**Role-based behavior:**
- **Patient role:** Updates only your own record (matched to your login). You cannot create a new patient.
- **Staff / Admin role:** Provide `email`. If a patient with that email exists you update them; otherwise a new patient is created.

**Before calling this tool:**
1. Collect the patient's details conversationally (name, date of birth, contact info).
2. Read the details back to the user and ask them to confirm they are correct.
3. For SMS reminders, only set `smsConsent=true` if the patient has explicitly agreed (TCPA).
4. Call the tool only after the user confirms.

**After the tool responds:**
- On success, confirm the registered/updated details (including the new patient ID) to the user, and offer the next intake step (e.g. opening an encounter or scheduling an appointment).
- On error, explain what was missing or ambiguous and ask for the correction.
