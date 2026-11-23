using ClinicSchedulerV2.Data.Model;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Npgsql.NameTranslation;

namespace ClinicSchedulerV2.Data;

/// <summary>
/// Maps the row classes onto the tables created by db/postgres/*.sql. There are no EF
/// migrations: the SQL scripts own the schema, and this context only reads and writes rows.
/// It derives from Identity's user context so sign-in uses the same database and the same
/// rules (app_user and its side tables are created by 04_accounts.sql).
/// </summary>
public class ClinicDbContext(DbContextOptions<ClinicDbContext> options) : IdentityUserContext<AppUser, int>(options)
{
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<Staff> Staff => Set<Staff>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();
    public DbSet<Medication> Medications => Set<Medication>();
    public DbSet<TreatmentPlan> TreatmentPlans => Set<TreatmentPlan>();
    public DbSet<TreatmentPlanItem> TreatmentPlanItems => Set<TreatmentPlanItem>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<MedicationAdministration> MedicationAdministrations => Set<MedicationAdministration>();
    public DbSet<AppointmentRequest> AppointmentRequests => Set<AppointmentRequest>();
    public DbSet<CancellationRequest> CancellationRequests => Set<CancellationRequest>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<InsurancePolicy> InsurancePolicies => Set<InsurancePolicy>();
    public DbSet<WaitlistEntry> Waitlist => Set<WaitlistEntry>();
    public DbSet<VisitNote> VisitNotes => Set<VisitNote>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<InvoiceSummary> InvoiceSummaries => Set<InvoiceSummary>();
    public DbSet<PatientBalance> PatientBalances => Set<PatientBalance>();
    public DbSet<SuperbillLine> Superbill => Set<SuperbillLine>();

    /// <summary>Provider, naming and enum settings shared by the app and the tests.</summary>
    public static void Configure(DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseNpgsql(connectionString, MapEnums).UseSnakeCaseNamingConvention();

    private static void MapEnums(NpgsqlDbContextOptionsBuilder npgsql)
    {
        // Labels match the C# member names as written (or their [PgName]), not snake_case
        var asWritten = new NpgsqlNullNameTranslator();
        npgsql.MapEnum<RoomType>("room_type", nameTranslator: asWritten);
        npgsql.MapEnum<ProviderCredential>("provider_credential", nameTranslator: asWritten);
        npgsql.MapEnum<DrugClass>("drug_class", nameTranslator: asWritten);
        npgsql.MapEnum<PlanStatus>("plan_status", nameTranslator: asWritten);
        npgsql.MapEnum<AppointmentStatus>("appointment_status", nameTranslator: asWritten);
        npgsql.MapEnum<DoseUnit>("dose_unit", nameTranslator: asWritten);
        npgsql.MapEnum<AdminRoute>("admin_route", nameTranslator: asWritten);
        npgsql.MapEnum<AdminReason>("admin_reason", nameTranslator: asWritten);
        npgsql.MapEnum<RequestStatus>("request_status", nameTranslator: asWritten);
        npgsql.MapEnum<CancelSource>("cancel_source", nameTranslator: asWritten);
        npgsql.MapEnum<AuditAction>("audit_action", nameTranslator: asWritten);
        npgsql.MapEnum<InvoiceStatus>("invoice_status", nameTranslator: asWritten);
        npgsql.MapEnum<PaymentMethod>("payment_method", nameTranslator: asWritten);
        npgsql.MapEnum<WaitlistStatus>("waitlist_status", nameTranslator: asWritten);
        npgsql.MapEnum<NotificationKind>("notification_kind", nameTranslator: asWritten);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configuration)
    {
        // Clinic-local wall-clock time, as in the schema (never timestamptz)
        configuration.Properties<DateTime>().HaveColumnType("timestamp without time zone");
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);

        // Identity's tables, under the names 04_accounts.sql gives them
        model.Entity<AppUser>(e =>
        {
            e.ToTable("app_user");
            e.HasOne(x => x.Employee).WithOne().HasForeignKey<AppUser>(x => x.EmployeeId);
            e.HasOne(x => x.Patient).WithOne().HasForeignKey<AppUser>(x => x.PatientId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("clinic_now()");
        });
        model.Entity<IdentityUserClaim<int>>().ToTable("app_user_claim");
        model.Entity<IdentityUserLogin<int>>().ToTable("app_user_login");
        model.Entity<IdentityUserToken<int>>().ToTable("app_user_token");

        model.Entity<Location>(e =>
        {
            e.ToTable("location").HasKey(x => x.LocationId);
            e.Ignore(x => x.ShortName);
            e.HasMany(x => x.Rooms).WithOne(r => r.Location).HasForeignKey(r => r.LocationId);
        });

        model.Entity<Room>(e => e.ToTable("room").HasKey(x => x.RoomId));

        model.Entity<Employee>(e =>
        {
            e.ToTable("employee").HasKey(x => x.EmployeeId);
            e.Ignore(x => x.FullName);
            e.HasOne(x => x.Provider).WithOne(p => p.Employee).HasForeignKey<Provider>(p => p.EmployeeId);
            e.HasOne(x => x.Staff).WithOne(s => s.Employee).HasForeignKey<Staff>(s => s.EmployeeId);
            e.HasMany(x => x.Locations).WithMany().UsingEntity<EmployeeLocation>(
                right => right.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId),
                left => left.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId),
                join =>
                {
                    join.ToTable("employee_location");
                    join.HasKey(x => new { x.EmployeeId, x.LocationId });
                });
        });

        model.Entity<Provider>(e =>
        {
            e.ToTable("provider").HasKey(x => x.EmployeeId);
            e.Property(x => x.EmployeeId).ValueGeneratedNever();
            e.Ignore(x => x.DisplayName);
            e.HasMany(x => x.Services).WithMany().UsingEntity<ProviderQualification>(
                right => right.HasOne<ServiceType>().WithMany().HasForeignKey(x => x.ServiceTypeId),
                left => left.HasOne<Provider>().WithMany().HasForeignKey(x => x.EmployeeId),
                join =>
                {
                    join.ToTable("provider_qualification");
                    join.HasKey(x => new { x.EmployeeId, x.ServiceTypeId });
                });
        });

        model.Entity<Staff>(e =>
        {
            e.ToTable("staff").HasKey(x => x.EmployeeId);
            e.Property(x => x.EmployeeId).ValueGeneratedNever();
        });

        model.Entity<Patient>(e =>
        {
            e.ToTable("patient").HasKey(x => x.PatientId);
            e.Ignore(x => x.FullName);
            e.HasOne(x => x.HomeLocation).WithMany().HasForeignKey(x => x.HomeLocationId);
        });

        model.Entity<ServiceType>(e => e.ToTable("service_type").HasKey(x => x.ServiceTypeId));
        model.Entity<Medication>(e => e.ToTable("medication").HasKey(x => x.MedicationId));

        model.Entity<TreatmentPlan>(e =>
        {
            e.ToTable("treatment_plan").HasKey(x => x.PlanId);
            e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId);
            e.HasOne(x => x.OrderingProvider).WithMany().HasForeignKey(x => x.OrderingProviderId);
            e.HasMany(x => x.Items).WithOne(i => i.Plan).HasForeignKey(i => i.PlanId);
        });

        model.Entity<TreatmentPlanItem>(e =>
        {
            e.ToTable("treatment_plan_item").HasKey(x => x.PlanItemId);
            e.HasOne(x => x.ServiceType).WithMany().HasForeignKey(x => x.ServiceTypeId);
            e.HasOne(x => x.Medication).WithMany().HasForeignKey(x => x.MedicationId);
            e.HasMany(x => x.Appointments).WithOne(a => a.PlanItem).HasForeignKey(a => a.PlanItemId);
        });

        model.Entity<Appointment>(e =>
        {
            e.ToTable("appointment").HasKey(x => x.AppointmentId);
            e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId);
            e.HasOne(x => x.Provider).WithMany().HasForeignKey(x => x.ProviderId);
            e.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId);
            e.HasOne(x => x.ServiceType).WithMany().HasForeignKey(x => x.ServiceTypeId);
            // Let the database stamp the clinic-local creation time
            e.Property(x => x.CreatedAt).HasDefaultValueSql("clinic_now()");
        });

        model.Entity<MedicationAdministration>(e =>
        {
            e.ToTable("medication_administration").HasKey(x => x.AdministrationId);
            e.HasOne(x => x.Appointment).WithMany().HasForeignKey(x => x.AppointmentId);
            e.HasOne(x => x.Medication).WithMany().HasForeignKey(x => x.MedicationId);
        });

        model.Entity<AppointmentRequest>(e =>
        {
            e.ToTable("appointment_request").HasKey(x => x.RequestId);
            e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId);
            e.HasOne(x => x.ServiceType).WithMany().HasForeignKey(x => x.ServiceTypeId);
            e.HasOne(x => x.PreferredLocation).WithMany().HasForeignKey(x => x.PreferredLocationId);
            e.Property(x => x.SubmittedAt).HasDefaultValueSql("clinic_now()");
        });

        model.Entity<CancellationRequest>(e =>
        {
            e.ToTable("cancellation_request").HasKey(x => x.CancelRequestId);
            e.HasOne(x => x.Appointment).WithMany().HasForeignKey(x => x.AppointmentId);
            e.Property(x => x.SubmittedAt).HasDefaultValueSql("clinic_now()");
        });

        model.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("audit_log").HasKey(x => x.AuditId);
            e.Property(x => x.OldValues).HasColumnType("jsonb");
            e.Property(x => x.NewValues).HasColumnType("jsonb");
            e.HasOne(x => x.ChangedByEmployee).WithMany().HasForeignKey(x => x.ChangedBy);
        });

        // ---- Billing (05_billing.sql) ----
        model.Entity<Invoice>(e =>
        {
            e.ToTable("invoice").HasKey(x => x.InvoiceId);
            e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.InvoiceId);
            e.HasMany(x => x.Payments).WithOne().HasForeignKey(p => p.InvoiceId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("clinic_now()");
        });
        model.Entity<InvoiceLine>(e =>
        {
            e.ToTable("invoice_line").HasKey(x => x.LineId);
            e.HasOne(x => x.Appointment).WithMany().HasForeignKey(x => x.AppointmentId);
            e.Property(x => x.Amount).ValueGeneratedOnAddOrUpdate();
        });
        model.Entity<Payment>(e =>
        {
            e.ToTable("payment").HasKey(x => x.PaymentId);
            e.HasOne(x => x.RecordedByEmployee).WithMany().HasForeignKey(x => x.RecordedBy);
            e.Property(x => x.PaidAt).HasDefaultValueSql("clinic_now()");
        });
        model.Entity<InsurancePolicy>(e => e.ToTable("insurance_policy").HasKey(x => x.PolicyId));

        // ---- Waitlist, notes, notifications (06_care.sql) ----
        model.Entity<WaitlistEntry>(e =>
        {
            e.ToTable("waitlist_entry").HasKey(x => x.EntryId);
            e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId);
            e.HasOne(x => x.ServiceType).WithMany().HasForeignKey(x => x.ServiceTypeId);
            e.HasOne(x => x.PreferredLocation).WithMany().HasForeignKey(x => x.PreferredLocationId);
            e.HasOne(x => x.PreferredProvider).WithMany().HasForeignKey(x => x.PreferredProviderId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("clinic_now()");
        });
        model.Entity<VisitNote>(e =>
        {
            e.ToTable("visit_note").HasKey(x => x.NoteId);
            e.HasOne(x => x.Appointment).WithMany().HasForeignKey(x => x.AppointmentId);
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("clinic_now()");
        });
        model.Entity<Notification>(e =>
        {
            e.ToTable("notification").HasKey(x => x.NotificationId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("clinic_now()");
        });

        // ---- Read-only views ----
        model.Entity<InvoiceSummary>(e => e.ToView("vw_invoice_summary").HasNoKey());
        model.Entity<PatientBalance>(e => e.ToView("vw_patient_balance").HasNoKey());
        model.Entity<SuperbillLine>(e => e.ToView("vw_superbill").HasNoKey());
    }
}
