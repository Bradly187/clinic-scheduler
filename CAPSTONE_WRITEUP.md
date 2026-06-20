# Clinic Scheduler — AI Concierge Agent for Private Health Clinics

**Kaggle 5-Day AI Agents: Intensive Vibe Coding Capstone**
**Track:** Agents for Business

---

## 1. The problem

Private and small health clinics run on thin margins. Enterprise healthcare software with built-in
AI is expensive and over-built for a single pain-management practice. So the day-to-day — patients
requesting appointments, cancelling, rescheduling, and staff validating business rules by hand —
falls on front-desk staff. That manual work is slow, error-prone, and a frequent source of
double-bookings.

## 2. The solution

**Clinic Scheduler** is a full scheduling system with an embedded **AI Concierge Agent**. Patients
and staff talk to it in plain language ("What appointments do I have?", "Cancel my Thursday session",
"Book Jane with Dr. Smith next Tuesday at 2 PM") and the agent executes **strict, role-checked C#
backend logic** to enforce the clinic's real business rules — weekday 8 AM–5 PM windows, fixed slot
lengths, therapist/room/patient conflict detection, and a per-location daily patient cap.

The agent is powered by **Google Gemini (`gemini-2.5-flash`)** via its OpenAI-compatible tool-calling
endpoint, embedded in an ASP.NET Core 10 + Blazor application backed by PostgreSQL.

## 3. Course concepts demonstrated

This project demonstrates **five** of the course's key concepts:

### a) Multi-agent system
The chat is served by an **orchestrator** (`OrchestratorAgentService`): a lightweight **Coordinator**
classifies each request and delegates it to exactly one **specialist sub-agent**, each with its own
system prompt and a *restricted* skill set:
- **Info agent** — read-only lookups (what appointments do I have?).
- **Scheduling agent** — booking, cancelling, rescheduling (the write operations).
- **Triage agent** — advisory: maps described symptoms to an appropriate therapy type/specialty.

The Coordinator's *tools are the specialists* (`route_to_*`); routing runs the chosen specialist as a
nested tool loop and feeds its answer back. Restricting each specialist's skills is itself a safety
boundary — a read-only role can't perform writes. New clinic workflows are added by dropping a new
specialist into the roster.

### b) Agent skills
Each tool the agent can use is defined as a self-contained `SKILL.md` file (YAML frontmatter +
natural-language instructions) under `ClinicScheduler.Web/Skills/<name>/`. At startup, `SkillRegistry`
discovers and parses them into the agent's system-prompt tool catalog, while `SkillExecutor` holds the
matching JSON schema and C# implementation. This mirrors the modern agent-skills pattern and cleanly
separates **how a tool is described** to the model from **how it is executed**.

Skills: `get_my_appointments`, `cancel_my_appointment`, `get_appointments`, `cancel_any_appointment`,
`schedule_appointment`.

### c) MCP server
A standalone **Model Context Protocol server** (`ClinicScheduler.Mcp`, built on the official C# SDK)
exposes the scheduling operations to *any* MCP client — Claude Desktop, an ADK agent, or the MCP
Inspector — over stdio: `list_therapists`, `get_appointments`, `schedule_appointment`,
`cancel_appointment`. Crucially, it **reuses the same domain logic** (`AppointmentSchedulingService`
+ EF Core repositories) as the web app, so business rules are enforced identically no matter which
front end calls them.

### d) Security features
Security is enforced **in code, not just prompts**:
- **Role-based authorization** — privileged tools (e.g. cancelling anyone's appointment) check the
  caller's roles in C# and refuse unauthorized callers, so a misbehaving model cannot escalate.
- **Code-enforced two-step confirmation** for destructive actions — a cancel tool first returns a
  `CONFIRMATION REQUIRED` preview and only cancels when called again with `confirmed=true`. This holds
  even if the model ignores its instructions.
- **Secret hygiene** — API keys live in git-ignored `.env` / environment variables, never in the repo.
- **Auth-gated agent endpoint** — the chat API requires authentication.

### e) Tool use & context injection *(bonus)*
The agent runs a full OpenAI-style tool-calling loop (model → `tool_calls` → backend execution →
result → repeat until a final answer), with the logged-in user's identity and roles injected into the
system prompt so the agent behaves correctly per permission level.

## 4. Architecture

```
Patient / Staff
      │  natural language
      ▼
AgentChat (Blazor)
      ▼
Coordinator agent  ──► routes to one specialist (Gemini tool-calling)
      ├─ Info agent        (read-only lookups)
      ├─ Scheduling agent  (book / cancel / reschedule)
      └─ Triage agent      (symptom → specialty advice)
                 │  each runs its own tool loop
                 ▼
            SkillExecutor ──► AppointmentSchedulingService ──► PostgreSQL
                                        ▲
ClinicScheduler.Mcp (MCP server) ───────┘   (same business rules)
      ▲ stdio
MCP client (Claude Desktop / ADK / Inspector)
```

Every path — the Coordinator's specialists *and* the MCP server — funnels through the **same**
`AppointmentSchedulingService`, the single source of truth for scheduling rules.

## 5. Tech stack

- **AI:** Google Gemini `gemini-2.5-flash` (OpenAI-compatible endpoint); Model Context Protocol (C# SDK)
- **Backend:** ASP.NET Core 10, Entity Framework Core 10, PostgreSQL
- **Frontend:** Blazor (Server + WebAssembly hybrid), MudBlazor
- **Infra:** Docker / docker-compose; deployed on AWS (EC2 + nginx)
- **Testing:** xUnit, FluentAssertions, Moq, Testcontainers

## 6. Try it

- **Live demo:** https://clinic.bradtarver.com
- **Source:** https://github.com/Bradly187/clinic-scheduler (branch `MVP`)
- **Run locally / MCP setup:** see `README.md` and `ClinicScheduler/ClinicScheduler.Mcp/README.md`.

**Demo script:**
1. Sign in as a patient → open the 💬 AI Assistant → "What appointments do I have?"
2. "Cancel my appointment" → the agent lists them, you pick one → it asks you to **confirm** → confirm → cancelled.
3. "Book me with Dr. Smith next Tuesday at 2 PM" → the agent schedules it and reads back the details.
4. (MCP) Connect Claude Desktop to `ClinicScheduler.Mcp` and run the same operations from outside the app.

## 7. Reflection

The most valuable design decision was funneling every entry point — the Gemini agent, the MCP server,
and the web UI — through one domain service. It meant **agent safety became a property of the system**,
not of the prompt: business rules, role checks, and destructive-action confirmation are enforced in C#
and are impossible for the model to bypass. The skills-as-markdown and MCP layers then made those same
guarded capabilities reusable across any agent runtime.
