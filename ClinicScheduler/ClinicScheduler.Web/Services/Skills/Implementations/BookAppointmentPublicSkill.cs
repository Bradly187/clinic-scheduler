using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Core.Services;
using ClinicScheduler.Infrastructure.Data;
using ClinicScheduler.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>
/// Public-facing skill: books an appointment for a patient identified by email.
/// If the patient exists and email matches, books directly.
/// If no patient found, creates a pending AppointmentRequest.
/// </summary>
public sealed class BookAppointmentPublicSkill : ISkill
{
    private readonly IDbContextFactory<ClinicDbContext> _dbFactory;
    private readonly IRepository<Therapist> _therapistRepo;
    private readonly IRepository<Room> _roomRepo;
    private readonly AppointmentSchedulingService _schedulingService;
    private readonly IClinicTimeFormatter _clock;

    public BookAppointmentPublicSkill(
        IDbContextFactory<ClinicDbContext> dbFactory,
        IRepository<Therapist> therapistRepo,
        IRepository<Room> roomRepo,
        AppointmentSchedulingService schedulingService,
        IClinicTimeFormatter clock)
    {
        _dbFactory = dbFactory;
        _therapistRepo = therapistRepo;
        _roomRepo = roomRepo;
        _schedulingService = schedulingService;
        _clock = clock;
    }

    public string Name => "book_appointment_public";

    public JsonObject GetSchema() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = "book_appointment_public",
            ["description"] = "Books an appointment for a patient. Requires the patient's email for identification. If the patient is not registered, creates a pending request for staff approval.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["patientEmail"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Patient's email address for identification."
                    },
                    ["therapistName"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Full or partial name of the desired therapist."
                    },
                    ["date"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Appointment date in YYYY-MM-DD format."
                    },
                    ["startTime"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Appointment start time in HH:MM (24-hour) format."
                    },
                    ["reason"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Brief reason for the visit."
                    }
                },
                ["required"] = new JsonArray { "patientEmail", "therapistName", "date", "startTime" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonObject? arguments)
    {
        var email = arguments?["patientEmail"]?.GetValue<string>();
        var therapistName = arguments?["therapistName"]?.GetValue<string>();
        var dateStr = arguments?["date"]?.GetValue<string>();
        var startTimeStr = arguments?["startTime"]?.GetValue<string>();
        var reason = arguments?["reason"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(email)) return "Error: patientEmail is required.";
        if (string.IsNullOrWhiteSpace(therapistName)) return "Error: therapistName is required.";
        if (string.IsNullOrWhiteSpace(dateStr)) return "Error: date is required (YYYY-MM-DD).";
        if (string.IsNullOrWhiteSpace(startTimeStr)) return "Error: startTime is required (HH:MM).";

        if (!DateOnly.TryParse(dateStr, out var parsedDate))
            return $"Error: Could not parse date '{dateStr}'. Use YYYY-MM-DD format.";
        if (!TimeOnly.TryParse(startTimeStr, out var parsedTime))
            return $"Error: Could not parse time '{startTimeStr}'. Use HH:MM format.";

        // Resolve therapist
        var therapists = await _therapistRepo.FindAsync(
            t => t.FirstName.Contains(therapistName) || t.LastName.Contains(therapistName));
        if (therapists.Count == 0)
            return $"Error: No therapist found matching '{therapistName}'.";
        if (therapists.Count > 1)
            return $"Multiple therapists match '{therapistName}': {string.Join(", ", therapists.Select(t => $"{t.FirstName} {t.LastName}"))}. Please be more specific.";
        var therapist = therapists.First();

        await using var db = await _dbFactory.CreateDbContextAsync();
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Email == email);

        var startDateTime = new DateTime(parsedDate.Year, parsedDate.Month, parsedDate.Day,
            parsedTime.Hour, parsedTime.Minute, 0, DateTimeKind.Utc);

        if (patient is not null)
        {
            // Known patient — book directly
            var rooms = await _roomRepo.GetAllAsync();
            var room = rooms.FirstOrDefault();
            if (room is null)
                return "Error: No rooms are available in the system.";

            try
            {
                var appointment = await _schedulingService.CreateAppointmentAsync(
                    patient.Id, therapist.Id, room.Id, startDateTime);

                return $"Appointment booked successfully! " +
                       $"Date: {_clock.Format(appointment.StartTime)}, " +
                       $"Therapist: {therapist.FirstName} {therapist.LastName}. " +
                       $"You will receive a confirmation reminder before your appointment.";
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return $"Could not book that slot: {ex.Message}. Please try a different time.";
            }
        }
        else
        {
            // Unknown patient — create a pending appointment request
            // We'll create a minimal patient-less request by storing in AppointmentRequests
            // with a note containing the email for staff to follow up
            var newPatient = new Patient("New", "Patient", email, DateOnly.FromDateTime(DateTime.UtcNow));
            db.Patients.Add(newPatient);
            await db.SaveChangesAsync();

            var request = new AppointmentRequest(newPatient, reason ?? "Booked via online scheduling", therapist);
            request.SetPreferredDateTime(startDateTime);
            db.AppointmentRequests.Add(request);
            await db.SaveChangesAsync();

            return $"Your appointment request has been submitted for {_clock.Format(startDateTime)} with " +
                   $"{therapist.FirstName} {therapist.LastName}. " +
                   $"Since this is your first visit, our staff will confirm your appointment and " +
                   $"send a confirmation to {email}. Please check your email for next steps.";
        }
    }
}
