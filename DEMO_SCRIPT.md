# Clinic Scheduler — Demo Script

A record-ready walkthrough for the Kaggle capstone video. Target length **4–6 minutes**.
Fill in the `<PLACEHOLDERS>` with real values from your clinic before recording.

> **Concepts you're showing:** multi-agent routing · agent skills · MCP server · security
> (code-enforced confirmation + role limits) · tool use. Call them out as you go — graders are
> scoring for these.

---

## 0. Pre-flight checklist (do before you hit record)

- [ ] Live site is up and redeployed with the latest `MVP` (`bash deploy/start.sh` on the box).
- [ ] Hard-refresh the site (Ctrl+F5) so the new build + 💬 chat icon load.
- [ ] Have two logins ready:
  - **Patient:** `patient@clinic.com`
  - **Staff/Admin:** `admin@clinic.com`
  - (passwords are whatever you set via `SeedAdmin__Password`)
- [ ] Know one real **therapist name** at your clinic (e.g. `<THERAPIST LAST NAME>`).
- [ ] Pick two near-future weekday dates/times inside clinic hours (8 AM–5 PM): a "book" slot and a "move-to" slot.
- [ ] (For the MCP segment) Claude Desktop configured with the `clinic-scheduler` MCP server, OR the MCP Inspector ready. See `ClinicScheduler.Mcp/README.md`.
- [ ] Close noisy tabs/notifications. The agent makes 2–3 model calls per reply, so expect a ~1–2 s pause — that's normal; don't talk over dead air, pause your narration.

**Fill-in sheet (decide these now):**
| Placeholder | Your value |
|---|---|
| `<THERAPIST LAST NAME>` | e.g. Smith |
| `<BOOK DAY+TIME>` | e.g. next Tuesday at 2 PM |
| `<MOVE DAY+TIME>` | e.g. next Thursday at 10 AM |
| `<WAITLIST START>` / `<WAITLIST END>` | e.g. Aug 1 / Aug 15 |
| `<PLAN START>` | e.g. Aug 3 |

---

## 1. Intro (~25 s) — on camera or voiceover over the dashboard

> "Hi, I'm Brad. This is **Clinic Scheduler**, my submission for the Agents-for-Business track —
> an AI concierge for a pain-management clinic. Patients and staff just talk to it in plain English,
> and behind the chat is a **multi-agent system**: a coordinator that routes each request to a
> specialist sub-agent, all enforcing the clinic's real booking rules. Let me show you."

*Action:* Sign in as the **patient**, land on the dashboard, click the **💬 AI Assistant** icon (top-right) to open the chat.

---

## 2. Triage agent — advisory routing (~30 s)  · *shows multi-agent routing*

**Type:**
> `I've had lower back pain for a couple of months. Who should I see?`

*Expected:* The coordinator routes to the **Triage** specialist; it recommends a therapy
type/specialty (e.g. pain management / physical therapy) and suggests booking — without touching any records.

> Say: "The coordinator recognized this as a triage question and handed it to the Triage specialist —
> which only gives advice, it has no access to records."

---

## 3. Schedule agent — booking (~30 s)  · *tool use + business rules*

**Type:**
> `Book me with Dr. <THERAPIST LAST NAME> <BOOK DAY+TIME>.`

*Expected:* Routed to the **Scheduling** specialist; it books the appointment and reads back the
confirmed details (ID, date/time, therapist, room).

> Say: "Different request, different specialist — Scheduling. It ran the booking through the same
> rules engine the whole app uses: clinic hours, conflicts, and capacity."

---

## 4. Info agent — lookup (~20 s)  · *read-only specialist*

**Type:**
> `What appointments do I have?`

*Expected:* Routed to the **Info** specialist; it lists the appointment you just booked.

> Say: "This went to the read-only Info specialist — it can look things up but it literally has no
> booking or cancel tools."

---

## 5. Reschedule — composite skill (~40 s)  · *composite workflow + confirmation*

**Type:**
> `Move that appointment to <MOVE DAY+TIME>.`

*Expected:* Scheduling specialist returns a **"CONFIRMATION REQUIRED"** preview showing old → new time.

**Then type:**
> `Yes, go ahead.`

*Expected:* It books the new slot and cancels the old one, then reports the new appointment.

> Say: "Reschedule is a composite skill — it books the new slot first, fully validated, and only then
> cancels the old one, so you can never lose your appointment to a failed move. And notice it asked me
> to confirm first."

---

## 6. Cancel — code-enforced confirmation (~30 s)  · *security*

**Type:**
> `Actually, cancel that appointment.`

*Expected:* Another **"CONFIRMATION REQUIRED"** preview.

**Then type:**
> `Yes.`

*Expected:* The appointment is cancelled.

> Say: "That confirmation step isn't just a polite prompt — it's enforced in **code**. The cancel tool
> physically can't cancel until it's called a second time with a confirmed flag, even if the model tried to."

---

## 7. Waitlist agent (~30 s)  · *multi-agent + a distinct workflow*

**Type:**
> `Add me to the waitlist for any opening between <WAITLIST START> and <WAITLIST END>.`

*Expected:* Routed to the **Waitlist** specialist; confirms you're on the waitlist.

**Then type:**
> `What am I waiting for?`

*Expected:* Lists your active waitlist entry.

> Say: "A fourth specialist. When a slot frees up, the system books the first matching person automatically."

---

## 8. Security — role limits (~25 s)  · *security / authorization*

Still as the **patient**, **type:**
> `Create a treatment plan for me: 3 times a week for 30 sessions.`

*Expected:* The Treatment-plan specialist explains that creating plans is staff-only (or the tool
returns "Unauthorized").

> Say: "Authorization is enforced in C#, per role — a patient simply can't create a treatment plan,
> no matter how they phrase it."

---

## 9. Treatment-plan agent as staff — create + generate series (~45 s)  · *composite booking*

*Action:* Open a second browser/incognito window, sign in as **admin@clinic.com**, open the chat.

**Type:**
> `Create a treatment plan for <PATIENT NAME>: 3 times a week, 30 sessions, starting <PLAN START>, with Dr. <THERAPIST LAST NAME>.`

*Expected:* Treatment-plan specialist creates the plan and returns its **ID**.

**Then type (use the ID it returned):**
> `Generate the appointments for plan <ID>.`

*Expected:* It books the recurring series and reports how many sessions were booked.

> Say: "As staff, the same specialist can create a plan and then generate its whole recurring
> appointment series in one shot — dozens of bookings, all rule-checked."

---

## 10. MCP server (~40 s)  · *MCP server*

*Action:* Switch to **Claude Desktop** (or the MCP Inspector) with the `clinic-scheduler` server connected.

> Say: "The same scheduling capabilities are also exposed over the **Model Context Protocol**, so any
> MCP client can use them. Here's Claude Desktop connected to my clinic's MCP server."

*Action:* Show the four tools in the tools menu, then **type to Claude Desktop:**
> `Using the clinic tools, what appointments does <PATIENT NAME> have?`

*Expected:* Claude calls the `get_appointments` MCP tool and reports back.

> Say: "Same business logic, reused — one source of truth whether it's the in-app agent or an
> external MCP client."

---

## 11. Code peek (~30 s)  · *skills + multi-agent, in code*

*Action:* In the editor, briefly show:
1. A `SKILL.md` file (e.g. `Skills/cancel_my_appointment/SKILL.md`) — "each tool is a markdown-defined skill."
2. `OrchestratorAgentService.cs` — the `Specialists` roster and the `route_to_*` tools — "five specialists, each with a restricted set of skills."
3. The `CONFIRMATION REQUIRED` / `confirmed` check in `SkillExecutor.cs` — "the code-enforced guardrail."

---

## 12. Wrap-up (~20 s)

> "So that's Clinic Scheduler: a multi-agent assistant with markdown-defined skills, an MCP server,
> and security enforced in code — built on a real .NET clinic app. Thanks for watching."

---

## Lightning version (~90 s, if you need it short)

1. Patient: `Book me with Dr. <THERAPIST LAST NAME> <BOOK DAY+TIME>.` → booked.
2. `Cancel that appointment.` → confirm → cancelled. *(say: "confirmation enforced in code")*
3. `Add me to the waitlist between <WAITLIST START> and <WAITLIST END>.` → on waitlist. *(say: "different specialist")*
4. MCP: show Claude Desktop calling a clinic tool. *(say: "same tools over MCP")*
5. One line: "Five specialists, markdown skills, an MCP server, security in code."

---

## If something goes wrong on camera

- **Chat icon missing / page broken:** the box is on an old build — redeploy and hard-refresh.
- **"I wasn't able to complete that within a reasonable number of steps":** the iteration cap tripped;
  rephrase more directly (one action per message).
- **Therapist/patient "not found":** use the exact name as it appears on the Patients/Therapists page.
- **"outside the configured schedule":** pick a weekday time between 8 AM and 5 PM.
- **Booking says a time you didn't expect:** times are clinic-local wall-clock — say the time plainly
  ("2 PM"), no timezone.
