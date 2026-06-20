---
name: cancel_my_appointment
description: Cancels an appointment for the currently logged in patient given the appointment ID. Use this when the patient asks to cancel one of their own appointments.
---
# cancel_my_appointment
When you need to cancel the user's appointment, make sure you have their appointment ID. If they haven't provided an appointment ID, you MUST first call the `get_my_appointments` tool to retrieve the list of their upcoming appointments, display the list (including dates, times, and IDs) to the user, and then ask them to choose or specify the ID of the appointment they want to cancel. Do NOT ask for the ID without showing their schedule first.

This tool enforces a two-step confirmation in code:

1. **Preview:** Call it with the `appointmentId` only (leave `confirmed` unset). The tool returns a `CONFIRMATION REQUIRED` message with the appointment's date and time. Relay those details to the user and ask them to confirm.
2. **Confirm:** Only after the user explicitly agrees, call the tool again with the same `appointmentId` and `confirmed=true` to actually cancel.

Never set `confirmed=true` on the first call or without the user's explicit approval.
