using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Shared.Services;
using ClinicScheduler.Web.Services.Skills;

namespace ClinicScheduler.Web.Services;

/// <summary>
/// Definition of a specialist sub-agent: a focused role with its own system prompt and a
/// subset of the clinic skills it is allowed to use.
/// </summary>
/// <param name="Name">Lower-snake-case id; the coordinator routes to it via <c>route_to_{Name}</c>.</param>
/// <param name="Description">What this specialist handles — shown to the coordinator for routing.</param>
/// <param name="SystemPrompt">Role instructions for the specialist's own tool loop.</param>
/// <param name="SkillNames">Skills (tools) this specialist may call. Empty = advisory only.</param>
public sealed record SpecialistAgent(string Name, string Description, string SystemPrompt, string[] SkillNames);

/// <summary>
/// Multi-agent orchestrator. A lightweight <b>Coordinator</b> classifies each user request and
/// delegates it to exactly one <b>specialist sub-agent</b> (Info, Scheduling, or Triage), each of
/// which runs its own tool-calling loop with a focused system prompt and a restricted set of skills.
///
/// Routing is exposed to the coordinator as <c>route_to_*</c> tools; when the coordinator calls one,
/// this service runs that specialist (via the shared <see cref="AgentService.RunLoopAsync"/> primitive)
/// and feeds its answer back so the coordinator can relay it to the user. The specialist's internal
/// tool calls never touch the user-visible chat history.
/// </summary>
public class OrchestratorAgentService : IAgentService
{
    private static readonly ActivitySource ActivitySource = new("ClinicScheduler.OrchestratorAgentService");

    private readonly AgentService _agent;
    private readonly ISkillExecutor _skillExecutor;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<OrchestratorAgentService> _logger;

    /// <summary>The specialist roster. Add a specialist here to give the clinic a new workflow.</summary>
    private static readonly SpecialistAgent[] Specialists =
    [
        new SpecialistAgent(
            "info_agent",
            "Looks up and reports appointment information (e.g. \"what appointments do I have?\"). Read-only — cannot change anything.",
            "You are the Info specialist for a pain-management clinic. You retrieve and clearly present " +
            "appointment information using your tools. You never book, cancel, or modify anything — if the " +
            "user asks for a change, tell them you'll hand that to the Scheduling specialist.",
            ["get_my_appointments", "get_appointments"]),

        new SpecialistAgent(
            "scheduling_agent",
            "Books, cancels, or reschedules appointments — use for any request that changes the schedule.",
            "You are the Scheduling specialist for a pain-management clinic. You book, cancel, and reschedule " +
            "appointments using your tools. Cancelling and rescheduling both use a two-step confirmation: preview " +
            "first, then call again with confirmed=true only after the user agrees. Look up appointments when you " +
            "need an ID. Present results clearly.",
            ["get_my_appointments", "get_appointments", "schedule_appointment", "reschedule_appointment", "cancel_my_appointment", "cancel_any_appointment"]),

        new SpecialistAgent(
            "waitlist_agent",
            "Manages the waitlist: add a patient to it for a date window, list their waitlist entries, or remove one. Use when no slot is available now or the user mentions waiting for an opening.",
            "You are the Waitlist specialist for a pain-management clinic. You add patients to the waitlist for a " +
            "date window (the system books the first matching opening automatically), list their active waitlist " +
            "entries, and remove entries on request. Confirm the date window and any preferences before adding, and " +
            "confirm which entry to remove (show the list first if needed). Present results clearly.",
            ["join_waitlist", "get_my_waitlist", "leave_waitlist"]),

        new SpecialistAgent(
            "treatment_plan_agent",
            "Views, creates, and schedules treatment plans (recurring courses of therapy). Use for anything about a patient's treatment plan or generating its session series.",
            "You are the Treatment Plan specialist for a pain-management clinic. You can show a patient's treatment " +
            "plan to anyone entitled to see it. Creating a plan and generating its appointment series are Staff/Admin " +
            "only — if a patient asks to create one, explain that staff set up treatment plans. When creating a plan, " +
            "confirm the frequency (2, 3, or 4 per week), total sessions (20, 30, or 50), therapist, and start date " +
            "first. After creating, offer to generate the session series. Present results clearly.",
            ["get_my_treatment_plan", "create_treatment_plan", "generate_plan_appointments"]),

        new SpecialistAgent(
            "triage_agent",
            "Advises which therapy type or therapist specialty fits a described symptom or concern, then guides toward booking. No record access.",
            "You are the Triage specialist for a pain-management clinic. Based on the patient's described symptoms " +
            "or concerns, recommend an appropriate therapy type or therapist specialty (e.g. acute musculoskeletal " +
            "injury → physical therapy; chronic pain → pain management; anxiety or PTSD → behavioral therapy). You do " +
            "not access records or book appointments. Keep advice general, never give a diagnosis, and finish by " +
            "suggesting the patient ask to schedule with the recommended specialty.",
            []),
    ];

    public OrchestratorAgentService(
        AgentService agent,
        ISkillExecutor skillExecutor,
        ICurrentUserService currentUserService,
        ILogger<OrchestratorAgentService> logger)
    {
        _agent = agent;
        _skillExecutor = skillExecutor;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> ProcessMessageAsync(JsonArray chatHistory, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("ProcessMessageAsync");
        _logger.LogInformation("Orchestrator starting ProcessMessageAsync. Chat history length: {Count}", chatHistory.Count);

        // Snapshot the user-visible conversation (user/assistant text) BEFORE we add the coordinator's
        // system prompt or routing turns — specialists run with this clean context so they can handle
        // multi-turn flows (e.g. cancel → confirm) without seeing the orchestration plumbing.
        var conversation = ExtractConversation(chatHistory);

        if (chatHistory.Count == 0 || chatHistory[0]?["role"]?.GetValue<string>() != "system")
        {
            chatHistory.Insert(0, new JsonObject
            {
                ["role"] = "system",
                ["content"] = BuildCoordinatorPrompt()
            });
        }

        var routeTools = BuildRouteTools();

        // Run the coordinator loop; its "tools" are the specialists.
        var result = await _agent.RunLoopAsync(chatHistory, routeTools, async (toolName, args, innerCt) =>
        {
            using var routeActivity = ActivitySource.StartActivity("RouteToSpecialist");
            routeActivity?.SetTag("specialist.route", toolName);
            _logger.LogInformation("Coordinator routing request to specialist via: {ToolName}", toolName);

            var specialist = Array.Find(Specialists, s => $"route_to_{s.Name}" == toolName);
            if (specialist is null) 
            {
                _logger.LogWarning("Unknown specialist route requested: {ToolName}", toolName);
                return $"Error: unknown specialist route '{toolName}'.";
            }

            var task = args?["task"]?.GetValue<string>();
            _logger.LogInformation("Extracted task for {SpecialistName}: {Task}", specialist.Name, task);

            var specialistResult = await RunSpecialistAsync(specialist, conversation, task, innerCt);
            _logger.LogInformation("Specialist {SpecialistName} completed its task.", specialist.Name);
            return specialistResult;
        }, ct);

        _logger.LogInformation("Orchestrator ProcessMessageAsync completed.");
        return result;
    }

    /// <summary>Runs one specialist as a nested tool loop over a clean copy of the conversation.</summary>
    private async Task<string> RunSpecialistAsync(
        SpecialistAgent specialist, JsonArray conversation, string? task, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("RunSpecialistAsync");
        activity?.SetTag("specialist.name", specialist.Name);
        _logger.LogInformation("Initializing sub-agent loop for specialist: {SpecialistName}", specialist.Name);

        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = specialist.SystemPrompt + "\n" + BuildUserContext()
            }
        };

        // Give the specialist the real conversation so it has full context.
        foreach (var msg in conversation)
            messages.Add(msg!.DeepClone());

        if (!string.IsNullOrWhiteSpace(task))
            messages.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = $"The coordinator routed this request to you: {task}"
            });

        // Build this specialist's restricted tool set from its allowed skills.
        var tools = new JsonArray();
        foreach (var skillName in specialist.SkillNames)
        {
            try 
            { 
                tools.Add(_skillExecutor.GetToolSchema(skillName)); 
            }
            catch (ArgumentException ex) 
            { 
                _logger.LogWarning(ex, "Specialist {SpecialistName} requires skill '{SkillName}' but it is not implemented. Skipping.", specialist.Name, skillName);
            }
        }

        _logger.LogDebug("Specialist {SpecialistName} is starting its LLM loop with {ToolCount} allowed tools.", specialist.Name, tools.Count);

        var result = await _agent.RunLoopAsync(messages, tools,
            async (toolName, toolInput, _) => 
            {
                using var skillActivity = ActivitySource.StartActivity("SpecialistExecuteSkill");
                skillActivity?.SetTag("skill.name", toolName);
                _logger.LogInformation("Specialist {SpecialistName} executing skill: {SkillName}", specialist.Name, toolName);
                var res = await _skillExecutor.ExecuteAsync(toolName, toolInput);
                _logger.LogInformation("Specialist {SpecialistName} finished skill {SkillName}.", specialist.Name, toolName);
                return res;
            }, ct);

        _logger.LogInformation("Specialist {SpecialistName} completed its LLM loop.", specialist.Name);
        return result;
    }

    /// <summary>Builds one <c>route_to_{name}</c> function tool per specialist for the coordinator.</summary>
    private static JsonArray BuildRouteTools()
    {
        var tools = new JsonArray();
        foreach (var s in Specialists)
        {
            tools.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = $"route_to_{s.Name}",
                    ["description"] = s.Description,
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["task"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "A clear, one-sentence restatement of what the user needs."
                            }
                        },
                        ["required"] = new JsonArray { "task" }
                    }
                }
            });
        }
        return tools;
    }

    private string BuildCoordinatorPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are the Coordinator for a pain-management clinic's AI assistant.");
        sb.AppendLine("You do NOT perform clinic operations yourself. Read the user's request and delegate it to");
        sb.AppendLine("exactly ONE specialist using the matching route_to_* tool, passing a clear 'task'. After the");
        sb.AppendLine("specialist responds, relay its answer to the user faithfully (you may lightly adjust tone, but");
        sb.AppendLine("do not invent information). For a simple greeting or an unclear request, reply briefly yourself");
        sb.AppendLine("and ask what they need — do not route. Available specialists:");
        foreach (var s in Specialists)
            sb.AppendLine($"- route_to_{s.Name}: {s.Description}");
        sb.AppendLine(BuildUserContext());
        return sb.ToString();
    }

    private string BuildUserContext()
    {
        var user = _currentUserService.Principal;
        var userName = "Anonymous";
        var roleInfo = "Guest";
        if (user?.Identity?.IsAuthenticated == true)
        {
            userName = user.Identity.Name ?? "Unknown";
            var roles = new List<string>();
            foreach (var role in new[] { "Admin", "ClinicManager", "Therapist", "Staff", "Patient" })
                if (user.IsInRole(role)) roles.Add(role);
            roleInfo = roles.Count > 0 ? string.Join(", ", roles) : "Authenticated User";
        }
        return $"Currently logged in user: {userName} (Roles: {roleInfo})";
    }

    /// <summary>Clones the user/assistant text turns from the chat history, skipping system/tool turns
    /// and empty (tool-call-only) assistant turns.</summary>
    private static JsonArray ExtractConversation(JsonArray chatHistory)
    {
        var conversation = new JsonArray();
        foreach (var msg in chatHistory)
        {
            var role = msg?["role"]?.GetValue<string>();
            if (role != "user" && role != "assistant") continue;

            var content = msg?["content"];
            var text = content is JsonValue ? content.GetValue<string>() : null;
            if (string.IsNullOrWhiteSpace(text)) continue;

            conversation.Add(new JsonObject { ["role"] = role, ["content"] = text });
        }
        return conversation;
    }
}
