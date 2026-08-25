using ClinicScheduler.Web.Services.Skills.Implementations;

namespace ClinicScheduler.Web.Services.Skills;

/// <summary>
/// Registers the agent skill surface: the shared time formatter, every <see cref="ISkill"/>,
/// and the <see cref="ISkillExecutor"/> dispatcher. Adding a new skill is a one-line addition here.
/// </summary>
public static class SkillServiceCollectionExtensions
{
    public static IServiceCollection AddClinicSkills(this IServiceCollection services)
    {
        services.AddSingleton<IClinicTimeFormatter, ClinicTimeFormatter>();

        // Scheduling / appointments
        services.AddScoped<ISkill, GetMyAppointmentsSkill>();
        services.AddScoped<ISkill, GetAppointmentsSkill>();
        services.AddScoped<ISkill, ScheduleAppointmentSkill>();
        services.AddScoped<ISkill, RescheduleAppointmentSkill>();
        services.AddScoped<ISkill, CancelMyAppointmentSkill>();
        services.AddScoped<ISkill, CancelAnyAppointmentSkill>();

        // Waitlist
        services.AddScoped<ISkill, JoinWaitlistSkill>();
        services.AddScoped<ISkill, GetMyWaitlistSkill>();
        services.AddScoped<ISkill, LeaveWaitlistSkill>();

        // Treatment plans
        services.AddScoped<ISkill, GetMyTreatmentPlanSkill>();
        services.AddScoped<ISkill, CreateTreatmentPlanSkill>();
        services.AddScoped<ISkill, GeneratePlanAppointmentsSkill>();

        // Intake
        services.AddScoped<ISkill, RegisterPatientSkill>();
        services.AddScoped<ISkill, VerifyPatientDemographicsSkill>();
        services.AddScoped<ISkill, StartEncounterSkill>();

        // Public booking
        services.AddScoped<ISkill, CheckAvailabilitySkill>();
        services.AddScoped<ISkill, ListTherapistsPublicSkill>();
        services.AddScoped<ISkill, ListTherapyTypesPublicSkill>();
        services.AddScoped<ISkill, BookAppointmentPublicSkill>();

        // Billing (staff)
        services.AddScoped<ISkill, GetPatientBalanceSkill>();
        services.AddScoped<ISkill, GetPatientInvoicesSkill>();
        services.AddScoped<ISkill, CreateInvoiceSkill>();
        services.AddScoped<ISkill, RecordPaymentSkill>();

        services.AddScoped<ISkillExecutor, SkillExecutor>();
        return services;
    }
}
