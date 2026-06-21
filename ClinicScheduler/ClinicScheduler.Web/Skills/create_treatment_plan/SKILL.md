---
name: create_treatment_plan
description: Creates a treatment plan for a patient (Staff/Admin only). Use when staff want to set up a recurring course of therapy for a patient.
---
# create_treatment_plan
Staff/Admin only. Collect: `patientName`, `therapistName`, `frequencyPerWeek` (2, 3, or 4), `totalDays` (20, 30, or 50 sessions), `startDate` (YYYY-MM-DD), and optionally a `therapyTypeName`.

Confirm the details with the user before creating. After it succeeds, report the plan ID and schedule, and offer to book the session series with `generate_plan_appointments`.
