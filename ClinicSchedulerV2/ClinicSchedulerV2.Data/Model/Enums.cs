using NpgsqlTypes;

namespace ClinicSchedulerV2.Data.Model;

// Each enum mirrors a PostgreSQL enum type in db/postgres/01_schema.sql. Member names are the
// database labels exactly; [PgName] covers labels that are not valid C# identifiers.

public enum RoomType { Standard, Infusion }

public enum ProviderCredential { MD, NP, PA, RN }

public enum DrugClass { Biologic, Corticosteroid, Premedication }

public enum PlanStatus { Active, Completed, Discontinued }

public enum AppointmentStatus { Scheduled, Completed, Canceled, Missed }

public enum DoseUnit
{
    [PgName("mg")] Mg,
    [PgName("mg/kg")] MgPerKg,
    [PgName("mL")] ML,
}

public enum AdminRoute
{
    IV,
    PO,
    [PgName("Intra-articular")] IntraArticular,
}

public enum AdminReason
{
    Scheduled,
    [PgName("As-needed")] AsNeeded,
}

public enum RequestStatus { Pending, Approved, Denied }

public enum CancelSource { Patient, Clinic }

public enum AuditAction { INSERT, UPDATE, DELETE }
