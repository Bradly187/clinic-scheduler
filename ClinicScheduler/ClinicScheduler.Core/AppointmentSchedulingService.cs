using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Exceptions;
using ClinicScheduler.Core.Interfaces;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
namespace ClinicScheduler.Core.Services;

public class AppointmentSchedulingService
{
    /// <summary>
    /// Default slot length, used when a location is missing or has no explicit configuration.
    /// Per-location values live in <see cref="Location.SlotDurationMinutes"/>.
    /// </summary>
    public const int SlotDurationMinutes = Location.DefaultSlotDurationMinutes;

    public static readonly TimeSpan SlotDuration = TimeSpan.FromMinutes(SlotDurationMinutes);

    // Fallback operating hours for locations with no TimeSlot configuration
    private static readonly TimeOnly DefaultOpen  = new(8, 0);
    private static readonly TimeOnly DefaultClose = new(17, 0);

    private readonly IRepository<Appointment> _appointmentRepository;
    private readonly IRepository<Patient> _patientRepository;
    private readonly IRepository<Therapist> _therapistRepository;
    private readonly IRepository<Room> _roomRepository;
    private readonly IRepository<TimeSlot> _timeSlotRepository;
    private readonly IRepository<Location> _locationRepository;
    private readonly IRepository<TherapistShift> _therapistShiftRepository;
    private readonly IFhirSyncService _fhirSyncService;
    private readonly ILogger<AppointmentSchedulingService> _logger;

    private static readonly ActivitySource _activitySource = new("ClinicScheduler.BusinessLogic");
    private static readonly Meter _meter = new("ClinicScheduler.BusinessLogic");
    
    private static readonly Counter<int> _appointmentsScheduled = _meter.CreateCounter<int>("clinic.appointments.scheduled", description: "Number of appointments scheduled");
    private static readonly Counter<int> _appointmentsRescheduled = _meter.CreateCounter<int>("clinic.appointments.rescheduled", description: "Number of appointments rescheduled");
    private static readonly Counter<int> _capacityRejections = _meter.CreateCounter<int>("clinic.appointments.capacity_rejections", description: "Number of appointment requests rejected due to capacity limits");

    public AppointmentSchedulingService(
        IRepository<Appointment> appointmentRepository,
        IRepository<Patient> patientRepository,
        IRepository<Therapist> therapistRepository,
        IRepository<Room> roomRepository,
        IRepository<TimeSlot> timeSlotRepository,
        IRepository<Location> locationRepository,
        IRepository<TherapistShift> therapistShiftRepository,
        IFhirSyncService fhirSyncService,
        ILogger<AppointmentSchedulingService> logger)
    {
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _therapistRepository = therapistRepository;
        _roomRepository = roomRepository;
        _timeSlotRepository = timeSlotRepository;
        _locationRepository = locationRepository;
        _therapistShiftRepository = therapistShiftRepository;
        _fhirSyncService = fhirSyncService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new appointment using the room's location slot duration. Convenience
    /// overload for callers that don't supply an explicit duration.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown on conflict or capacity exceeded.</exception>
    /// <exception cref="ArgumentException">Thrown if entities not found or slot is invalid.</exception>
    public async Task<Appointment> CreateAppointmentAsync(
        int patientId,
        int therapistId,
        int roomId,
        DateTime startTime,
        CancellationToken ct = default,
        TherapyType? therapyType = null)
    {
        var room = await _roomRepository.GetByIdAsync(roomId, ct)
            ?? throw new ArgumentException("Room not found.", nameof(roomId));

        var location = await _locationRepository.GetByIdAsync(room.LocationId, ct);
        var therapist = await _therapistRepository.GetByIdAsync(therapistId, ct);
        var slotLength = TimeSpan.FromMinutes(therapist?.SlotDurationMinutes ?? location?.SlotDurationMinutes ?? Location.DefaultSlotDurationMinutes);

        return await CreateAppointmentAsync(patientId, therapistId, roomId, startTime, slotLength, ct, therapyType);
    }

    /// <summary>
    /// Creates a new appointment after validating slot rules and conflicts.
    /// Uses location-aware validation: derives the location from the room, validates
    /// against the location's TimeSlot windows and SlotDurationMinutes, and enforces
    /// Location.DailyCapacity. Creates ScheduleConflict records when conflicts are detected.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown on conflict or capacity exceeded.</exception>
    /// <exception cref="ArgumentException">Thrown if entities not found or slot is invalid.</exception>
    public async Task<Appointment> CreateAppointmentAsync(
        int patientId,
        int therapistId,
        int roomId,
        DateTime startTime,
        TimeSpan duration,
        CancellationToken ct = default,
        TherapyType? therapyType = null)
    {
        using var activity = _activitySource.StartActivity("CreateAppointment");
        activity?.SetTag("appointment.patientId", patientId);
        activity?.SetTag("appointment.therapistId", therapistId);
        activity?.SetTag("appointment.roomId", roomId);

        var patient = await _patientRepository.GetByIdAsync(patientId, ct)
            ?? throw new ArgumentException("Patient not found.", nameof(patientId));

        var therapist = await _therapistRepository.GetByIdAsync(therapistId, ct)
            ?? throw new ArgumentException("Therapist not found.", nameof(therapistId));

        var room = await _roomRepository.GetByIdAsync(roomId, ct)
            ?? throw new ArgumentException("Room not found.", nameof(roomId));

        // Derive locationId from the room
        var locationId = room.LocationId;

        var location = await _locationRepository.GetByIdAsync(locationId, ct)
            ?? throw new ArgumentException("Location not found.");

        // Location-aware time slot validation (operating windows + slot alignment)
        await ValidateSlotAsync(startTime, location, therapistId, ct);
        
        var slotLength = TimeSpan.FromMinutes(therapist.SlotDurationMinutes ?? location.SlotDurationMinutes);

        var endTime = startTime.Add(duration);

        if (duration != slotLength)
            throw new ArgumentException(
                $"Appointments for this therapist/location must be exactly {slotLength.TotalMinutes} minutes.",
                nameof(duration));

        var overlapping = await _appointmentRepository.FindAsync(a =>
            a.Status != AppointmentStatus.Canceled && a.Status != AppointmentStatus.Missed &&
            a.StartTime < endTime && a.EndTime > startTime, ct);

        var overlappingList = overlapping.ToList();

        if (overlappingList.Any(a => a.TherapistId == therapistId))
            throw new InvalidOperationException("The selected therapist is unavailable at the requested time.");

        if (overlappingList.Any(a => a.RoomId == roomId))
            throw new InvalidOperationException("The selected room is unavailable at the requested time.");

        if (overlappingList.Any(a => a.PatientId == patientId))
            throw new InvalidOperationException("The patient is already scheduled for another appointment at the requested time.");

        // Create the appointment first so we can attach conflicts to it
        var newAppointment = new Appointment(patient, therapist, room, startTime, duration, therapyType);

        // Check location daily capacity: count distinct patients with active appointments
        // at the location on the appointment date
        var appointmentDate = startTime.Date;
        var nextDate = appointmentDate.AddDays(1);

        // Find all rooms at this location
        var locationRooms = await _roomRepository.FindAsync(r => r.LocationId == locationId, ct);
        var locationRoomIds = locationRooms.Select(r => r.Id).ToHashSet();

        // Count distinct patients with active appointments at this location on the same date
        var dailyAppointments = await _appointmentRepository.FindAsync(a =>
            a.Status != AppointmentStatus.Canceled && a.Status != AppointmentStatus.Missed &&
            a.StartTime >= appointmentDate && a.StartTime < nextDate, ct);

        var locationDailyAppointments = dailyAppointments
            .Where(a => locationRoomIds.Contains(a.RoomId))
            .ToList();

        var distinctPatientCount = locationDailyAppointments
            .Select(a => a.PatientId)
            .Distinct()
            .Count();

        // If the current patient is not already among today's patients, they would be a new addition
        var patientAlreadyScheduledToday = locationDailyAppointments.Any(a => a.PatientId == patientId);
        var effectivePatientCount = patientAlreadyScheduledToday ? distinctPatientCount : distinctPatientCount + 1;

        if (effectivePatientCount > location.DailyCapacity)
        {
            _logger.LogWarning("Location {LocationId} daily capacity reached. Could not schedule patient {PatientId}.", location.Id, patientId);
            _capacityRejections.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, "Capacity exceeded");
            // Nothing is persisted: a rejected booking must not leave artifacts behind
            throw new CapacityExceededException(
                $"Location daily capacity reached: cannot schedule more than {location.DailyCapacity} patients on this date.");
        }

        newAppointment = await _appointmentRepository.AddAsync(newAppointment, ct);
        
        _appointmentsScheduled.Add(1);
        _logger.LogInformation("Successfully scheduled appointment {AppointmentId} for patient {PatientId} with therapist {TherapistId} at {StartTime}.", 
            newAppointment.Id, patientId, therapistId, startTime);

        // Sync to EHR in background
        _ = _fhirSyncService.SyncAppointmentAsync(newAppointment, ct);
        
        return newAppointment;
    }

    /// <summary>
    /// Validates that the appointment start time falls within one of the location's configured
    /// TimeSlot windows for the given day of week, aligned to the location's slot duration.
    /// If no TimeSlot records exist for the location/day, falls back to the default
    /// 8:00 AM–5:00 PM weekday schedule.
    /// </summary>
    /// <param name="startTime">The proposed appointment start time.</param>
    /// <param name="locationId">The location to validate against.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException">Thrown if the start time is outside configured hours or misaligned.</exception>
    public async Task ValidateSlotForLocation(DateTime startTime, int locationId, int therapistId, CancellationToken ct = default)
    {
        var location = await _locationRepository.GetByIdAsync(locationId, ct);
        await ValidateSlotAsync(startTime, location, therapistId, ct, locationId);
    }

    private async Task ValidateSlotAsync(DateTime startTime, Location? location, int therapistId, CancellationToken ct, int? locationIdOverride = null)
    {
        var locationId = locationIdOverride ?? location!.Id;
        var therapist = await _therapistRepository.GetByIdAsync(therapistId, ct);
        var slotMinutes = therapist?.SlotDurationMinutes ?? location?.SlotDurationMinutes ?? Location.DefaultSlotDurationMinutes;

        var windows = await GetOperatingWindowsAsync(locationId, therapistId, startTime.DayOfWeek, ct);

        var appointmentStart = TimeOnly.FromDateTime(startTime);
        var startMinutes = (int)appointmentStart.ToTimeSpan().TotalMinutes;
        var endMinutes = startMinutes + slotMinutes;

        // Find a window that fully contains the appointment
        var containing = windows
            .Where(w => startMinutes >= w.StartMinutes && endMinutes <= w.EndMinutes)
            .ToList();

        if (containing.Count == 0)
            throw new ArgumentException(
                "Appointment time is outside the configured schedule for this location.");

        // The start must align to a slot boundary measured from the window start
        var aligned = containing.Any(w => (startMinutes - w.StartMinutes) % slotMinutes == 0);
        if (!aligned)
            throw new ArgumentException(
                $"Appointments must start on a {slotMinutes}-minute boundary within the location's operating hours.");
    }

    /// <summary>
    /// Returns the operating windows (in minutes from midnight) for a location on a given
    /// day of week: the location's TimeSlot records when configured, otherwise the default
    /// 8:00 AM–5:00 PM weekday schedule (empty on weekends).
    /// </summary>
    private async Task<IReadOnlyList<(int StartMinutes, int EndMinutes)>> GetOperatingWindowsAsync(
        int locationId, int therapistId, DayOfWeek dayOfWeek, CancellationToken ct)
    {
        var therapistShifts = await _therapistShiftRepository.FindAsync(
            ts => ts.TherapistId == therapistId && ts.LocationId == locationId && ts.DayOfWeek == dayOfWeek, ct);

        if (therapistShifts.Count > 0)
        {
            return therapistShifts
                .Select(ts => (
                    (int)ts.StartTime.ToTimeSpan().TotalMinutes,
                    (int)ts.EndTime.ToTimeSpan().TotalMinutes))
                .ToList();
        }

        var timeSlots = await _timeSlotRepository.FindAsync(
            ts => ts.LocationId == locationId && ts.DayOfWeek == dayOfWeek, ct);

        if (timeSlots.Count > 0)
        {
            return timeSlots
                .Select(ts => (
                    (int)ts.StartTime.ToTimeSpan().TotalMinutes,
                    (int)ts.EndTime.ToTimeSpan().TotalMinutes))
                .ToList();
        }

        if (dayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return [];

        return
        [
            ((int)DefaultOpen.ToTimeSpan().TotalMinutes, (int)DefaultClose.ToTimeSpan().TotalMinutes)
        ];
    }

    /// <summary>
    /// Returns every valid appointment start time for a location on the given date,
    /// derived from the location's operating windows and slot duration.
    /// </summary>
    public async Task<IReadOnlyList<DateTime>> GetDailySlotStartsAsync(
        int locationId, int therapistId, DateTime date, CancellationToken ct = default)
    {
        var location = await _locationRepository.GetByIdAsync(locationId, ct);
        var therapist = await _therapistRepository.GetByIdAsync(therapistId, ct);
        var slotMinutes = therapist?.SlotDurationMinutes ?? location?.SlotDurationMinutes ?? Location.DefaultSlotDurationMinutes;
        return await GetDailySlotStartsAsync(locationId, therapistId, slotMinutes, date, ct);
    }

    /// <summary>
    /// Returns every valid appointment start time for the room's location on the given date,
    /// along with the location's slot duration. Used by reschedule flows that need both.
    /// </summary>
    public async Task<(IReadOnlyList<DateTime> SlotStarts, TimeSpan SlotLength)> GetDailySlotsForRoomAsync(
        int roomId, int therapistId, DateTime date, CancellationToken ct = default)
    {
        var room = await _roomRepository.GetByIdAsync(roomId, ct)
            ?? throw new ArgumentException("Room not found.", nameof(roomId));

        var location = await _locationRepository.GetByIdAsync(room.LocationId, ct);
        var therapist = await _therapistRepository.GetByIdAsync(therapistId, ct);
        var slotMinutes = therapist?.SlotDurationMinutes ?? location?.SlotDurationMinutes ?? Location.DefaultSlotDurationMinutes;
        var slots = await GetDailySlotStartsAsync(room.LocationId, therapistId, slotMinutes, date, ct);
        return (slots, TimeSpan.FromMinutes(slotMinutes));
    }

    private async Task<IReadOnlyList<DateTime>> GetDailySlotStartsAsync(
        int locationId, int therapistId, int slotMinutes, DateTime date, CancellationToken ct)
    {
        var windows = await GetOperatingWindowsAsync(locationId, therapistId, date.DayOfWeek, ct);

        var slots = new List<DateTime>();
        foreach (var (startMinutes, endMinutes) in windows)
        {
            for (var m = startMinutes; m + slotMinutes <= endMinutes; m += slotMinutes)
                slots.Add(date.Date.AddMinutes(m));
        }

        slots.Sort();
        return slots;
    }

    /// <summary>
    /// Finds the next available slot for the same therapist/room as the missed appointment
    /// and creates a replacement appointment. Searches forward up to 30 calendar days using
    /// the location's configured hours and slot duration.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if no open slot is found within 30 days.</exception>
    public async Task<Appointment> RescheduleAfterMissedAsync(
        Appointment missed,
        CancellationToken ct = default)
    {
        using var activity = _activitySource.StartActivity("RescheduleAfterMissed");
        activity?.SetTag("appointment.originalId", missed.Id);
        activity?.SetTag("appointment.patientId", missed.PatientId);

        if (missed.Status != AppointmentStatus.Missed)
        {
            _logger.LogWarning("Attempted to reschedule appointment {AppointmentId} which is not marked as Missed. Current status: {Status}", missed.Id, missed.Status);
            throw new ArgumentException("Appointment must be marked as Missed before rescheduling.", nameof(missed));
        }

        var patient = await _patientRepository.GetByIdAsync(missed.PatientId, ct)
            ?? throw new InvalidOperationException("Patient not found for missed appointment.");

        var therapist = await _therapistRepository.GetByIdAsync(missed.TherapistId, ct)
            ?? throw new InvalidOperationException("Therapist not found for missed appointment.");

        var room = await _roomRepository.GetByIdAsync(missed.RoomId, ct)
            ?? throw new InvalidOperationException("Room not found for missed appointment.");

        var locationId = room.LocationId;
        var location = await _locationRepository.GetByIdAsync(locationId, ct);
        var slotMinutes = therapist.SlotDurationMinutes ?? location?.SlotDurationMinutes ?? Location.DefaultSlotDurationMinutes;
        var slotLength = TimeSpan.FromMinutes(slotMinutes);

        var missedTime = TimeOnly.FromDateTime(missed.StartTime);
        // Always reschedule at least 7 days from now, regardless of when the
        // original appointment was.  This avoids booking into the past when an
        // old appointment is marked missed and gives the patient reasonable notice.
        var searchStart = DateTime.UtcNow.Date.AddDays(7);
        var searchEnd = searchStart.AddDays(30);

        // Gather every valid slot in the window from the location's configured hours
        var allSlots = new List<DateTime>();
        for (var day = searchStart; day < searchEnd; day = day.AddDays(1))
            allSlots.AddRange(await GetDailySlotStartsAsync(locationId, therapist.Id, slotMinutes, day, ct));

        // Try same time-of-day first across the window, then all other slots
        var candidateSlots = allSlots
            .Where(s => TimeOnly.FromDateTime(s) == missedTime)
            .Concat(allSlots.Where(s => TimeOnly.FromDateTime(s) != missedTime));

        // Hoist location room IDs for the capacity check
        var locationRooms = await _roomRepository.FindAsync(r => r.LocationId == locationId, ct);
        var locationRoomIds = locationRooms.Select(r => r.Id).ToHashSet();

        foreach (var slot in candidateSlots)
        {
            var slotEnd = slot.Add(slotLength);

            var overlapping = await _appointmentRepository.FindAsync(a =>
                a.Status != AppointmentStatus.Canceled && a.Status != AppointmentStatus.Missed &&
                a.StartTime < slotEnd && a.EndTime > slot, ct);

            var overlappingList = overlapping.ToList();

            if (overlappingList.Any(a => a.TherapistId == missed.TherapistId)) continue;
            if (overlappingList.Any(a => a.RoomId == missed.RoomId)) continue;
            if (overlappingList.Any(a => a.PatientId == missed.PatientId)) continue;

            // Check location daily capacity
            if (location != null)
            {
                var appointmentDate = slot.Date;
                var nextDate = appointmentDate.AddDays(1);

                var dailyAppointments = await _appointmentRepository.FindAsync(a =>
                    a.Status != AppointmentStatus.Canceled && a.Status != AppointmentStatus.Missed &&
                    a.StartTime >= appointmentDate && a.StartTime < nextDate, ct);

                var locationDailyAppointments = dailyAppointments
                    .Where(a => locationRoomIds.Contains(a.RoomId))
                    .ToList();

                var distinctPatientCount = locationDailyAppointments
                    .Select(a => a.PatientId)
                    .Distinct()
                    .Count();

                var patientAlreadyScheduledToday = locationDailyAppointments.Any(a => a.PatientId == missed.PatientId);
                var effectivePatientCount = patientAlreadyScheduledToday ? distinctPatientCount : distinctPatientCount + 1;

                if (effectivePatientCount > location.DailyCapacity) continue;
            }

            var rescheduled = new Appointment(patient, therapist, room, slot, slotLength, missed.TherapyType);
            rescheduled.TreatmentPlanId = missed.TreatmentPlanId;
            var result = await _appointmentRepository.AddAsync(rescheduled, ct);
            
            _appointmentsRescheduled.Add(1);
            _logger.LogInformation("Successfully rescheduled missed appointment {OriginalAppointmentId}. New appointment {NewAppointmentId} at {StartTime}.", missed.Id, result.Id, slot);

            _ = _fhirSyncService.SyncAppointmentAsync(result, ct);
            
            return result;
        }

        _logger.LogError("Failed to reschedule missed appointment {AppointmentId}. No available time slot found within 30 days.", missed.Id);
        activity?.SetStatus(ActivityStatusCode.Error, "No available time slot found");
        throw new InvalidOperationException(
            "No available time slot found within the next 30 days for this therapist and room.");
    }
}
