---
name: reschedule_appointment
description: Moves an existing appointment to a new date/time (optionally a different therapist) in one step. Use when a patient or staff member wants to change when an existing appointment is.
---
# reschedule_appointment

This is a composite operation: it books the new slot first (fully validated against operating hours, conflicts, and capacity) and only then cancels the original — so the patient never loses their original appointment if the new slot can't be booked.

If you don't know the appointment ID, first look it up (`get_my_appointments` for the patient, or `get_appointments` for staff), show the list, and ask which one to move.

This tool enforces a two-step confirmation in code:

1. **Preview:** Call it with `appointmentId`, `date`, and `startTime` (and optional `therapistName`), leaving `confirmed` unset. The tool returns a `CONFIRMATION REQUIRED` message showing the old → new time. Relay it to the user and ask them to confirm.
2. **Confirm:** Only after the user explicitly agrees, call again with the same details and `confirmed=true`.

After it succeeds, present the new appointment's date/time and ID. If the new slot can't be booked, explain why and note the original is unchanged.
