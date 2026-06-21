# Demo Cue Card — talking points only

One screen. **▸ = type into chat. 🎙 = say it.** Fill `<...>` before recording.

---

🎙 "I'm Brad. This is **Clinic Scheduler** — an AI concierge for a pain clinic, for the Agents-for-Business track. Behind the chat is a **multi-agent system**: a coordinator routing to specialist agents. Watch."

**Triage**
▸ `I've had lower back pain for months. Who should I see?`
🎙 "The coordinator sent that to the **Triage** specialist — advice only, no record access."

**Book**
▸ `Book me with Dr. <THERAPIST> <BOOK DAY+TIME>.`
🎙 "Different request, the **Scheduling** specialist — booked with full conflict and capacity checks."

**View**
▸ `What appointments do I have?`
🎙 "The read-only **Info** specialist — no booking tools at all."

**Reschedule**
▸ `Move that appointment to <MOVE DAY+TIME>.` → then ▸ `Yes, go ahead.`
🎙 "A **composite** skill — it books the new slot first, then cancels the old, so you never lose your spot. And it confirmed first."

**Cancel**
▸ `Actually, cancel that appointment.` → then ▸ `Yes.`
🎙 "That confirmation is enforced in **code** — it can't cancel without a second confirmed call."

**Waitlist**
▸ `Add me to the waitlist between <START> and <END>.` → then ▸ `What am I waiting for?`
🎙 "A fourth specialist — when a slot frees up, it books the first match automatically."

**Security (still the patient)**
▸ `Create a treatment plan for me, 3 times a week.`
🎙 "Denied — authorization is enforced per role in C#. Patients can't create plans."

**Staff (sign in as admin)**
▸ `Create a treatment plan for <PATIENT>: 3x a week, 30 sessions, starting <PLAN START>, with Dr. <THERAPIST>.`
▸ `Generate the appointments for plan <ID>.`
🎙 "As staff, the **Treatment-plan** specialist creates the plan and books the whole recurring series at once."

**MCP (Claude Desktop)**
▸ `Using the clinic tools, what appointments does <PATIENT> have?`
🎙 "The same capabilities are exposed over the **Model Context Protocol** — any MCP client can use them. Same business logic, one source of truth."

**Code peek**
🎙 "In code: each tool is a markdown **SKILL.md**; five specialists each with restricted skills; and the confirmation guardrail right here."

**Close**
🎙 "Multi-agent, markdown skills, an MCP server, security in code — on a real .NET clinic app. Thanks for watching."
