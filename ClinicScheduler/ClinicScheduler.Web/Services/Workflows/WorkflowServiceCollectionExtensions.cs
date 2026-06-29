namespace ClinicScheduler.Web.Services.Workflows;

/// <summary>
/// Registers the clinic framing (<see cref="ClinicProfile"/>) and every <see cref="IWorkflowPack"/>.
/// Adding a new workflow is a one-line registration here — the orchestrator picks it up automatically.
/// </summary>
public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddClinicWorkflows(this IServiceCollection services)
    {
        services.AddSingleton<ClinicProfile>();

        services.AddSingleton<IWorkflowPack, SchedulingWorkflowPack>();
        services.AddSingleton<IWorkflowPack, TreatmentPlanWorkflowPack>();
        services.AddSingleton<IWorkflowPack, TriageWorkflowPack>();

        return services;
    }
}
