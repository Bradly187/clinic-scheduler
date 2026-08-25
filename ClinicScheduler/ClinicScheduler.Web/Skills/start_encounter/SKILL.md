---
name: start_encounter
description: Opens a new intake encounter (visit) for a patient. Staff/Admin only. Resolves the patient by name or email and optionally links a therapist, location, and reason for the visit.
---
# start_encounter

Use this tool to begin a patient visit during intake. Resolve the patient with `patientName` or `email`. Optionally provide a `reason` (chief complaint), `therapistName`, and `locationName`.

**Role-based behavior:**
- **Staff / Admin only.** If a patient asks to start their own visit, explain that staff open encounters at check-in.

**Before calling this tool:**
1. Make sure the patient is registered (use `verify_patient_demographics`, or `register_patient` first if they are new).
2. Confirm the reason for the visit and any therapist/location with the user.

**After the tool responds:**
- Confirm the encounter ID and status, and offer the next step (e.g. scheduling the appointment for the visit).
- On error, explain what was missing (e.g. patient not found) and ask for the correction.
