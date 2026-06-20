---
name: cancel_any_appointment
description: Cancels any appointment by ID or patient name. Only Staff or Admins can use this tool. Use this when an administrator or staff member needs to override or cancel someone else's appointment.
---
# cancel_any_appointment
Call this tool immediately with either the `appointmentId` or the `patientName` if you have one of them. Do not ask the user for more information (like an appointment ID) if they have already provided a patient name or reference to a patient. You must call this tool with the patient's name immediately. This is a privileged operation.

If providing the patient name results in multiple appointments, the tool will return a list of appointments. You MUST display this entire list of appointments (including their IDs and start times) to the user so they can see their options, and then ask them to choose or provide the specific appointment ID of the one they want to cancel. Do NOT ask for the ID without showing the list.

This tool enforces a two-step confirmation in code:

1. **Preview:** Call it with the `appointmentId` (or `patientName`) and leave `confirmed` unset. When a single appointment is identified, the tool returns a `CONFIRMATION REQUIRED` message with the appointment's date and time. Relay those details to the user and ask them to confirm.
2. **Confirm:** Only after the user explicitly agrees, call the tool again with the specific `appointmentId` and `confirmed=true` to actually cancel.

Never set `confirmed=true` on the first call or without the user's explicit approval.
