---
name: get_my_treatment_plan
description: Retrieves a patient's current treatment plan (frequency, duration, therapist, therapies, status). Use when a patient asks about their plan, or staff want to review a patient's plan.
---
# get_my_treatment_plan
Patients automatically see their own plan — do not provide `patientName`. Staff/Admin may pass a `patientName` to view a specific patient's plan. Present the returned details clearly (frequency, total sessions, date range, therapist, therapies, status). If there is no plan, tell the user.
