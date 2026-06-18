---
name: cancel_my_appointment
description: Cancels an appointment for the currently logged in patient given the appointment ID. Use this when the patient asks to cancel one of their own appointments.
---
# cancel_my_appointment
When you need to cancel the user's appointment, make sure you have their appointment ID. If they haven't provided an appointment ID, you MUST first call the `get_my_appointments` tool to retrieve the list of their upcoming appointments, display the list (including dates, times, and IDs) to the user, and then ask them to choose or specify the ID of the appointment they want to cancel. Do NOT ask for the ID without showing their schedule first.
