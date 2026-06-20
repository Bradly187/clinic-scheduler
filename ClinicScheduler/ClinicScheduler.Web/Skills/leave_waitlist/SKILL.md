---
name: leave_waitlist
description: Removes one of the currently logged in patient's waitlist entries by ID. Use when the patient no longer wants to wait for a slot.
---
# leave_waitlist
If the patient hasn't given an entry ID, first call `get_my_waitlist`, show the list, and ask which entry to remove. State the entry you are about to remove and confirm with the user before calling this tool.
