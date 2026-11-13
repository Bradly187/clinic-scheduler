-- =====================================================================
-- ClinicScheduler Database: Trigger, Procedure and View Tests
-- CSCI 380 Project Part 2 | John Tarver
-- Run after schema.sql, sample_data.sql and triggers.sql.
--
-- HOW TO RUN: several tests are SUPPOSED to fail with an error. Most SQL
-- tools stop a script at the first error, so run this file one statement
-- at a time (in Workbench: put the cursor on a statement and press
-- Ctrl+Enter). Each test says what to expect. Screenshot each result or
-- error message for the report.
--
-- Everything runs inside one transaction and is rolled back at the end,
-- so the sample data is unchanged afterward.
-- =====================================================================

USE clinic_db;
START TRANSACTION;

-- Identify the staff member making changes (used by the audit triggers)
SET @current_employee_id = 13;   -- Monica Patel, Scheduler
SET @audit_start = (SELECT COALESCE(MAX(audit_id), 0) FROM AuditLog);

-- =====================================================================
-- SCHEDULING RULES (trg_appointment_before_insert)
-- =====================================================================

-- T1. Provider double-booking
-- Dr. Gonzalez (1) already sees Linda Carter on Nov 23 at 11:00 (appt 29).
-- Expected: ERROR "Provider 1 is already booked at that time (appointment 29)"
INSERT INTO Appointment (patient_id, provider_id, room_id, service_type_id, created_by, start_time, end_time)
VALUES (3, 1, 2, 2, 9, '2026-11-23 11:00:00', '2026-11-23 11:30:00');

-- T2. Patient double-booking
-- Linda Carter (1) has a lab draw on Nov 30 at 08:00 (appt 32).
-- Expected: ERROR "Patient 1 already has an appointment at that time (appointment 32)"
INSERT INTO Appointment (patient_id, provider_id, room_id, service_type_id, created_by, start_time, end_time)
VALUES (1, 1, 1, 2, 9, '2026-11-30 08:00:00', '2026-11-30 08:30:00');

-- T3. Room type must match the service
-- An IV infusion booked in Exam 1 (a Standard room).
-- Expected: ERROR "Room type mismatch: this service requires room type Infusion, but room 1 is Standard"
INSERT INTO Appointment (patient_id, provider_id, room_id, service_type_id, created_by, start_time, end_time)
VALUES (3, 5, 1, 3, 9, '2026-12-07 09:00:00', '2026-12-07 11:00:00');

-- T4. Provider must be qualified
-- Rachel Kim, RN (5) booked for a follow-up visit (MD/NP/PA only).
-- Expected: ERROR "Provider 5 is not qualified for service 2"
INSERT INTO Appointment (patient_id, provider_id, room_id, service_type_id, created_by, start_time, end_time)
VALUES (3, 5, 1, 2, 9, '2026-12-07 09:00:00', '2026-12-07 09:30:00');

-- T5. Provider must work at the room's location
-- Dr. Whitfield (2) works at Memorial City and Clear Lake, not Medical Center.
-- Expected: ERROR "Provider 2 does not work at location 1"
INSERT INTO Appointment (patient_id, provider_id, room_id, service_type_id, created_by, start_time, end_time)
VALUES (3, 2, 1, 2, 9, '2026-12-07 09:00:00', '2026-12-07 09:30:00');

-- T6. Infusion exemption: allowed
-- Emily Tran, RN (7) already runs an infusion in the Clear Lake suite on
-- Dec 3, 13:00-15:00 (appt 38). A second, overlapping infusion in the
-- SAME suite is allowed.
-- Expected: 1 row inserted
INSERT INTO Appointment (patient_id, provider_id, room_id, service_type_id, created_by, start_time, end_time)
VALUES (12, 7, 15, 3, 12, '2026-12-03 13:30:00', '2026-12-03 15:30:00');

-- T7. Infusion exemption: different room is still blocked
-- The same RN at the same time in the Memorial City suite.
-- Expected: ERROR "Provider 7 is already booked at that time (appointment 38)"
INSERT INTO Appointment (patient_id, provider_id, room_id, service_type_id, created_by, start_time, end_time)
VALUES (11, 7, 10, 3, 13, '2026-12-03 13:30:00', '2026-12-03 15:30:00');

-- T8. Room capacity
-- The Medical Center infusion suite (room 5, 3 chairs) has 2 infusions on
-- Nov 30 at 09:00. The first insert takes the 3rd chair; the second
-- would be a 4th patient.
-- Expected: first insert succeeds; second gives ERROR "Room 5 is full at that time (capacity 3)"
INSERT INTO Appointment (patient_id, provider_id, room_id, service_type_id, created_by, start_time, end_time)
VALUES (2, 6, 5, 3, 10, '2026-11-30 09:00:00', '2026-11-30 11:00:00');

INSERT INTO Appointment (patient_id, provider_id, room_id, service_type_id, created_by, start_time, end_time)
VALUES (3, 6, 5, 3, 10, '2026-11-30 09:00:00', '2026-11-30 11:00:00');

-- T9. Location capacity
-- Medical Center now has 3 patients at 09:00 on Nov 30. For the test,
-- lower its limit from 12 to 3 (rolled back at the end), then try a 4th.
-- Expected: UPDATE succeeds; INSERT gives ERROR "Location 1 is at its limit of 3 concurrent patients at that time"
UPDATE Location SET max_concurrent_patients = 3 WHERE location_id = 1;

INSERT INTO Appointment (patient_id, provider_id, room_id, service_type_id, created_by, start_time, end_time)
VALUES (7, 1, 1, 2, 9, '2026-11-30 09:00:00', '2026-11-30 09:30:00');

UPDATE Location SET max_concurrent_patients = 12 WHERE location_id = 1;

-- =====================================================================
-- SUBTYPE RULE (trg_staff_before_insert)
-- =====================================================================

-- T10. An employee cannot be both Provider and Staff
-- Dr. Gonzalez (1) is a Provider.
-- Expected: ERROR "Employee is already a Provider; an employee cannot be both Provider and Staff"
INSERT INTO Staff (employee_id, job_title) VALUES (1, 'Front Desk');

-- =====================================================================
-- CANCELLATION RULES (trg_cancel_*)
-- =====================================================================

-- T11. Only one pending cancellation per appointment
-- Appointment 26 already has a pending request.
-- Expected: ERROR "This appointment already has a pending cancellation request"
INSERT INTO CancellationRequest (appointment_id, source, reason)
VALUES (26, 'Patient', 'Asking again');

-- T12. Only a Scheduled appointment can be cancelled
-- Appointment 1 is Completed.
-- Expected: ERROR "Only a Scheduled appointment can receive a cancellation request"
INSERT INTO CancellationRequest (appointment_id, source, reason)
VALUES (1, 'Patient', 'Too late');

-- T13. Clinic cancellations are approved automatically
-- No status is given; the trigger approves it and cancels appointment 28.
-- Expected: request shows Approved, reviewed_by 12; appointment 28 shows Canceled
INSERT INTO CancellationRequest (appointment_id, source, submitted_by, reason)
VALUES (28, 'Clinic', 12, 'Provider called in sick');

SELECT cr.cancel_request_id, cr.source, cr.status, cr.submitted_by, cr.reviewed_by,
       a.appointment_id, a.status AS appointment_status
FROM CancellationRequest cr
JOIN Appointment a ON a.appointment_id = cr.appointment_id
WHERE cr.appointment_id = 28;

-- T14. Approving a patient cancellation cancels the appointment
-- Request 8 (appointment 29) is Pending; staff approve it.
-- Expected: reviewed_at filled in automatically; appointment 29 shows Canceled
UPDATE CancellationRequest SET status = 'Approved', reviewed_by = 9
WHERE cancel_request_id = 8;

SELECT cr.cancel_request_id, cr.status, cr.reviewed_by,
       DATE_FORMAT(cr.reviewed_at, '%Y-%m-%d %H:%i') AS reviewed_at,
       a.appointment_id, a.status AS appointment_status
FROM CancellationRequest cr
JOIN Appointment a ON a.appointment_id = cr.appointment_id
WHERE cr.cancel_request_id = 8;

-- =====================================================================
-- STORED PROCEDURES
-- =====================================================================

-- T15. Approve an appointment request
-- Request 7 (David Ramirez, follow-up). Book with Dr. Gonzalez in
-- Memorial City Exam 1 (room 6) on Dec 7 at 10:00.
-- Expected: the new appointment is shown; request 7 becomes Approved and links to it
CALL sp_approve_request(7, 13, 1, 6, '2026-12-07 10:00:00');

SELECT request_id, status, reviewed_by, appointment_id
FROM AppointmentRequest WHERE request_id = 7;

-- T16. Deny an appointment request
-- Expected: request 8 becomes Denied with the note
CALL sp_deny_request(8, 12, 'Labs not due until January per treatment plan');

SELECT request_id, status, reviewed_by, decision_note
FROM AppointmentRequest WHERE request_id = 8;

-- T17. Reschedule a missed appointment
-- Jennifer Lee missed her Nov 18 follow-up (appointment 27). The procedure
-- finds the first open slot after today with the same provider.
-- Expected: the new appointment is shown, with rescheduled_from_id = 27
UPDATE Appointment SET status = 'Missed' WHERE appointment_id = 27;

CALL sp_reschedule_missed(27, 13, @new_id);

SELECT appointment_id, rescheduled_from_id, provider_id, room_id,
       DATE_FORMAT(start_time, '%Y-%m-%d %H:%i') AS start_time, status, notes
FROM Appointment WHERE appointment_id = @new_id;

-- T18. Only a Missed appointment can be rescheduled
-- Appointment 28 was canceled in T13.
-- Expected: ERROR "Only a Missed appointment can be rescheduled"
CALL sp_reschedule_missed(28, 13, @new_id2);

-- =====================================================================
-- VIEWS
-- =====================================================================

-- T19. Dr. Gonzalez's upcoming schedule
SELECT appt_date, start_time, end_time, patient, service, room_name, location, status
FROM vw_provider_schedule
WHERE provider_id = 1 AND appt_date >= '2026-11-13'
ORDER BY appt_date, start_time;

-- T20. Medical Center infusion suite on Nov 30 (3 of 3 chairs after T8)
SELECT room_name, capacity, start_time, end_time, patient, provider, service
FROM vw_room_schedule
WHERE room_id = 5 AND appt_date = '2026-11-30'
ORDER BY start_time;

-- T21. Staff work queue of pending requests
SELECT request_type, request_id, patient, service, requested_time, details,
       DATE_FORMAT(submitted_at, '%Y-%m-%d %H:%i') AS submitted_at
FROM vw_pending_requests
ORDER BY submitted_at;

-- =====================================================================
-- AUDIT LOG
-- =====================================================================

-- T22. Every change made by the tests above, recorded by the audit triggers
SELECT audit_id, table_name, record_id, action, changed_by,
       old_values, new_values
FROM AuditLog
WHERE audit_id > @audit_start
ORDER BY audit_id;

-- Undo all test changes
ROLLBACK;
