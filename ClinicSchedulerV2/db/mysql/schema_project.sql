-- =====================================================================
-- ClinicScheduler Database: Schema Creation Script
-- Rheumatology Clinic Database for Treatment and Scheduling
-- CSCI 380 Project Part 2 | John Tarver
-- Target: MySQL 8.0.16+ (CHECK constraints are enforced from 8.0.16)
--
-- Creates 17 tables in dependency order (parents before children).
-- Safe to re-run: existing tables are dropped first.
--
-- Rules that span rows or tables (double-booking, room and location
-- capacity, room-type match, provider qualification and work location,
-- Provider/Staff disjointness, one pending cancellation per appointment)
-- cannot be expressed as CHECK constraints in MySQL. They are enforced
-- by triggers in a separate script (triggers.sql).
-- =====================================================================

CREATE DATABASE IF NOT EXISTS clinic_db
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_0900_ai_ci;

USE clinic_db;

SET FOREIGN_KEY_CHECKS = 0;
DROP TABLE IF EXISTS AuditLog;
DROP TABLE IF EXISTS CancellationRequest;
DROP TABLE IF EXISTS AppointmentRequest;
DROP TABLE IF EXISTS MedicationAdministration;
DROP TABLE IF EXISTS Appointment;
DROP TABLE IF EXISTS TreatmentPlanItem;
DROP TABLE IF EXISTS TreatmentPlan;
DROP TABLE IF EXISTS Medication;
DROP TABLE IF EXISTS ProviderQualification;
DROP TABLE IF EXISTS ServiceType;
DROP TABLE IF EXISTS Patient;
DROP TABLE IF EXISTS EmployeeLocation;
DROP TABLE IF EXISTS Staff;
DROP TABLE IF EXISTS Provider;
DROP TABLE IF EXISTS Employee;
DROP TABLE IF EXISTS Room;
DROP TABLE IF EXISTS Location;
SET FOREIGN_KEY_CHECKS = 1;

-- ---------------------------------------------------------------------
-- 1. Location: the three Houston clinic sites
-- ---------------------------------------------------------------------
CREATE TABLE Location (
  location_id             INT              NOT NULL AUTO_INCREMENT,
  name                    VARCHAR(100)     NOT NULL,
  street                  VARCHAR(150)     NOT NULL,
  city                    VARCHAR(60)      NOT NULL DEFAULT 'Houston',
  state                   CHAR(2)          NOT NULL DEFAULT 'TX',
  zip                     CHAR(5)          NOT NULL,
  phone                   VARCHAR(20)      NOT NULL,
  max_concurrent_patients TINYINT UNSIGNED NOT NULL DEFAULT 12,
  CONSTRAINT pk_location PRIMARY KEY (location_id),
  CONSTRAINT uq_location_name UNIQUE (name),
  CONSTRAINT ck_location_zip CHECK (zip REGEXP '^[0-9]{5}$'),
  CONSTRAINT ck_location_capacity CHECK (max_concurrent_patients > 0)
) ENGINE = InnoDB;

-- ---------------------------------------------------------------------
-- 2. Room: belongs to one location; one infusion room per location
-- ---------------------------------------------------------------------
CREATE TABLE Room (
  room_id     INT                         NOT NULL AUTO_INCREMENT,
  location_id INT                         NOT NULL,
  room_name   VARCHAR(50)                 NOT NULL,
  room_type   ENUM('Standard','Infusion') NOT NULL DEFAULT 'Standard',
  capacity    TINYINT UNSIGNED            NOT NULL DEFAULT 1,
  is_active   BOOLEAN                     NOT NULL DEFAULT TRUE,
  CONSTRAINT pk_room PRIMARY KEY (room_id),
  CONSTRAINT uq_room_name_per_location UNIQUE (location_id, room_name),
  CONSTRAINT fk_room_location FOREIGN KEY (location_id)
    REFERENCES Location (location_id),
  CONSTRAINT ck_room_capacity CHECK (capacity >= 1),
  -- Standard rooms hold one patient; infusion rooms hold up to three
  CONSTRAINT ck_room_type_capacity CHECK (
    (room_type = 'Standard' AND capacity = 1) OR
    (room_type = 'Infusion' AND capacity BETWEEN 1 AND 3))
) ENGINE = InnoDB;

-- ---------------------------------------------------------------------
-- 3. Employee: supertype holding attributes shared by providers and staff
-- ---------------------------------------------------------------------
CREATE TABLE Employee (
  employee_id INT          NOT NULL AUTO_INCREMENT,
  first_name  VARCHAR(50)  NOT NULL,
  last_name   VARCHAR(50)  NOT NULL,
  email       VARCHAR(100) NOT NULL,
  phone       VARCHAR(20)  NULL,
  hire_date   DATE         NOT NULL,
  is_active   BOOLEAN      NOT NULL DEFAULT TRUE,
  CONSTRAINT pk_employee PRIMARY KEY (employee_id),
  CONSTRAINT uq_employee_email UNIQUE (email),
  CONSTRAINT ck_employee_email CHECK (email LIKE '%_@_%._%')
) ENGINE = InnoDB;

-- ---------------------------------------------------------------------
-- 4. Provider: subtype of Employee (MD, NP, PA, RN)
-- ---------------------------------------------------------------------
CREATE TABLE Provider (
  employee_id INT                       NOT NULL,
  npi         CHAR(10)                  NOT NULL,
  credential  ENUM('MD','NP','PA','RN') NOT NULL,
  CONSTRAINT pk_provider PRIMARY KEY (employee_id),
  CONSTRAINT uq_provider_npi UNIQUE (npi),
  CONSTRAINT fk_provider_employee FOREIGN KEY (employee_id)
    REFERENCES Employee (employee_id),
  CONSTRAINT ck_provider_npi CHECK (npi REGEXP '^[0-9]{10}$')
) ENGINE = InnoDB;

-- ---------------------------------------------------------------------
-- 5. Staff: subtype of Employee (front desk, scheduler, manager)
-- ---------------------------------------------------------------------
CREATE TABLE Staff (
  employee_id INT         NOT NULL,
  job_title   VARCHAR(60) NOT NULL,
  CONSTRAINT pk_staff PRIMARY KEY (employee_id),
  CONSTRAINT fk_staff_employee FOREIGN KEY (employee_id)
    REFERENCES Employee (employee_id)
) ENGINE = InnoDB;

-- ---------------------------------------------------------------------
-- 6. EmployeeLocation: M:N between Employee and Location
-- ---------------------------------------------------------------------
CREATE TABLE EmployeeLocation (
  employee_id INT NOT NULL,
  location_id INT NOT NULL,
  CONSTRAINT pk_employee_location PRIMARY KEY (employee_id, location_id),
  CONSTRAINT fk_emploc_employee FOREIGN KEY (employee_id)
    REFERENCES Employee (employee_id) ON DELETE CASCADE,
  CONSTRAINT fk_emploc_location FOREIGN KEY (location_id)
    REFERENCES Location (location_id) ON DELETE CASCADE
) ENGINE = InnoDB;

-- ---------------------------------------------------------------------
-- 7. Patient: each patient has one required home location
-- ---------------------------------------------------------------------
CREATE TABLE Patient (
  patient_id       INT          NOT NULL AUTO_INCREMENT,
  home_location_id INT          NOT NULL,
  first_name       VARCHAR(50)  NOT NULL,
  last_name        VARCHAR(50)  NOT NULL,
  date_of_birth    DATE         NOT NULL,
  email            VARCHAR(100) NULL,
  phone            VARCHAR(20)  NOT NULL,
  street           VARCHAR(150) NULL,
  city             VARCHAR(60)  NULL,
  state            CHAR(2)      NULL,
  zip              CHAR(5)      NULL,
  is_active        BOOLEAN      NOT NULL DEFAULT TRUE,
  CONSTRAINT pk_patient PRIMARY KEY (patient_id),
  CONSTRAINT uq_patient_email UNIQUE (email),
  CONSTRAINT fk_patient_home_location FOREIGN KEY (home_location_id)
    REFERENCES Location (location_id),
  CONSTRAINT ck_patient_zip CHECK (zip IS NULL OR zip REGEXP '^[0-9]{5}$'),
  INDEX idx_patient_name (last_name, first_name)
) ENGINE = InnoDB;

-- ---------------------------------------------------------------------
-- 8. ServiceType: rheumatology services and the room type each needs
-- ---------------------------------------------------------------------
CREATE TABLE ServiceType (
  service_type_id      INT                         NOT NULL AUTO_INCREMENT,
  name                 VARCHAR(60)                 NOT NULL,
  default_duration_min SMALLINT UNSIGNED           NOT NULL,
  required_room_type   ENUM('Standard','Infusion') NOT NULL DEFAULT 'Standard',
  is_active            BOOLEAN                     NOT NULL DEFAULT TRUE,
  CONSTRAINT pk_service_type PRIMARY KEY (service_type_id),
  CONSTRAINT uq_service_type_name UNIQUE (name),
  -- Durations fit the 30-minute slot grid
  CONSTRAINT ck_service_duration CHECK (
    default_duration_min > 0 AND default_duration_min % 30 = 0)
) ENGINE = InnoDB;

-- ---------------------------------------------------------------------
-- 9. ProviderQualification: M:N between Provider and ServiceType
-- ---------------------------------------------------------------------
CREATE TABLE ProviderQualification (
  employee_id     INT NOT NULL,
  service_type_id INT NOT NULL,
  CONSTRAINT pk_provider_qualification PRIMARY KEY (employee_id, service_type_id),
  CONSTRAINT fk_provqual_provider FOREIGN KEY (employee_id)
    REFERENCES Provider (employee_id) ON DELETE CASCADE,
  CONSTRAINT fk_provqual_service FOREIGN KEY (service_type_id)
    REFERENCES ServiceType (service_type_id) ON DELETE CASCADE
) ENGINE = InnoDB;

-- ---------------------------------------------------------------------
-- 10. Medication: catalog of drugs administered in clinic
-- ---------------------------------------------------------------------
CREATE TABLE Medication (
  medication_id INT                                               NOT NULL AUTO_INCREMENT,
  generic_name  VARCHAR(100)                                      NOT NULL,
  brand_name    VARCHAR(100)                                      NULL,
  drug_class    ENUM('Biologic','Corticosteroid','Premedication') NOT NULL,
  is_active     BOOLEAN                                           NOT NULL DEFAULT TRUE,
  CONSTRAINT pk_medication PRIMARY KEY (medication_id),
  CONSTRAINT uq_medication_generic UNIQUE (generic_name)
) ENGINE = InnoDB;

-- ---------------------------------------------------------------------
-- 11. TreatmentPlan: header ordered by an MD, NP, or PA
-- ---------------------------------------------------------------------
CREATE TABLE TreatmentPlan (
  plan_id              INT                                       NOT NULL AUTO_INCREMENT,
  patient_id           INT                                       NOT NULL,
  ordering_provider_id INT                                       NOT NULL,
  start_date           DATE                                      NOT NULL,
  end_date             DATE                                      NULL,
  status               ENUM('Active','Completed','Discontinued') NOT NULL DEFAULT 'Active',
  notes                TEXT                                      NULL,
  CONSTRAINT pk_treatment_plan PRIMARY KEY (plan_id),
  CONSTRAINT fk_plan_patient FOREIGN KEY (patient_id)
    REFERENCES Patient (patient_id),
  CONSTRAINT fk_plan_provider FOREIGN KEY (ordering_provider_id)
    REFERENCES Provider (employee_id),
  CONSTRAINT ck_plan_dates CHECK (end_date IS NULL OR end_date >= start_date)
) ENGINE = InnoDB;

-- ---------------------------------------------------------------------
-- 12. TreatmentPlanItem: one recurring service at a fixed interval
-- ---------------------------------------------------------------------
CREATE TABLE TreatmentPlanItem (
  plan_item_id    INT               NOT NULL AUTO_INCREMENT,
  plan_id         INT               NOT NULL,
  service_type_id INT               NOT NULL,
  medication_id   INT               NULL,
  interval_weeks  TINYINT UNSIGNED  NOT NULL,
  total_sessions  SMALLINT UNSIGNED NULL,
  CONSTRAINT pk_plan_item PRIMARY KEY (plan_item_id),
  CONSTRAINT fk_planitem_plan FOREIGN KEY (plan_id)
    REFERENCES TreatmentPlan (plan_id) ON DELETE CASCADE,
  CONSTRAINT fk_planitem_service FOREIGN KEY (service_type_id)
    REFERENCES ServiceType (service_type_id),
  CONSTRAINT fk_planitem_medication FOREIGN KEY (medication_id)
    REFERENCES Medication (medication_id),
  CONSTRAINT ck_planitem_interval CHECK (interval_weeks BETWEEN 1 AND 52),
  CONSTRAINT ck_planitem_sessions CHECK (total_sessions IS NULL OR total_sessions > 0)
) ENGINE = InnoDB;

-- ---------------------------------------------------------------------
-- 13. Appointment: the central entity
--     Location is derived through Room -> Location (not stored, for 3NF).
-- ---------------------------------------------------------------------
CREATE TABLE Appointment (
  appointment_id      INT                                              NOT NULL AUTO_INCREMENT,
  patient_id          INT                                              NOT NULL,
  provider_id         INT                                              NOT NULL,
  room_id             INT                                              NOT NULL,
  service_type_id     INT                                              NOT NULL,
  plan_item_id        INT                                              NULL,
  rescheduled_from_id INT                                              NULL,
  created_by          INT                                              NOT NULL,
  start_time          DATETIME                                         NOT NULL,
  end_time            DATETIME                                         NOT NULL,
  status              ENUM('Scheduled','Completed','Canceled','Missed') NOT NULL DEFAULT 'Scheduled',
  created_at          DATETIME                                         NOT NULL DEFAULT CURRENT_TIMESTAMP,
  notes               TEXT                                             NULL,
  CONSTRAINT pk_appointment PRIMARY KEY (appointment_id),
  -- A missed appointment is rescheduled at most once (1:0..1)
  CONSTRAINT uq_appointment_rescheduled_from UNIQUE (rescheduled_from_id),
  CONSTRAINT fk_appt_patient FOREIGN KEY (patient_id)
    REFERENCES Patient (patient_id),
  CONSTRAINT fk_appt_provider FOREIGN KEY (provider_id)
    REFERENCES Provider (employee_id),
  CONSTRAINT fk_appt_room FOREIGN KEY (room_id)
    REFERENCES Room (room_id),
  CONSTRAINT fk_appt_service FOREIGN KEY (service_type_id)
    REFERENCES ServiceType (service_type_id),
  CONSTRAINT fk_appt_plan_item FOREIGN KEY (plan_item_id)
    REFERENCES TreatmentPlanItem (plan_item_id),
  CONSTRAINT fk_appt_rescheduled_from FOREIGN KEY (rescheduled_from_id)
    REFERENCES Appointment (appointment_id),
  CONSTRAINT fk_appt_created_by FOREIGN KEY (created_by)
    REFERENCES Staff (employee_id),
  -- Time-slot rules: same weekday, 8 AM-5 PM, on :00 or :30
  CONSTRAINT ck_appt_order CHECK (end_time > start_time),
  CONSTRAINT ck_appt_same_day CHECK (DATE(start_time) = DATE(end_time)),
  CONSTRAINT ck_appt_weekday CHECK (DAYOFWEEK(start_time) BETWEEN 2 AND 6),
  CONSTRAINT ck_appt_hours CHECK (
    TIME(start_time) >= '08:00:00' AND TIME(end_time) <= '17:00:00'),
  CONSTRAINT ck_appt_slot_grid CHECK (
    MINUTE(start_time) IN (0, 30) AND SECOND(start_time) = 0 AND
    MINUTE(end_time)   IN (0, 30) AND SECOND(end_time)   = 0),
  -- Indexes for conflict checks and schedule views
  INDEX idx_appt_provider_time (provider_id, start_time),
  INDEX idx_appt_room_time (room_id, start_time),
  INDEX idx_appt_patient_time (patient_id, start_time),
  INDEX idx_appt_status_time (status, start_time)
) ENGINE = InnoDB;

-- ---------------------------------------------------------------------
-- 14. MedicationAdministration: one row per dose given (M:N Appointment-Medication)
-- ---------------------------------------------------------------------
CREATE TABLE MedicationAdministration (
  administration_id INT                               NOT NULL AUTO_INCREMENT,
  appointment_id    INT                               NOT NULL,
  medication_id     INT                               NOT NULL,
  administered_by   INT                               NOT NULL,
  dose              DECIMAL(8,2)                      NOT NULL,
  dose_unit         ENUM('mg','mg/kg','mL')           NOT NULL,
  route             ENUM('IV','PO','Intra-articular') NOT NULL,
  site              VARCHAR(50)                       NULL,
  lot_number        VARCHAR(30)                       NOT NULL,
  reason            ENUM('Scheduled','As-needed')     NOT NULL DEFAULT 'Scheduled',
  administered_at   DATETIME                          NOT NULL,
  CONSTRAINT pk_med_admin PRIMARY KEY (administration_id),
  CONSTRAINT fk_medadmin_appointment FOREIGN KEY (appointment_id)
    REFERENCES Appointment (appointment_id),
  CONSTRAINT fk_medadmin_medication FOREIGN KEY (medication_id)
    REFERENCES Medication (medication_id),
  CONSTRAINT fk_medadmin_provider FOREIGN KEY (administered_by)
    REFERENCES Provider (employee_id),
  CONSTRAINT ck_medadmin_dose CHECK (dose > 0),
  -- Joint injections must record the injection site
  CONSTRAINT ck_medadmin_site CHECK (route <> 'Intra-articular' OR site IS NOT NULL),
  INDEX idx_medadmin_medication_time (medication_id, administered_at)
) ENGINE = InnoDB;

-- ---------------------------------------------------------------------
-- 15. AppointmentRequest: patient-submitted booking requests
-- ---------------------------------------------------------------------
CREATE TABLE AppointmentRequest (
  request_id            INT                                  NOT NULL AUTO_INCREMENT,
  patient_id            INT                                  NOT NULL,
  service_type_id       INT                                  NOT NULL,
  preferred_location_id INT                                  NULL,
  preferred_start       DATETIME                             NOT NULL,
  patient_notes         TEXT                                 NULL,
  status                ENUM('Pending','Approved','Denied')  NOT NULL DEFAULT 'Pending',
  submitted_at          DATETIME                             NOT NULL DEFAULT CURRENT_TIMESTAMP,
  reviewed_by           INT                                  NULL,
  reviewed_at           DATETIME                             NULL,
  decision_note         VARCHAR(255)                         NULL,
  appointment_id        INT                                  NULL,
  CONSTRAINT pk_appointment_request PRIMARY KEY (request_id),
  -- An approved request produces at most one appointment (1:0..1)
  CONSTRAINT uq_apptreq_appointment UNIQUE (appointment_id),
  CONSTRAINT fk_apptreq_patient FOREIGN KEY (patient_id)
    REFERENCES Patient (patient_id),
  CONSTRAINT fk_apptreq_service FOREIGN KEY (service_type_id)
    REFERENCES ServiceType (service_type_id),
  CONSTRAINT fk_apptreq_location FOREIGN KEY (preferred_location_id)
    REFERENCES Location (location_id),
  CONSTRAINT fk_apptreq_reviewer FOREIGN KEY (reviewed_by)
    REFERENCES Staff (employee_id),
  CONSTRAINT fk_apptreq_appointment FOREIGN KEY (appointment_id)
    REFERENCES Appointment (appointment_id),
  -- Review fields must match the status
  CONSTRAINT ck_apptreq_status CHECK (
    (status = 'Pending'  AND reviewed_by IS NULL     AND reviewed_at IS NULL     AND appointment_id IS NULL) OR
    (status = 'Approved' AND reviewed_by IS NOT NULL AND reviewed_at IS NOT NULL AND appointment_id IS NOT NULL) OR
    (status = 'Denied'   AND reviewed_by IS NOT NULL AND reviewed_at IS NOT NULL AND appointment_id IS NULL)),
  CONSTRAINT ck_apptreq_review_time CHECK (reviewed_at IS NULL OR reviewed_at >= submitted_at),
  INDEX idx_apptreq_status (status, submitted_at)
) ENGINE = InnoDB;

-- ---------------------------------------------------------------------
-- 16. CancellationRequest: from patients (reviewed) or the clinic
--     (approved automatically on insert)
-- ---------------------------------------------------------------------
CREATE TABLE CancellationRequest (
  cancel_request_id INT                                 NOT NULL AUTO_INCREMENT,
  appointment_id    INT                                 NOT NULL,
  source            ENUM('Patient','Clinic')            NOT NULL,
  submitted_by      INT                                 NULL,
  reason            VARCHAR(255)                        NOT NULL,
  status            ENUM('Pending','Approved','Denied') NOT NULL DEFAULT 'Pending',
  submitted_at      DATETIME                            NOT NULL DEFAULT CURRENT_TIMESTAMP,
  reviewed_by       INT                                 NULL,
  reviewed_at       DATETIME                            NULL,
  CONSTRAINT pk_cancellation_request PRIMARY KEY (cancel_request_id),
  CONSTRAINT fk_cancel_appointment FOREIGN KEY (appointment_id)
    REFERENCES Appointment (appointment_id),
  CONSTRAINT fk_cancel_submitted_by FOREIGN KEY (submitted_by)
    REFERENCES Staff (employee_id),
  CONSTRAINT fk_cancel_reviewed_by FOREIGN KEY (reviewed_by)
    REFERENCES Staff (employee_id),
  -- Patient requests have no staff submitter; clinic requests must have one
  CONSTRAINT ck_cancel_source CHECK (
    (source = 'Patient' AND submitted_by IS NULL) OR
    (source = 'Clinic'  AND submitted_by IS NOT NULL)),
  -- Review fields must match the status
  CONSTRAINT ck_cancel_status CHECK (
    (status = 'Pending'  AND reviewed_by IS NULL     AND reviewed_at IS NULL) OR
    (status <> 'Pending' AND reviewed_by IS NOT NULL AND reviewed_at IS NOT NULL)),
  -- Clinic cancellations are approved by the staff member who submits them
  CONSTRAINT ck_cancel_clinic_auto CHECK (
    source = 'Patient' OR (status = 'Approved' AND reviewed_by = submitted_by)),
  CONSTRAINT ck_cancel_review_time CHECK (reviewed_at IS NULL OR reviewed_at >= submitted_at),
  INDEX idx_cancel_status (status, submitted_at)
) ENGINE = InnoDB;

-- ---------------------------------------------------------------------
-- 17. AuditLog: filled by triggers; changed_by comes from @current_employee_id
-- ---------------------------------------------------------------------
CREATE TABLE AuditLog (
  audit_id    BIGINT                           NOT NULL AUTO_INCREMENT,
  table_name  VARCHAR(64)                      NOT NULL,
  record_id   INT                              NOT NULL,
  action      ENUM('INSERT','UPDATE','DELETE') NOT NULL,
  changed_by  INT                              NULL,
  changed_at  DATETIME                         NOT NULL DEFAULT CURRENT_TIMESTAMP,
  old_values  JSON                             NULL,
  new_values  JSON                             NULL,
  CONSTRAINT pk_audit_log PRIMARY KEY (audit_id),
  CONSTRAINT fk_audit_employee FOREIGN KEY (changed_by)
    REFERENCES Employee (employee_id),
  INDEX idx_audit_record (table_name, record_id),
  INDEX idx_audit_time (changed_at)
) ENGINE = InnoDB;

-- ---------------------------------------------------------------------
-- Verify: should list 17 tables
-- ---------------------------------------------------------------------
SHOW TABLES;
