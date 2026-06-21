---
name: generate_plan_appointments
description: Books the recurring appointment series for an existing treatment plan (Staff/Admin only). Use after a plan is created to schedule its sessions.
---
# generate_plan_appointments
Staff/Admin only. This is a composite booking: it schedules many sessions at once for the plan, following the plan's weekly frequency and respecting operating hours, conflicts, and capacity.

Provide the `treatmentPlanId` (look it up with `get_my_treatment_plan` if needed) and optionally a `preferredTime` (HH:MM, defaults to 09:00). After it runs, report how many sessions were booked and how many (if any) could not be placed.
