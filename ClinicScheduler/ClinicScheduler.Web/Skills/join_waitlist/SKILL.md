---
name: join_waitlist
description: Adds a patient to the waitlist for a date window so the system books the first matching opening automatically. Use when the patient wants a slot but none are currently available, or wants to be notified/booked when one frees up.
---
# join_waitlist
Collect the date window the patient can attend: `earliestDate` and `latestDate` (both YYYY-MM-DD, within 90 days of each other). Optionally collect a preferred therapist (`therapistName`), a preferred time range (`preferredTimeFrom` / `preferredTimeTo`, HH:MM), and `notes`.

- **Patient role:** the patient is added under their own account — do not provide `patientName`.
- **Staff / Admin role:** provide `patientName` to add a specific patient.

Confirm the window (and any preferences) with the user before calling. After it succeeds, tell the user they're on the waitlist and that the first matching opening will be booked automatically.
