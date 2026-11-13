-- =====================================================================
-- ClinicScheduler V2: Triggers, Functions and Views
-- PostgreSQL port of db/mysql/triggers.sql. Apply after 01_schema.sql.
--
-- Contents (same numbering as the MySQL script)
--   1. sp_validate_appointment   shared scheduling-rule checks
--   2. Appointment triggers      run the checks on insert and update
--   3. Subtype triggers          an employee is a Provider or Staff, not both
--   4. Cancellation triggers     one pending request per appointment,
--                                clinic requests auto-approved, approval
--                                cancels the appointment
--   5. Audit triggers            appointment, appointment_request and
--                                cancellation_request changes -> audit_log
--   6. Stored routines           approve / deny a request, reschedule a
--                                missed appointment
--   7. Views                     provider schedule, room schedule,
--                                pending request queue
--
-- What changes in the port:
--   * SIGNAL SQLSTATE '45000'  -> RAISE EXCEPTION (SQLSTATE P0001). Every
--     rule also carries a name in the error's CONSTRAINT field
--     (rule_provider_double_booked, rule_room_full, ...), so a client can
--     tell rules apart without parsing the message text.
--   * The MySQL procedures become functions: PostgreSQL procedures cannot
--     return a result set, so sp_approve_request and sp_reschedule_missed
--     return the new appointment_id instead of selecting from a view.
--   * sp_validate_appointment takes transaction-level advisory locks on the
--     provider and the location before it checks them. Without the locks,
--     two bookings made at the same instant could each pass the check and
--     both commit; with them, the second waits and then sees the first.
--   * Audit: MySQL read @current_employee_id. Here each session runs
--       SELECT set_config('app.current_employee_id', '<employee_id>', false);
--     and the triggers read it through current_employee_id(). If it is not
--     set, changed_by is stored as NULL.
-- =====================================================================

-- The employee making changes in this session, or NULL
CREATE FUNCTION current_employee_id() RETURNS integer
LANGUAGE sql STABLE
AS $$ SELECT NULLIF(current_setting('app.current_employee_id', true), '')::integer $$;

-- =====================================================================
-- 1. sp_validate_appointment
--    Raises an error if a booking breaks a rule. p_id is the appointment's
--    own id so an update is not compared with itself. Canceled
--    appointments never block.
-- =====================================================================
CREATE FUNCTION sp_validate_appointment(
  p_id        integer,
  p_patient   integer,
  p_provider  integer,
  p_room      integer,
  p_service   integer,
  p_plan_item integer,
  p_start     timestamp,
  p_end       timestamp)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
  v_room_type   room_type;
  v_req_type    room_type;
  v_location    integer;
  v_capacity    integer;
  v_room_active boolean;
  v_loc_max     integer;
  v_conflict    integer;
  v_peak        integer;
BEGIN
  SELECT r.room_type, r.location_id, r.capacity, r.is_active
    INTO v_room_type, v_location, v_capacity, v_room_active
    FROM room r WHERE r.room_id = p_room;
  SELECT s.required_room_type INTO v_req_type
    FROM service_type s WHERE s.service_type_id = p_service;
  SELECT l.max_concurrent_patients INTO v_loc_max
    FROM location l WHERE l.location_id = v_location;

  -- Unknown room or service: let the foreign key report the error
  IF v_location IS NULL OR v_req_type IS NULL THEN
    RETURN;
  END IF;

  -- Serialize bookings that could conflict: same provider, or same location
  -- (which covers the room). Always taken in this order, held to commit.
  PERFORM pg_advisory_xact_lock(1, p_provider);
  PERFORM pg_advisory_xact_lock(2, v_location);

  -- Room must be in service
  IF NOT v_room_active THEN
    RAISE EXCEPTION 'Room % is inactive', p_room
      USING CONSTRAINT = 'rule_room_inactive', TABLE = 'appointment';
  END IF;

  -- Room type must match the service (infusions in infusion rooms)
  IF v_room_type <> v_req_type THEN
    RAISE EXCEPTION 'Room type mismatch: this service requires room type %, but room % is %',
                    v_req_type, p_room, v_room_type
      USING CONSTRAINT = 'rule_room_type_mismatch', TABLE = 'appointment';
  END IF;

  -- Provider must be qualified for the service
  IF NOT EXISTS (SELECT 1 FROM provider_qualification q
                 WHERE q.employee_id = p_provider AND q.service_type_id = p_service) THEN
    RAISE EXCEPTION 'Provider % is not qualified for service %', p_provider, p_service
      USING CONSTRAINT = 'rule_provider_not_qualified', TABLE = 'appointment';
  END IF;

  -- Provider must work at the room's location
  IF NOT EXISTS (SELECT 1 FROM employee_location el
                 WHERE el.employee_id = p_provider AND el.location_id = v_location) THEN
    RAISE EXCEPTION 'Provider % does not work at location %', p_provider, v_location
      USING CONSTRAINT = 'rule_provider_wrong_location', TABLE = 'appointment';
  END IF;

  -- Plan item must belong to this patient and be for this service
  IF p_plan_item IS NOT NULL AND NOT EXISTS (
       SELECT 1 FROM treatment_plan_item i
       JOIN treatment_plan t ON t.plan_id = i.plan_id
       WHERE i.plan_item_id = p_plan_item
         AND i.service_type_id = p_service
         AND t.patient_id = p_patient) THEN
    RAISE EXCEPTION 'Plan item % does not match this patient and service', p_plan_item
      USING CONSTRAINT = 'rule_plan_item_mismatch', TABLE = 'appointment';
  END IF;

  -- Provider double-booking.
  -- Exemption: one RN may run overlapping IV infusions in the same
  -- infusion room (room capacity still applies).
  SELECT a.appointment_id INTO v_conflict
  FROM appointment a
  JOIN service_type s ON s.service_type_id = a.service_type_id
  WHERE a.provider_id = p_provider
    AND a.appointment_id <> p_id
    AND a.status <> 'Canceled'
    AND a.start_time < p_end AND a.end_time > p_start
    AND NOT (v_req_type = 'Infusion'
             AND s.required_room_type = 'Infusion'
             AND a.room_id = p_room)
  ORDER BY a.appointment_id
  LIMIT 1;
  IF v_conflict IS NOT NULL THEN
    RAISE EXCEPTION 'Provider % is already booked at that time (appointment %)', p_provider, v_conflict
      USING CONSTRAINT = 'rule_provider_double_booked', TABLE = 'appointment';
  END IF;

  -- Patient double-booking (also guaranteed by ex_appt_patient_no_overlap)
  SELECT a.appointment_id INTO v_conflict
  FROM appointment a
  WHERE a.patient_id = p_patient
    AND a.appointment_id <> p_id
    AND a.status <> 'Canceled'
    AND a.start_time < p_end AND a.end_time > p_start
  ORDER BY a.appointment_id
  LIMIT 1;
  IF v_conflict IS NOT NULL THEN
    RAISE EXCEPTION 'Patient % already has an appointment at that time (appointment %)', p_patient, v_conflict
      USING CONSTRAINT = 'rule_patient_double_booked', TABLE = 'appointment';
  END IF;

  -- Room capacity: the busiest moment during the new booking.
  -- Occupancy only rises when an appointment starts, so checking the new
  -- start time and every other start inside the window is enough.
  SELECT MAX(busy.cnt) INTO v_peak FROM (
    SELECT (SELECT COUNT(*) FROM appointment o
            WHERE o.room_id = p_room AND o.status <> 'Canceled'
              AND o.appointment_id <> p_id
              AND o.start_time <= pts.pt AND o.end_time > pts.pt) AS cnt
    FROM (SELECT p_start AS pt
          UNION
          SELECT a.start_time FROM appointment a
          WHERE a.room_id = p_room AND a.status <> 'Canceled' AND a.appointment_id <> p_id
            AND a.start_time > p_start AND a.start_time < p_end) pts
  ) busy;
  IF v_peak + 1 > v_capacity THEN
    RAISE EXCEPTION 'Room % is full at that time (capacity %)', p_room, v_capacity
      USING CONSTRAINT = 'rule_room_full', TABLE = 'appointment';
  END IF;

  -- Location capacity: same check across every room at the location
  SELECT MAX(busy.cnt) INTO v_peak FROM (
    SELECT (SELECT COUNT(*) FROM appointment o
            JOIN room r ON r.room_id = o.room_id
            WHERE r.location_id = v_location AND o.status <> 'Canceled'
              AND o.appointment_id <> p_id
              AND o.start_time <= pts.pt AND o.end_time > pts.pt) AS cnt
    FROM (SELECT p_start AS pt
          UNION
          SELECT a.start_time FROM appointment a
          JOIN room r ON r.room_id = a.room_id
          WHERE r.location_id = v_location AND a.status <> 'Canceled'
            AND a.appointment_id <> p_id
            AND a.start_time > p_start AND a.start_time < p_end) pts
  ) busy;
  IF v_peak + 1 > v_loc_max THEN
    RAISE EXCEPTION 'Location % is at its limit of % concurrent patients at that time', v_location, v_loc_max
      USING CONSTRAINT = 'rule_location_full', TABLE = 'appointment';
  END IF;
END;
$$;

-- =====================================================================
-- 2. Appointment triggers
-- =====================================================================
CREATE FUNCTION trg_fn_appointment_validate() RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
  v_check boolean;
BEGIN
  IF NEW.status = 'Canceled' THEN
    RETURN NEW;
  END IF;

  -- On update, re-check only when the booking itself changes (or a canceled
  -- visit is reinstated). Status changes such as Scheduled -> Completed skip it.
  IF TG_OP = 'INSERT' THEN
    v_check := TRUE;
  ELSE
    v_check := OLD.status = 'Canceled'
            OR NEW.patient_id      <> OLD.patient_id
            OR NEW.provider_id     <> OLD.provider_id
            OR NEW.room_id         <> OLD.room_id
            OR NEW.service_type_id <> OLD.service_type_id
            OR NEW.start_time      <> OLD.start_time
            OR NEW.end_time        <> OLD.end_time
            OR NEW.plan_item_id IS DISTINCT FROM OLD.plan_item_id;
  END IF;

  IF v_check THEN
    PERFORM sp_validate_appointment(NEW.appointment_id, NEW.patient_id, NEW.provider_id,
            NEW.room_id, NEW.service_type_id, NEW.plan_item_id, NEW.start_time, NEW.end_time);
  END IF;
  RETURN NEW;
END;
$$;

CREATE TRIGGER trg_appointment_before_insert
BEFORE INSERT ON appointment
FOR EACH ROW EXECUTE FUNCTION trg_fn_appointment_validate();

CREATE TRIGGER trg_appointment_before_update
BEFORE UPDATE ON appointment
FOR EACH ROW EXECUTE FUNCTION trg_fn_appointment_validate();

-- =====================================================================
-- 3. Subtype triggers: Provider and Staff are disjoint
-- =====================================================================
CREATE FUNCTION trg_fn_provider_disjoint() RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  IF EXISTS (SELECT 1 FROM staff s WHERE s.employee_id = NEW.employee_id) THEN
    RAISE EXCEPTION 'Employee is already Staff; an employee cannot be both Provider and Staff'
      USING CONSTRAINT = 'rule_subtype_disjoint', TABLE = 'provider';
  END IF;
  RETURN NEW;
END;
$$;

CREATE FUNCTION trg_fn_staff_disjoint() RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  IF EXISTS (SELECT 1 FROM provider p WHERE p.employee_id = NEW.employee_id) THEN
    RAISE EXCEPTION 'Employee is already a Provider; an employee cannot be both Provider and Staff'
      USING CONSTRAINT = 'rule_subtype_disjoint', TABLE = 'staff';
  END IF;
  RETURN NEW;
END;
$$;

CREATE TRIGGER trg_provider_before_insert
BEFORE INSERT ON provider
FOR EACH ROW EXECUTE FUNCTION trg_fn_provider_disjoint();

CREATE TRIGGER trg_staff_before_insert
BEFORE INSERT ON staff
FOR EACH ROW EXECUTE FUNCTION trg_fn_staff_disjoint();

-- =====================================================================
-- 4. Cancellation triggers
-- =====================================================================
CREATE FUNCTION trg_fn_cancel_before_insert() RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  -- Clinic-initiated cancellations are approved by the submitter
  IF NEW.source = 'Clinic' THEN
    NEW.status      := 'Approved';
    NEW.reviewed_by := NEW.submitted_by;
    IF NEW.reviewed_at IS NULL THEN
      NEW.reviewed_at := NEW.submitted_at;
    END IF;
  END IF;

  IF NEW.status = 'Pending' THEN
    -- Only a scheduled appointment can be cancelled. The row lock keeps its
    -- status from changing until this request commits.
    PERFORM 1 FROM appointment a
      WHERE a.appointment_id = NEW.appointment_id AND a.status = 'Scheduled'
      FOR UPDATE;
    IF NOT FOUND THEN
      RAISE EXCEPTION 'Only a Scheduled appointment can receive a cancellation request'
        USING CONSTRAINT = 'rule_cancel_not_scheduled', TABLE = 'cancellation_request';
    END IF;
    -- At most one pending request per appointment (also guaranteed by uq_cancel_one_pending)
    IF EXISTS (SELECT 1 FROM cancellation_request c
               WHERE c.appointment_id = NEW.appointment_id AND c.status = 'Pending') THEN
      RAISE EXCEPTION 'This appointment already has a pending cancellation request'
        USING CONSTRAINT = 'rule_cancel_one_pending', TABLE = 'cancellation_request';
    END IF;
  END IF;
  RETURN NEW;
END;
$$;

-- Stamp the review time when staff approve or deny
CREATE FUNCTION trg_fn_cancel_before_update() RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  IF OLD.status = 'Pending' AND NEW.status <> 'Pending' AND NEW.reviewed_at IS NULL THEN
    NEW.reviewed_at := clinic_now();
  END IF;
  RETURN NEW;
END;
$$;

-- An approved cancellation cancels the appointment
CREATE FUNCTION trg_fn_cancel_apply() RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  IF NEW.status = 'Approved' AND (TG_OP = 'INSERT' OR OLD.status <> 'Approved') THEN
    UPDATE appointment SET status = 'Canceled'
    WHERE appointment_id = NEW.appointment_id AND status = 'Scheduled';
  END IF;
  RETURN NULL;
END;
$$;

CREATE TRIGGER trg_cancel_before_insert
BEFORE INSERT ON cancellation_request
FOR EACH ROW EXECUTE FUNCTION trg_fn_cancel_before_insert();

CREATE TRIGGER trg_cancel_before_update
BEFORE UPDATE ON cancellation_request
FOR EACH ROW EXECUTE FUNCTION trg_fn_cancel_before_update();

CREATE TRIGGER trg_cancel_after_insert
AFTER INSERT ON cancellation_request
FOR EACH ROW EXECUTE FUNCTION trg_fn_cancel_apply();

CREATE TRIGGER trg_cancel_after_update
AFTER UPDATE ON cancellation_request
FOR EACH ROW EXECUTE FUNCTION trg_fn_cancel_apply();

-- =====================================================================
-- 5. Audit triggers (changed_by comes from current_employee_id())
--    One function per table handles all three actions; the nine triggers
--    keep the names used in the MySQL script.
-- =====================================================================
CREATE FUNCTION trg_fn_audit_appointment() RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  IF TG_OP = 'INSERT' THEN
    INSERT INTO audit_log (table_name, record_id, action, changed_by, new_values)
    VALUES ('Appointment', NEW.appointment_id, 'INSERT', current_employee_id(),
            jsonb_build_object('patient_id', NEW.patient_id, 'provider_id', NEW.provider_id,
                               'room_id', NEW.room_id, 'service_type_id', NEW.service_type_id,
                               'start_time', NEW.start_time, 'end_time', NEW.end_time,
                               'status', NEW.status));
  ELSIF TG_OP = 'UPDATE' THEN
    INSERT INTO audit_log (table_name, record_id, action, changed_by, old_values, new_values)
    VALUES ('Appointment', NEW.appointment_id, 'UPDATE', current_employee_id(),
            jsonb_build_object('provider_id', OLD.provider_id, 'room_id', OLD.room_id,
                               'start_time', OLD.start_time, 'end_time', OLD.end_time,
                               'status', OLD.status),
            jsonb_build_object('provider_id', NEW.provider_id, 'room_id', NEW.room_id,
                               'start_time', NEW.start_time, 'end_time', NEW.end_time,
                               'status', NEW.status));
  ELSE
    INSERT INTO audit_log (table_name, record_id, action, changed_by, old_values)
    VALUES ('Appointment', OLD.appointment_id, 'DELETE', current_employee_id(),
            jsonb_build_object('patient_id', OLD.patient_id, 'provider_id', OLD.provider_id,
                               'room_id', OLD.room_id, 'start_time', OLD.start_time,
                               'status', OLD.status));
  END IF;
  RETURN NULL;
END;
$$;

CREATE FUNCTION trg_fn_audit_apptreq() RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  IF TG_OP = 'INSERT' THEN
    INSERT INTO audit_log (table_name, record_id, action, changed_by, new_values)
    VALUES ('AppointmentRequest', NEW.request_id, 'INSERT', current_employee_id(),
            jsonb_build_object('patient_id', NEW.patient_id, 'service_type_id', NEW.service_type_id,
                               'preferred_start', NEW.preferred_start, 'status', NEW.status));
  ELSIF TG_OP = 'UPDATE' THEN
    INSERT INTO audit_log (table_name, record_id, action, changed_by, old_values, new_values)
    VALUES ('AppointmentRequest', NEW.request_id, 'UPDATE', current_employee_id(),
            jsonb_build_object('status', OLD.status, 'reviewed_by', OLD.reviewed_by,
                               'appointment_id', OLD.appointment_id),
            jsonb_build_object('status', NEW.status, 'reviewed_by', NEW.reviewed_by,
                               'appointment_id', NEW.appointment_id));
  ELSE
    INSERT INTO audit_log (table_name, record_id, action, changed_by, old_values)
    VALUES ('AppointmentRequest', OLD.request_id, 'DELETE', current_employee_id(),
            jsonb_build_object('patient_id', OLD.patient_id, 'status', OLD.status,
                               'preferred_start', OLD.preferred_start));
  END IF;
  RETURN NULL;
END;
$$;

CREATE FUNCTION trg_fn_audit_cancel() RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  IF TG_OP = 'INSERT' THEN
    INSERT INTO audit_log (table_name, record_id, action, changed_by, new_values)
    VALUES ('CancellationRequest', NEW.cancel_request_id, 'INSERT', current_employee_id(),
            jsonb_build_object('appointment_id', NEW.appointment_id, 'source', NEW.source,
                               'status', NEW.status, 'reason', NEW.reason));
  ELSIF TG_OP = 'UPDATE' THEN
    INSERT INTO audit_log (table_name, record_id, action, changed_by, old_values, new_values)
    VALUES ('CancellationRequest', NEW.cancel_request_id, 'UPDATE', current_employee_id(),
            jsonb_build_object('status', OLD.status, 'reviewed_by', OLD.reviewed_by),
            jsonb_build_object('status', NEW.status, 'reviewed_by', NEW.reviewed_by));
  ELSE
    INSERT INTO audit_log (table_name, record_id, action, changed_by, old_values)
    VALUES ('CancellationRequest', OLD.cancel_request_id, 'DELETE', current_employee_id(),
            jsonb_build_object('appointment_id', OLD.appointment_id, 'status', OLD.status));
  END IF;
  RETURN NULL;
END;
$$;

CREATE TRIGGER trg_audit_appointment_insert AFTER INSERT ON appointment
FOR EACH ROW EXECUTE FUNCTION trg_fn_audit_appointment();
CREATE TRIGGER trg_audit_appointment_update AFTER UPDATE ON appointment
FOR EACH ROW EXECUTE FUNCTION trg_fn_audit_appointment();
CREATE TRIGGER trg_audit_appointment_delete AFTER DELETE ON appointment
FOR EACH ROW EXECUTE FUNCTION trg_fn_audit_appointment();

CREATE TRIGGER trg_audit_apptreq_insert AFTER INSERT ON appointment_request
FOR EACH ROW EXECUTE FUNCTION trg_fn_audit_apptreq();
CREATE TRIGGER trg_audit_apptreq_update AFTER UPDATE ON appointment_request
FOR EACH ROW EXECUTE FUNCTION trg_fn_audit_apptreq();
CREATE TRIGGER trg_audit_apptreq_delete AFTER DELETE ON appointment_request
FOR EACH ROW EXECUTE FUNCTION trg_fn_audit_apptreq();

CREATE TRIGGER trg_audit_cancel_insert AFTER INSERT ON cancellation_request
FOR EACH ROW EXECUTE FUNCTION trg_fn_audit_cancel();
CREATE TRIGGER trg_audit_cancel_update AFTER UPDATE ON cancellation_request
FOR EACH ROW EXECUTE FUNCTION trg_fn_audit_cancel();
CREATE TRIGGER trg_audit_cancel_delete AFTER DELETE ON cancellation_request
FOR EACH ROW EXECUTE FUNCTION trg_fn_audit_cancel();

-- =====================================================================
-- 6. Stored routines
-- =====================================================================

-- Approve a pending appointment request: book the appointment (the
-- appointment triggers enforce every scheduling rule) and link it.
-- Returns the new appointment_id.
CREATE FUNCTION sp_approve_request(
  p_request_id integer,
  p_staff_id   integer,
  p_provider   integer,
  p_room       integer,
  p_start      timestamp)
RETURNS integer
LANGUAGE plpgsql
AS $$
DECLARE
  v_patient  integer;
  v_service  integer;
  v_status   request_status;
  v_duration integer;
  v_appt_id  integer;
BEGIN
  SELECT r.patient_id, r.service_type_id, r.status
    INTO v_patient, v_service, v_status
    FROM appointment_request r WHERE r.request_id = p_request_id
    FOR UPDATE;

  IF NOT FOUND THEN
    RAISE EXCEPTION 'Appointment request not found'
      USING CONSTRAINT = 'rule_request_not_found', TABLE = 'appointment_request';
  ELSIF v_status <> 'Pending' THEN
    RAISE EXCEPTION 'Only a Pending request can be approved'
      USING CONSTRAINT = 'rule_request_not_pending', TABLE = 'appointment_request';
  END IF;

  SELECT s.default_duration_min INTO v_duration
    FROM service_type s WHERE s.service_type_id = v_service;

  INSERT INTO appointment (patient_id, provider_id, room_id, service_type_id,
                           created_by, start_time, end_time, notes)
  VALUES (v_patient, p_provider, p_room, v_service, p_staff_id,
          p_start, p_start + make_interval(mins => v_duration),
          'Booked from appointment request ' || p_request_id)
  RETURNING appointment_id INTO v_appt_id;

  UPDATE appointment_request
  SET status = 'Approved', reviewed_by = p_staff_id, reviewed_at = clinic_now(),
      appointment_id = v_appt_id
  WHERE request_id = p_request_id;

  RETURN v_appt_id;
END;
$$;

-- Deny a pending appointment request with a reason
CREATE FUNCTION sp_deny_request(
  p_request_id integer,
  p_staff_id   integer,
  p_note       varchar(255))
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
  PERFORM 1 FROM appointment_request r
    WHERE r.request_id = p_request_id AND r.status = 'Pending'
    FOR UPDATE;
  IF NOT FOUND THEN
    RAISE EXCEPTION 'Only a Pending request can be denied'
      USING CONSTRAINT = 'rule_request_not_pending', TABLE = 'appointment_request';
  END IF;

  UPDATE appointment_request
  SET status = 'Denied', reviewed_by = p_staff_id, reviewed_at = clinic_now(),
      decision_note = p_note
  WHERE request_id = p_request_id;
END;
$$;

-- Reschedule a missed appointment into the first open slot in the next
-- 30 days: same patient, provider and service, a room of the same type
-- at the same location (original room tried first), weekdays 8 AM-5 PM
-- on the 30-minute grid. Each candidate is tried as an INSERT inside its
-- own sub-block; the appointment trigger rejects slots that break a rule,
-- and the exception handler moves on to the next candidate, so every
-- scheduling rule is reused rather than duplicated here.
-- Returns the new appointment_id.
CREATE FUNCTION sp_reschedule_missed(
  p_appointment_id integer,
  p_staff_id       integer)
RETURNS integer
LANGUAGE plpgsql
AS $$
DECLARE
  v_patient   integer;
  v_provider  integer;
  v_room      integer;
  v_service   integer;
  v_plan_item integer;
  v_status    appointment_status;
  v_length    interval;
  v_location  integer;
  v_room_type room_type;
  v_orig_date date;
  v_day       date;
  v_slot      timestamp;
  v_try_room  integer;
  v_new_id    integer;
BEGIN
  SELECT a.patient_id, a.provider_id, a.room_id, a.service_type_id, a.plan_item_id,
         a.status, a.end_time - a.start_time, r.location_id, r.room_type, a.start_time::date
    INTO v_patient, v_provider, v_room, v_service, v_plan_item,
         v_status, v_length, v_location, v_room_type, v_orig_date
    FROM appointment a JOIN room r ON r.room_id = a.room_id
    WHERE a.appointment_id = p_appointment_id;

  IF NOT FOUND THEN
    RAISE EXCEPTION 'Appointment not found'
      USING CONSTRAINT = 'rule_appointment_not_found', TABLE = 'appointment';
  ELSIF v_status <> 'Missed' THEN
    RAISE EXCEPTION 'Only a Missed appointment can be rescheduled'
      USING CONSTRAINT = 'rule_reschedule_not_missed', TABLE = 'appointment';
  ELSIF EXISTS (SELECT 1 FROM appointment x WHERE x.rescheduled_from_id = p_appointment_id) THEN
    RAISE EXCEPTION 'This appointment has already been rescheduled'
      USING CONSTRAINT = 'rule_already_rescheduled', TABLE = 'appointment';
  END IF;

  v_day := GREATEST(v_orig_date, clinic_now()::date) + 1;

  FOR v_day_n IN 1..30 LOOP
    IF EXTRACT(ISODOW FROM v_day) BETWEEN 1 AND 5 THEN
      v_slot := v_day + TIME '08:00';
      WHILE v_slot + v_length <= v_day + TIME '17:00' LOOP
        FOR v_try_room IN
          SELECT r.room_id FROM room r
          WHERE r.location_id = v_location AND r.room_type = v_room_type AND r.is_active
          ORDER BY (r.room_id <> v_room), r.room_id
        LOOP
          BEGIN
            INSERT INTO appointment (patient_id, provider_id, room_id, service_type_id,
                                     plan_item_id, rescheduled_from_id, created_by,
                                     start_time, end_time, notes)
            VALUES (v_patient, v_provider, v_try_room, v_service,
                    v_plan_item, p_appointment_id, p_staff_id,
                    v_slot, v_slot + v_length,
                    'Auto-rescheduled from missed appointment ' || p_appointment_id)
            RETURNING appointment_id INTO v_new_id;
            RETURN v_new_id;
          EXCEPTION
            -- A rule violation from the trigger (or the patient exclusion
            -- constraint) rejects this candidate; try the next one
            WHEN raise_exception OR exclusion_violation THEN
              NULL;
          END;
        END LOOP;
        v_slot := v_slot + INTERVAL '30 minutes';
      END LOOP;
    END IF;
    v_day := v_day + 1;
  END LOOP;

  RAISE EXCEPTION 'No open slot found in the next 30 days'
    USING CONSTRAINT = 'rule_no_open_slot', TABLE = 'appointment';
END;
$$;

-- =====================================================================
-- 7. Views
-- =====================================================================

-- Each provider's schedule (canceled visits excluded)
CREATE VIEW vw_provider_schedule AS
SELECT a.appointment_id,
       a.provider_id,
       e.first_name || ' ' || e.last_name      AS provider,
       pr.credential,
       to_char(a.start_time, 'YYYY-MM-DD')     AS appt_date,
       to_char(a.start_time, 'HH24:MI')        AS start_time,
       to_char(a.end_time, 'HH24:MI')          AS end_time,
       p.first_name || ' ' || p.last_name      AS patient,
       st.name                                 AS service,
       r.room_name,
       l.name                                  AS location,
       a.status
FROM appointment a
JOIN provider     pr ON pr.employee_id     = a.provider_id
JOIN employee     e  ON e.employee_id      = pr.employee_id
JOIN patient      p  ON p.patient_id       = a.patient_id
JOIN service_type st ON st.service_type_id = a.service_type_id
JOIN room         r  ON r.room_id          = a.room_id
JOIN location     l  ON l.location_id      = r.location_id
WHERE a.status <> 'Canceled';

-- Each room's schedule with its capacity (canceled visits excluded)
CREATE VIEW vw_room_schedule AS
SELECT r.room_id,
       l.name                                  AS location,
       r.room_name,
       r.room_type,
       r.capacity,
       to_char(a.start_time, 'YYYY-MM-DD')     AS appt_date,
       to_char(a.start_time, 'HH24:MI')        AS start_time,
       to_char(a.end_time, 'HH24:MI')          AS end_time,
       a.appointment_id,
       p.first_name || ' ' || p.last_name      AS patient,
       e.first_name || ' ' || e.last_name      AS provider,
       st.name                                 AS service,
       a.status
FROM appointment a
JOIN room         r  ON r.room_id          = a.room_id
JOIN location     l  ON l.location_id      = r.location_id
JOIN patient      p  ON p.patient_id       = a.patient_id
JOIN employee     e  ON e.employee_id      = a.provider_id
JOIN service_type st ON st.service_type_id = a.service_type_id
WHERE a.status <> 'Canceled';

-- Staff work queue: every pending request of both kinds
CREATE VIEW vw_pending_requests AS
SELECT 'Appointment request'::text                      AS request_type,
       ar.request_id                                    AS request_id,
       p.first_name || ' ' || p.last_name               AS patient,
       st.name                                          AS service,
       to_char(ar.preferred_start, 'YYYY-MM-DD HH24:MI') AS requested_time,
       ar.patient_notes                                 AS details,
       ar.submitted_at
FROM appointment_request ar
JOIN patient      p  ON p.patient_id       = ar.patient_id
JOIN service_type st ON st.service_type_id = ar.service_type_id
WHERE ar.status = 'Pending'
UNION ALL
SELECT 'Cancellation request',
       cr.cancel_request_id,
       p.first_name || ' ' || p.last_name,
       st.name,
       to_char(a.start_time, 'YYYY-MM-DD HH24:MI'),
       cr.reason::text,
       cr.submitted_at
FROM cancellation_request cr
JOIN appointment  a  ON a.appointment_id    = cr.appointment_id
JOIN patient      p  ON p.patient_id        = a.patient_id
JOIN service_type st ON st.service_type_id  = a.service_type_id
WHERE cr.status = 'Pending';
