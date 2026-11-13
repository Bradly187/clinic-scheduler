-- =====================================================================
-- ClinicScheduler Database: Triggers, Stored Procedures and Views
-- CSCI 380 Project Part 2 | John Tarver
-- Run after schema.sql and sample_data.sql. Safe to re-run.
--
-- Contents
--   1. sp_validate_appointment   shared scheduling-rule checks
--   2. Appointment triggers      run the checks on insert and update
--   3. Subtype triggers          an employee is a Provider or Staff, not both
--   4. Cancellation triggers     one pending request per appointment,
--                                clinic requests auto-approved, approval
--                                cancels the appointment
--   5. Audit triggers            Appointment, AppointmentRequest and
--                                CancellationRequest changes -> AuditLog
--   6. Stored procedures         approve / deny a request, reschedule a
--                                missed appointment
--   7. Views                     provider schedule, room schedule,
--                                pending request queue
--
-- Audit: before making changes, identify yourself with
--     SET @current_employee_id = <employee_id>;
-- MySQL triggers cannot see the application user, so they read this
-- session variable. If it is not set, changed_by is stored as NULL.
-- =====================================================================

USE clinic_db;

-- ---------------------------------------------------------------------
-- Drop existing objects so the script can be re-run
-- ---------------------------------------------------------------------
DROP TRIGGER IF EXISTS trg_appointment_before_insert;
DROP TRIGGER IF EXISTS trg_appointment_before_update;
DROP TRIGGER IF EXISTS trg_provider_before_insert;
DROP TRIGGER IF EXISTS trg_staff_before_insert;
DROP TRIGGER IF EXISTS trg_cancel_before_insert;
DROP TRIGGER IF EXISTS trg_cancel_before_update;
DROP TRIGGER IF EXISTS trg_cancel_after_insert;
DROP TRIGGER IF EXISTS trg_cancel_after_update;
DROP TRIGGER IF EXISTS trg_audit_appointment_insert;
DROP TRIGGER IF EXISTS trg_audit_appointment_update;
DROP TRIGGER IF EXISTS trg_audit_appointment_delete;
DROP TRIGGER IF EXISTS trg_audit_apptreq_insert;
DROP TRIGGER IF EXISTS trg_audit_apptreq_update;
DROP TRIGGER IF EXISTS trg_audit_apptreq_delete;
DROP TRIGGER IF EXISTS trg_audit_cancel_insert;
DROP TRIGGER IF EXISTS trg_audit_cancel_update;
DROP TRIGGER IF EXISTS trg_audit_cancel_delete;
DROP PROCEDURE IF EXISTS sp_validate_appointment;
DROP PROCEDURE IF EXISTS sp_approve_request;
DROP PROCEDURE IF EXISTS sp_deny_request;
DROP PROCEDURE IF EXISTS sp_reschedule_missed;
DROP VIEW IF EXISTS vw_provider_schedule;
DROP VIEW IF EXISTS vw_room_schedule;
DROP VIEW IF EXISTS vw_pending_requests;

DELIMITER $$

-- =====================================================================
-- 1. sp_validate_appointment
--    Raises an error (SQLSTATE 45000) if a booking breaks a rule.
--    p_id is the appointment's own id (0 for a new one) so an update
--    is not compared with itself. Canceled appointments never block.
-- =====================================================================
CREATE PROCEDURE sp_validate_appointment(
  IN p_id        INT,
  IN p_patient   INT,
  IN p_provider  INT,
  IN p_room      INT,
  IN p_service   INT,
  IN p_plan_item INT,
  IN p_start     DATETIME,
  IN p_end       DATETIME)
validate: BEGIN
  DECLARE v_room_type   VARCHAR(10);
  DECLARE v_req_type    VARCHAR(10);
  DECLARE v_location    INT;
  DECLARE v_capacity    INT;
  DECLARE v_room_active BOOLEAN;
  DECLARE v_loc_max     INT;
  DECLARE v_conflict    INT;
  DECLARE v_peak        INT;
  DECLARE v_msg         VARCHAR(255);

  SELECT room_type, location_id, capacity, is_active
    INTO v_room_type, v_location, v_capacity, v_room_active
    FROM Room WHERE room_id = p_room;
  SET v_req_type = (SELECT required_room_type FROM ServiceType
                    WHERE service_type_id = p_service);
  SET v_loc_max  = (SELECT max_concurrent_patients FROM Location
                    WHERE location_id = v_location);

  -- Unknown room or service: let the foreign key report the error
  IF v_location IS NULL OR v_req_type IS NULL THEN
    LEAVE validate;
  END IF;

  -- Room must be in service
  IF NOT v_room_active THEN
    SET v_msg = CONCAT('Room ', p_room, ' is inactive');
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = v_msg;
  END IF;

  -- Room type must match the service (infusions in infusion rooms)
  IF v_room_type <> v_req_type THEN
    SET v_msg = CONCAT('Room type mismatch: this service requires room type ', v_req_type,
                       ', but room ', p_room, ' is ', v_room_type);
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = v_msg;
  END IF;

  -- Provider must be qualified for the service
  IF NOT EXISTS (SELECT 1 FROM ProviderQualification
                 WHERE employee_id = p_provider AND service_type_id = p_service) THEN
    SET v_msg = CONCAT('Provider ', p_provider, ' is not qualified for service ', p_service);
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = v_msg;
  END IF;

  -- Provider must work at the room's location
  IF NOT EXISTS (SELECT 1 FROM EmployeeLocation
                 WHERE employee_id = p_provider AND location_id = v_location) THEN
    SET v_msg = CONCAT('Provider ', p_provider, ' does not work at location ', v_location);
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = v_msg;
  END IF;

  -- Plan item must belong to this patient and be for this service
  IF p_plan_item IS NOT NULL AND NOT EXISTS (
       SELECT 1 FROM TreatmentPlanItem i
       JOIN TreatmentPlan t ON t.plan_id = i.plan_id
       WHERE i.plan_item_id = p_plan_item
         AND i.service_type_id = p_service
         AND t.patient_id = p_patient) THEN
    SET v_msg = CONCAT('Plan item ', p_plan_item, ' does not match this patient and service');
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = v_msg;
  END IF;

  -- Provider double-booking.
  -- Exemption: one RN may run overlapping IV infusions in the same
  -- infusion room (room capacity still applies).
  SET v_conflict = (
    SELECT a.appointment_id
    FROM Appointment a
    JOIN ServiceType s ON s.service_type_id = a.service_type_id
    WHERE a.provider_id = p_provider
      AND a.appointment_id <> p_id
      AND a.status <> 'Canceled'
      AND a.start_time < p_end AND a.end_time > p_start
      AND NOT (v_req_type = 'Infusion'
               AND s.required_room_type = 'Infusion'
               AND a.room_id = p_room)
    LIMIT 1);
  IF v_conflict IS NOT NULL THEN
    SET v_msg = CONCAT('Provider ', p_provider,
                       ' is already booked at that time (appointment ', v_conflict, ')');
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = v_msg;
  END IF;

  -- Patient double-booking
  SET v_conflict = (
    SELECT appointment_id FROM Appointment
    WHERE patient_id = p_patient
      AND appointment_id <> p_id
      AND status <> 'Canceled'
      AND start_time < p_end AND end_time > p_start
    LIMIT 1);
  IF v_conflict IS NOT NULL THEN
    SET v_msg = CONCAT('Patient ', p_patient,
                       ' already has an appointment at that time (appointment ', v_conflict, ')');
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = v_msg;
  END IF;

  -- Room capacity: the busiest moment during the new booking.
  -- Occupancy only rises when an appointment starts, so checking the new
  -- start time and every other start inside the window is enough.
  SET v_peak = (
    SELECT MAX(cnt) FROM (
      SELECT (SELECT COUNT(*) FROM Appointment o
              WHERE o.room_id = p_room AND o.status <> 'Canceled'
                AND o.appointment_id <> p_id
                AND o.start_time <= pts.pt AND o.end_time > pts.pt) AS cnt
      FROM (SELECT p_start AS pt
            UNION
            SELECT start_time FROM Appointment
            WHERE room_id = p_room AND status <> 'Canceled' AND appointment_id <> p_id
              AND start_time > p_start AND start_time < p_end) pts
    ) busy);
  IF v_peak + 1 > v_capacity THEN
    SET v_msg = CONCAT('Room ', p_room, ' is full at that time (capacity ', v_capacity, ')');
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = v_msg;
  END IF;

  -- Location capacity: same check across every room at the location
  SET v_peak = (
    SELECT MAX(cnt) FROM (
      SELECT (SELECT COUNT(*) FROM Appointment o
              JOIN Room r ON r.room_id = o.room_id
              WHERE r.location_id = v_location AND o.status <> 'Canceled'
                AND o.appointment_id <> p_id
                AND o.start_time <= pts.pt AND o.end_time > pts.pt) AS cnt
      FROM (SELECT p_start AS pt
            UNION
            SELECT a.start_time FROM Appointment a
            JOIN Room r ON r.room_id = a.room_id
            WHERE r.location_id = v_location AND a.status <> 'Canceled'
              AND a.appointment_id <> p_id
              AND a.start_time > p_start AND a.start_time < p_end) pts
    ) busy);
  IF v_peak + 1 > v_loc_max THEN
    SET v_msg = CONCAT('Location ', v_location, ' is at its limit of ', v_loc_max,
                       ' concurrent patients at that time');
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = v_msg;
  END IF;
END$$

-- =====================================================================
-- 2. Appointment triggers
-- =====================================================================
CREATE TRIGGER trg_appointment_before_insert
BEFORE INSERT ON Appointment
FOR EACH ROW
BEGIN
  IF NEW.status <> 'Canceled' THEN
    CALL sp_validate_appointment(NEW.appointment_id, NEW.patient_id, NEW.provider_id,
         NEW.room_id, NEW.service_type_id, NEW.plan_item_id, NEW.start_time, NEW.end_time);
  END IF;
END$$

-- Re-check only when the booking itself changes (or a canceled visit is
-- reinstated). Status changes such as Scheduled -> Completed skip it.
CREATE TRIGGER trg_appointment_before_update
BEFORE UPDATE ON Appointment
FOR EACH ROW
BEGIN
  IF NEW.status <> 'Canceled' AND (
       OLD.status = 'Canceled'
       OR NEW.patient_id      <> OLD.patient_id
       OR NEW.provider_id     <> OLD.provider_id
       OR NEW.room_id         <> OLD.room_id
       OR NEW.service_type_id <> OLD.service_type_id
       OR NEW.start_time      <> OLD.start_time
       OR NEW.end_time        <> OLD.end_time
       OR NOT (NEW.plan_item_id <=> OLD.plan_item_id)) THEN
    CALL sp_validate_appointment(NEW.appointment_id, NEW.patient_id, NEW.provider_id,
         NEW.room_id, NEW.service_type_id, NEW.plan_item_id, NEW.start_time, NEW.end_time);
  END IF;
END$$

-- =====================================================================
-- 3. Subtype triggers: Provider and Staff are disjoint
-- =====================================================================
CREATE TRIGGER trg_provider_before_insert
BEFORE INSERT ON Provider
FOR EACH ROW
BEGIN
  IF EXISTS (SELECT 1 FROM Staff WHERE employee_id = NEW.employee_id) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'Employee is already Staff; an employee cannot be both Provider and Staff';
  END IF;
END$$

CREATE TRIGGER trg_staff_before_insert
BEFORE INSERT ON Staff
FOR EACH ROW
BEGIN
  IF EXISTS (SELECT 1 FROM Provider WHERE employee_id = NEW.employee_id) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'Employee is already a Provider; an employee cannot be both Provider and Staff';
  END IF;
END$$

-- =====================================================================
-- 4. Cancellation triggers
-- =====================================================================
CREATE TRIGGER trg_cancel_before_insert
BEFORE INSERT ON CancellationRequest
FOR EACH ROW
BEGIN
  -- Clinic-initiated cancellations are approved by the submitter
  IF NEW.source = 'Clinic' THEN
    SET NEW.status      = 'Approved';
    SET NEW.reviewed_by = NEW.submitted_by;
    IF NEW.reviewed_at IS NULL THEN
      SET NEW.reviewed_at = NEW.submitted_at;
    END IF;
  END IF;

  IF NEW.status = 'Pending' THEN
    -- Only a scheduled appointment can be cancelled
    IF NOT EXISTS (SELECT 1 FROM Appointment
                   WHERE appointment_id = NEW.appointment_id AND status = 'Scheduled') THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'Only a Scheduled appointment can receive a cancellation request';
    END IF;
    -- At most one pending request per appointment
    IF EXISTS (SELECT 1 FROM CancellationRequest
               WHERE appointment_id = NEW.appointment_id AND status = 'Pending') THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'This appointment already has a pending cancellation request';
    END IF;
  END IF;
END$$

-- Stamp the review time when staff approve or deny
CREATE TRIGGER trg_cancel_before_update
BEFORE UPDATE ON CancellationRequest
FOR EACH ROW
BEGIN
  IF OLD.status = 'Pending' AND NEW.status <> 'Pending' AND NEW.reviewed_at IS NULL THEN
    SET NEW.reviewed_at = NOW();
  END IF;
END$$

-- An approved cancellation cancels the appointment
CREATE TRIGGER trg_cancel_after_insert
AFTER INSERT ON CancellationRequest
FOR EACH ROW
BEGIN
  IF NEW.status = 'Approved' THEN
    UPDATE Appointment SET status = 'Canceled'
    WHERE appointment_id = NEW.appointment_id AND status = 'Scheduled';
  END IF;
END$$

CREATE TRIGGER trg_cancel_after_update
AFTER UPDATE ON CancellationRequest
FOR EACH ROW
BEGIN
  IF NEW.status = 'Approved' AND OLD.status <> 'Approved' THEN
    UPDATE Appointment SET status = 'Canceled'
    WHERE appointment_id = NEW.appointment_id AND status = 'Scheduled';
  END IF;
END$$

-- =====================================================================
-- 5. Audit triggers (changed_by comes from @current_employee_id)
-- =====================================================================
CREATE TRIGGER trg_audit_appointment_insert
AFTER INSERT ON Appointment
FOR EACH ROW
BEGIN
  INSERT INTO AuditLog (table_name, record_id, action, changed_by, new_values)
  VALUES ('Appointment', NEW.appointment_id, 'INSERT', @current_employee_id,
          JSON_OBJECT('patient_id', NEW.patient_id, 'provider_id', NEW.provider_id,
                      'room_id', NEW.room_id, 'service_type_id', NEW.service_type_id,
                      'start_time', NEW.start_time, 'end_time', NEW.end_time,
                      'status', NEW.status));
END$$

CREATE TRIGGER trg_audit_appointment_update
AFTER UPDATE ON Appointment
FOR EACH ROW
BEGIN
  INSERT INTO AuditLog (table_name, record_id, action, changed_by, old_values, new_values)
  VALUES ('Appointment', NEW.appointment_id, 'UPDATE', @current_employee_id,
          JSON_OBJECT('provider_id', OLD.provider_id, 'room_id', OLD.room_id,
                      'start_time', OLD.start_time, 'end_time', OLD.end_time,
                      'status', OLD.status),
          JSON_OBJECT('provider_id', NEW.provider_id, 'room_id', NEW.room_id,
                      'start_time', NEW.start_time, 'end_time', NEW.end_time,
                      'status', NEW.status));
END$$

CREATE TRIGGER trg_audit_appointment_delete
AFTER DELETE ON Appointment
FOR EACH ROW
BEGIN
  INSERT INTO AuditLog (table_name, record_id, action, changed_by, old_values)
  VALUES ('Appointment', OLD.appointment_id, 'DELETE', @current_employee_id,
          JSON_OBJECT('patient_id', OLD.patient_id, 'provider_id', OLD.provider_id,
                      'room_id', OLD.room_id, 'start_time', OLD.start_time,
                      'status', OLD.status));
END$$

CREATE TRIGGER trg_audit_apptreq_insert
AFTER INSERT ON AppointmentRequest
FOR EACH ROW
BEGIN
  INSERT INTO AuditLog (table_name, record_id, action, changed_by, new_values)
  VALUES ('AppointmentRequest', NEW.request_id, 'INSERT', @current_employee_id,
          JSON_OBJECT('patient_id', NEW.patient_id, 'service_type_id', NEW.service_type_id,
                      'preferred_start', NEW.preferred_start, 'status', NEW.status));
END$$

CREATE TRIGGER trg_audit_apptreq_update
AFTER UPDATE ON AppointmentRequest
FOR EACH ROW
BEGIN
  INSERT INTO AuditLog (table_name, record_id, action, changed_by, old_values, new_values)
  VALUES ('AppointmentRequest', NEW.request_id, 'UPDATE', @current_employee_id,
          JSON_OBJECT('status', OLD.status, 'reviewed_by', OLD.reviewed_by,
                      'appointment_id', OLD.appointment_id),
          JSON_OBJECT('status', NEW.status, 'reviewed_by', NEW.reviewed_by,
                      'appointment_id', NEW.appointment_id));
END$$

CREATE TRIGGER trg_audit_apptreq_delete
AFTER DELETE ON AppointmentRequest
FOR EACH ROW
BEGIN
  INSERT INTO AuditLog (table_name, record_id, action, changed_by, old_values)
  VALUES ('AppointmentRequest', OLD.request_id, 'DELETE', @current_employee_id,
          JSON_OBJECT('patient_id', OLD.patient_id, 'status', OLD.status,
                      'preferred_start', OLD.preferred_start));
END$$

CREATE TRIGGER trg_audit_cancel_insert
AFTER INSERT ON CancellationRequest
FOR EACH ROW
BEGIN
  INSERT INTO AuditLog (table_name, record_id, action, changed_by, new_values)
  VALUES ('CancellationRequest', NEW.cancel_request_id, 'INSERT', @current_employee_id,
          JSON_OBJECT('appointment_id', NEW.appointment_id, 'source', NEW.source,
                      'status', NEW.status, 'reason', NEW.reason));
END$$

CREATE TRIGGER trg_audit_cancel_update
AFTER UPDATE ON CancellationRequest
FOR EACH ROW
BEGIN
  INSERT INTO AuditLog (table_name, record_id, action, changed_by, old_values, new_values)
  VALUES ('CancellationRequest', NEW.cancel_request_id, 'UPDATE', @current_employee_id,
          JSON_OBJECT('status', OLD.status, 'reviewed_by', OLD.reviewed_by),
          JSON_OBJECT('status', NEW.status, 'reviewed_by', NEW.reviewed_by));
END$$

CREATE TRIGGER trg_audit_cancel_delete
AFTER DELETE ON CancellationRequest
FOR EACH ROW
BEGIN
  INSERT INTO AuditLog (table_name, record_id, action, changed_by, old_values)
  VALUES ('CancellationRequest', OLD.cancel_request_id, 'DELETE', @current_employee_id,
          JSON_OBJECT('appointment_id', OLD.appointment_id, 'status', OLD.status));
END$$

-- =====================================================================
-- 6. Stored procedures
-- =====================================================================

-- Approve a pending appointment request: book the appointment (the
-- Appointment triggers enforce every scheduling rule) and link it.
CREATE PROCEDURE sp_approve_request(
  IN p_request_id INT,
  IN p_staff_id   INT,
  IN p_provider   INT,
  IN p_room       INT,
  IN p_start      DATETIME)
BEGIN
  DECLARE v_patient  INT;
  DECLARE v_service  INT;
  DECLARE v_status   VARCHAR(10);
  DECLARE v_duration INT;
  DECLARE v_appt_id  INT;

  SELECT patient_id, service_type_id, status
    INTO v_patient, v_service, v_status
    FROM AppointmentRequest WHERE request_id = p_request_id;

  IF v_status IS NULL THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Appointment request not found';
  ELSEIF v_status <> 'Pending' THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Only a Pending request can be approved';
  END IF;

  SET v_duration = (SELECT default_duration_min FROM ServiceType
                    WHERE service_type_id = v_service);

  INSERT INTO Appointment (patient_id, provider_id, room_id, service_type_id,
                           created_by, start_time, end_time, notes)
  VALUES (v_patient, p_provider, p_room, v_service, p_staff_id,
          p_start, p_start + INTERVAL v_duration MINUTE,
          CONCAT('Booked from appointment request ', p_request_id));
  SET v_appt_id = LAST_INSERT_ID();

  UPDATE AppointmentRequest
  SET status = 'Approved', reviewed_by = p_staff_id, reviewed_at = NOW(),
      appointment_id = v_appt_id
  WHERE request_id = p_request_id;

  SELECT * FROM vw_provider_schedule WHERE appointment_id = v_appt_id;
END$$

-- Deny a pending appointment request with a reason
CREATE PROCEDURE sp_deny_request(
  IN p_request_id INT,
  IN p_staff_id   INT,
  IN p_note       VARCHAR(255))
BEGIN
  IF NOT EXISTS (SELECT 1 FROM AppointmentRequest
                 WHERE request_id = p_request_id AND status = 'Pending') THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Only a Pending request can be denied';
  END IF;

  UPDATE AppointmentRequest
  SET status = 'Denied', reviewed_by = p_staff_id, reviewed_at = NOW(),
      decision_note = p_note
  WHERE request_id = p_request_id;
END$$

-- Reschedule a missed appointment into the first open slot in the next
-- 30 days: same patient, provider and service, a room of the same type
-- at the same location (original room tried first), weekdays 8 AM-5 PM
-- on the 30-minute grid. Each candidate is tried as an INSERT; the
-- Appointment trigger rejects slots that break a rule, and the handler
-- moves on to the next candidate.
CREATE PROCEDURE sp_reschedule_missed(
  IN  p_appointment_id INT,
  IN  p_staff_id       INT,
  OUT p_new_id         INT)
BEGIN
  DECLARE v_patient   INT;
  DECLARE v_provider  INT;
  DECLARE v_room      INT;
  DECLARE v_service   INT;
  DECLARE v_plan_item INT;
  DECLARE v_status    VARCHAR(10);
  DECLARE v_minutes   INT;
  DECLARE v_location  INT;
  DECLARE v_room_type VARCHAR(10);
  DECLARE v_orig_date DATE;

  SELECT a.patient_id, a.provider_id, a.room_id, a.service_type_id, a.plan_item_id,
         a.status, TIMESTAMPDIFF(MINUTE, a.start_time, a.end_time), r.location_id,
         r.room_type, DATE(a.start_time)
    INTO v_patient, v_provider, v_room, v_service, v_plan_item,
         v_status, v_minutes, v_location, v_room_type, v_orig_date
    FROM Appointment a JOIN Room r ON r.room_id = a.room_id
    WHERE a.appointment_id = p_appointment_id;

  IF v_status IS NULL THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Appointment not found';
  ELSEIF v_status <> 'Missed' THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Only a Missed appointment can be rescheduled';
  ELSEIF EXISTS (SELECT 1 FROM Appointment WHERE rescheduled_from_id = p_appointment_id) THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'This appointment has already been rescheduled';
  END IF;

  SET p_new_id = NULL;

  search: BEGIN
    DECLARE v_failed   BOOLEAN DEFAULT FALSE;
    DECLARE v_day      DATE;
    DECLARE v_day_n    INT DEFAULT 0;
    DECLARE v_slot     DATETIME;
    DECLARE v_try_room INT;
    DECLARE v_room_n   INT;
    DECLARE v_rooms    INT;
    -- A rule violation from the trigger marks this candidate as failed
    DECLARE CONTINUE HANDLER FOR SQLSTATE '45000' SET v_failed = TRUE;

    SET v_rooms = (SELECT COUNT(*) FROM Room
                   WHERE location_id = v_location AND room_type = v_room_type
                     AND is_active = TRUE);
    SET v_day = GREATEST(v_orig_date, CURDATE()) + INTERVAL 1 DAY;

    day_loop: WHILE v_day_n < 30 DO
      IF DAYOFWEEK(v_day) BETWEEN 2 AND 6 THEN
        SET v_slot = TIMESTAMP(v_day, '08:00:00');
        slot_loop: WHILE v_slot + INTERVAL v_minutes MINUTE <= TIMESTAMP(v_day, '17:00:00') DO
          SET v_room_n = 0;
          room_loop: WHILE v_room_n < v_rooms DO
            SET v_try_room = (SELECT room_id FROM Room
                              WHERE location_id = v_location AND room_type = v_room_type
                                AND is_active = TRUE
                              ORDER BY (room_id <> v_room), room_id
                              LIMIT 1 OFFSET v_room_n);
            SET v_failed = FALSE;
            INSERT INTO Appointment (patient_id, provider_id, room_id, service_type_id,
                                     plan_item_id, rescheduled_from_id, created_by,
                                     start_time, end_time, notes)
            VALUES (v_patient, v_provider, v_try_room, v_service,
                    v_plan_item, p_appointment_id, p_staff_id,
                    v_slot, v_slot + INTERVAL v_minutes MINUTE,
                    CONCAT('Auto-rescheduled from missed appointment ', p_appointment_id));
            IF NOT v_failed THEN
              SET p_new_id = LAST_INSERT_ID();
              LEAVE day_loop;
            END IF;
            SET v_room_n = v_room_n + 1;
          END WHILE room_loop;
          SET v_slot = v_slot + INTERVAL 30 MINUTE;
        END WHILE slot_loop;
      END IF;
      SET v_day = v_day + INTERVAL 1 DAY;
      SET v_day_n = v_day_n + 1;
    END WHILE day_loop;
  END search;

  IF p_new_id IS NULL THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'No open slot found in the next 30 days';
  END IF;

  SELECT * FROM vw_provider_schedule WHERE appointment_id = p_new_id;
END$$

DELIMITER ;

-- =====================================================================
-- 7. Views
-- =====================================================================

-- Each provider's schedule (canceled visits excluded)
CREATE VIEW vw_provider_schedule AS
SELECT a.appointment_id,
       a.provider_id,
       CONCAT(e.first_name, ' ', e.last_name)   AS provider,
       pr.credential,
       DATE_FORMAT(a.start_time, '%Y-%m-%d')    AS appt_date,
       DATE_FORMAT(a.start_time, '%H:%i')       AS start_time,
       DATE_FORMAT(a.end_time, '%H:%i')         AS end_time,
       CONCAT(p.first_name, ' ', p.last_name)   AS patient,
       st.name                                  AS service,
       r.room_name,
       l.name                                   AS location,
       a.status
FROM Appointment a
JOIN Provider    pr ON pr.employee_id     = a.provider_id
JOIN Employee    e  ON e.employee_id      = pr.employee_id
JOIN Patient     p  ON p.patient_id       = a.patient_id
JOIN ServiceType st ON st.service_type_id = a.service_type_id
JOIN Room        r  ON r.room_id          = a.room_id
JOIN Location    l  ON l.location_id      = r.location_id
WHERE a.status <> 'Canceled';

-- Each room's schedule with its capacity (canceled visits excluded)
CREATE VIEW vw_room_schedule AS
SELECT r.room_id,
       l.name                                   AS location,
       r.room_name,
       r.room_type,
       r.capacity,
       DATE_FORMAT(a.start_time, '%Y-%m-%d')    AS appt_date,
       DATE_FORMAT(a.start_time, '%H:%i')       AS start_time,
       DATE_FORMAT(a.end_time, '%H:%i')         AS end_time,
       a.appointment_id,
       CONCAT(p.first_name, ' ', p.last_name)   AS patient,
       CONCAT(e.first_name, ' ', e.last_name)   AS provider,
       st.name                                  AS service,
       a.status
FROM Appointment a
JOIN Room        r  ON r.room_id          = a.room_id
JOIN Location    l  ON l.location_id      = r.location_id
JOIN Patient     p  ON p.patient_id       = a.patient_id
JOIN Employee    e  ON e.employee_id      = a.provider_id
JOIN ServiceType st ON st.service_type_id = a.service_type_id
WHERE a.status <> 'Canceled';

-- Staff work queue: every pending request of both kinds, oldest first
CREATE VIEW vw_pending_requests AS
SELECT 'Appointment request'                    AS request_type,
       ar.request_id                            AS request_id,
       CONCAT(p.first_name, ' ', p.last_name)   AS patient,
       st.name                                  AS service,
       DATE_FORMAT(ar.preferred_start, '%Y-%m-%d %H:%i') AS requested_time,
       ar.patient_notes                         AS details,
       ar.submitted_at
FROM AppointmentRequest ar
JOIN Patient     p  ON p.patient_id       = ar.patient_id
JOIN ServiceType st ON st.service_type_id = ar.service_type_id
WHERE ar.status = 'Pending'
UNION ALL
SELECT 'Cancellation request',
       cr.cancel_request_id,
       CONCAT(p.first_name, ' ', p.last_name),
       st.name,
       DATE_FORMAT(a.start_time, '%Y-%m-%d %H:%i'),
       cr.reason,
       cr.submitted_at
FROM CancellationRequest cr
JOIN Appointment a  ON a.appointment_id    = cr.appointment_id
JOIN Patient     p  ON p.patient_id        = a.patient_id
JOIN ServiceType st ON st.service_type_id  = a.service_type_id
WHERE cr.status = 'Pending';

-- ---------------------------------------------------------------------
-- Verify: list the triggers, procedures and views just created
-- ---------------------------------------------------------------------
SELECT 'TRIGGER' AS object_type, TRIGGER_NAME AS object_name,
       CONCAT(ACTION_TIMING, ' ', EVENT_MANIPULATION, ' ON ', EVENT_OBJECT_TABLE) AS detail
FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA = 'clinic_db'
UNION ALL
SELECT 'PROCEDURE', ROUTINE_NAME, '' FROM information_schema.ROUTINES
WHERE ROUTINE_SCHEMA = 'clinic_db' AND ROUTINE_TYPE = 'PROCEDURE'
UNION ALL
SELECT 'VIEW', TABLE_NAME, '' FROM information_schema.VIEWS WHERE TABLE_SCHEMA = 'clinic_db'
ORDER BY object_type, object_name;
