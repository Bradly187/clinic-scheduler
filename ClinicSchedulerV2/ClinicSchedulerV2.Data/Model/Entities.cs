namespace ClinicSchedulerV2.Data.Model;

// Plain row classes for the 17 tables. They carry no validation and no business rules on
// purpose: every rule is a constraint or trigger in the database (db/postgres/*.sql).

public class Location
{
    public int LocationId { get; set; }
    public string Name { get; set; } = "";
    public string Street { get; set; } = "";
    public string City { get; set; } = "Houston";
    public string State { get; set; } = "TX";
    public string Zip { get; set; } = "";
    public string Phone { get; set; } = "";
    public short MaxConcurrentPatients { get; set; } = 12;
    public List<Room> Rooms { get; set; } = [];

    /// <summary>"Houston Arthritis Center - Clear Lake" reads as "Clear Lake" in tight spaces.</summary>
    public string ShortName => Name.Contains(" - ") ? Name[(Name.LastIndexOf(" - ", StringComparison.Ordinal) + 3)..] : Name;
}

public class Room
{
    public int RoomId { get; set; }
    public int LocationId { get; set; }
    public Location Location { get; set; } = null!;
    public string RoomName { get; set; } = "";
    public RoomType RoomType { get; set; }
    public short Capacity { get; set; } = 1;
    public bool IsActive { get; set; } = true;
}

public class Employee
{
    public int EmployeeId { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public DateOnly HireDate { get; set; }
    public bool IsActive { get; set; } = true;
    public Provider? Provider { get; set; }
    public Staff? Staff { get; set; }
    public List<Location> Locations { get; set; } = [];

    public string FullName => $"{FirstName} {LastName}";
}

public class Provider
{
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public string Npi { get; set; } = "";
    public ProviderCredential Credential { get; set; }
    public List<ServiceType> Services { get; set; } = [];

    public string DisplayName => $"{Employee.FullName}, {Credential}";
}

public class Staff
{
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public string JobTitle { get; set; } = "";
}

public class EmployeeLocation
{
    public int EmployeeId { get; set; }
    public int LocationId { get; set; }
}

public class Patient
{
    public int PatientId { get; set; }
    public int HomeLocationId { get; set; }
    public Location HomeLocation { get; set; } = null!;
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateOnly DateOfBirth { get; set; }
    public string? Email { get; set; }
    public string Phone { get; set; } = "";
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public bool IsActive { get; set; } = true;

    public string FullName => $"{FirstName} {LastName}";
}

public class ServiceType
{
    public int ServiceTypeId { get; set; }
    public string Name { get; set; } = "";
    public short DefaultDurationMin { get; set; }
    public RoomType RequiredRoomType { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ProviderQualification
{
    public int EmployeeId { get; set; }
    public int ServiceTypeId { get; set; }
}

public class Medication
{
    public int MedicationId { get; set; }
    public string GenericName { get; set; } = "";
    public string? BrandName { get; set; }
    public DrugClass DrugClass { get; set; }
    public bool IsActive { get; set; } = true;
}

public class TreatmentPlan
{
    public int PlanId { get; set; }
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public int OrderingProviderId { get; set; }
    public Provider OrderingProvider { get; set; } = null!;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public PlanStatus Status { get; set; }
    public string? Notes { get; set; }
    public List<TreatmentPlanItem> Items { get; set; } = [];
}

public class TreatmentPlanItem
{
    public int PlanItemId { get; set; }
    public int PlanId { get; set; }
    public TreatmentPlan Plan { get; set; } = null!;
    public int ServiceTypeId { get; set; }
    public ServiceType ServiceType { get; set; } = null!;
    public int? MedicationId { get; set; }
    public Medication? Medication { get; set; }
    public short IntervalWeeks { get; set; }
    public short? TotalSessions { get; set; }
    public List<Appointment> Appointments { get; set; } = [];
}

public class Appointment
{
    public int AppointmentId { get; set; }
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public int ProviderId { get; set; }
    public Provider Provider { get; set; } = null!;
    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public int ServiceTypeId { get; set; }
    public ServiceType ServiceType { get; set; } = null!;
    public int? PlanItemId { get; set; }
    public TreatmentPlanItem? PlanItem { get; set; }
    public int? RescheduledFromId { get; set; }
    public int CreatedBy { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public AppointmentStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Notes { get; set; }
}

public class MedicationAdministration
{
    public int AdministrationId { get; set; }
    public int AppointmentId { get; set; }
    public Appointment Appointment { get; set; } = null!;
    public int MedicationId { get; set; }
    public Medication Medication { get; set; } = null!;
    public int AdministeredBy { get; set; }
    public decimal Dose { get; set; }
    public DoseUnit DoseUnit { get; set; }
    public AdminRoute Route { get; set; }
    public string? Site { get; set; }
    public string LotNumber { get; set; } = "";
    public AdminReason Reason { get; set; }
    public DateTime AdministeredAt { get; set; }
}

public class AppointmentRequest
{
    public int RequestId { get; set; }
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public int ServiceTypeId { get; set; }
    public ServiceType ServiceType { get; set; } = null!;
    public int? PreferredLocationId { get; set; }
    public Location? PreferredLocation { get; set; }
    public DateTime PreferredStart { get; set; }
    public string? PatientNotes { get; set; }
    public RequestStatus Status { get; set; }
    public DateTime SubmittedAt { get; set; }
    public int? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? DecisionNote { get; set; }
    public int? AppointmentId { get; set; }
}

public class CancellationRequest
{
    public int CancelRequestId { get; set; }
    public int AppointmentId { get; set; }
    public Appointment Appointment { get; set; } = null!;
    public CancelSource Source { get; set; }
    public int? SubmittedBy { get; set; }
    public string Reason { get; set; } = "";
    public RequestStatus Status { get; set; }
    public DateTime SubmittedAt { get; set; }
    public int? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

public class AuditLogEntry
{
    public long AuditId { get; set; }
    public string TableName { get; set; } = "";
    public int RecordId { get; set; }
    public AuditAction Action { get; set; }
    public int? ChangedBy { get; set; }
    public Employee? ChangedByEmployee { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
}
