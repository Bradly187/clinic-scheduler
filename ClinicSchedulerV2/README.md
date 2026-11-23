# ClinicSchedulerV2

A version of ClinicScheduler where **PostgreSQL enforces the rules**. The schema, its constraints,
triggers and routines are hand-written SQL in `db/postgres`; the C# code holds no validation. It
sends each change to the database as the signed-in user and shows what the database decided.

The design started as the CSCI 380 database project (MySQL 8, the original scripts are in
`db/mysql`) and was ported to PostgreSQL, then extended with accounts, billing, waitlist, visit
notes and notifications.

## What's where

| Path | Contents |
|---|---|
| `db/postgres/01_schema.sql` … `03_sample_data.sql` | The original 17 tables, scheduling rules and sample data (port of `db/mysql`) |
| `db/postgres/04_accounts.sql` | Logins, and what a patient login may write |
| `db/postgres/05_billing.sql` | Invoices, payments, insurance; totals and the superbill are views |
| `db/postgres/06_care.sql` | Waitlist, visit notes, notifications (written by triggers), `sp_book_appointment` |
| `db/postgres/07_sample_data_extended.sql` | Sample data for 04–06 |
| `db/postgres/reports/` | The eight management reports |
| `ClinicSchedulerV2.Data` | EF Core mapping onto the schema (no migrations), script runner, services |
| `ClinicSchedulerV2.Web` | Blazor Server + MudBlazor app for staff, providers and patients |
| `ClinicSchedulerV2.Tests` | 86 tests against a real PostgreSQL server |
| `../ClinicScheduler/ClinicScheduler.Mcp` | MCP server over this database (see its README) |

Scripts are applied in file-name order and recorded in a `schema_script` table, so each runs once
per database and new scripts reach existing databases on the next start. Nothing is ever dropped.

## Run it

1. Create `ClinicSchedulerV2.Web/appsettings.Development.json` (it is gitignored):

   ```json
   {
     "ConnectionStrings": {
       "ClinicV2": "Host=localhost;Database=clinic_scheduler_v2;Username=postgres;Password=YOUR_PASSWORD"
     },
     "Database": {
       "AutoCreate": true,
       "SeedSampleData": true,
       "DemoPassword": "choose-one-10-chars-or-more"
     }
   }
   ```

2. `dotnet run --project ClinicSchedulerV2/ClinicSchedulerV2.Web`, then open http://localhost:5052.

On first start the app creates the database, loads the sample data, and creates demo logins
using `DemoPassword` (no password is committed anywhere):

| User name | Who | Sees |
|---|---|---|
| `areyes` | Angela Reyes, clinic manager | Everything staff see, plus account management |
| `kbrooks` | Kevin Brooks, scheduler | Schedule, calendar, requests, waitlist, billing, reports |
| `mgonzalez` | Dr. Maria Gonzalez, MD | Her day, calendar, visit notes |
| `rkim` | Rachel Kim, RN | Her day, calendar, visit notes |
| `lcarter`, `dramirez` | Patients | Their visits, requests, waitlist, invoices |

Roles are not stored: an account belongs to an employee or a patient, and staff, provider,
manager or patient follows from that.

## Tests

```bash
dotnet test ClinicSchedulerV2/ClinicSchedulerV2.Tests
```

They need a PostgreSQL server; by default `Host=localhost;Username=postgres;Password=postgres`,
overridable with `CLINIC_V2_TEST_SERVER`. Each test class creates a throwaway database, applies
the scripts and drops it. CI runs them against a `postgres:17` service container.

## Trust model

The database is told who is acting through session settings (`app.current_user_id`, or
`app.current_employee_id` for a service without a login such as the MCP server), and its triggers
enforce what each kind of user may write. It trusts the application to set them, as the original
design did. Reads are scoped by the application.
