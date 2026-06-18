---
name: schedule_appointment
description: Schedules a new appointment. Patients can book for themselves; Staff and Admin can book for any patient by providing a patientName.
---
# schedule_appointment

Use this tool to book a new appointment. Required fields: `therapistName`, `date` (YYYY-MM-DD), and `startTime` (HH:MM in 24-hour format).

**Role-based behavior:**
- **Patient role:** You are automatically booked under your own account. Do not provide `patientName`.
- **Staff / Admin role:** You must provide `patientName` to specify which patient to book for.

**Before calling this tool:**
1. Confirm the therapist name, date, and time with the user if any are ambiguous.
2. Tell the user what you are about to book (therapist, date, time) and ask them to confirm.
3. Only call the tool after the user confirms.

**After the tool responds:**
- On success, present the confirmed appointment details (ID, date/time, therapist, room) to the user.
- On conflict or capacity error, explain the issue and ask the user if they would like to try a different time or therapist.
