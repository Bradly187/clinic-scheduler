-- =====================================================================
-- ClinicScheduler V2: Sample Data
-- PostgreSQL port of db/mysql/sample_data_project.sql (same 227 rows).
-- Apply after 01_schema.sql and 02_rules.sql.
--
-- Unlike the MySQL load order, the triggers already exist when this runs,
-- so every row is checked against every scheduling rule as it loads.
-- Dates span October 2026 (completed visits) through January 2027
-- (scheduled visits). All people, emails, phone numbers (555) and
-- NPI numbers are fictional.
-- =====================================================================

-- Locations (3 Houston sites)
INSERT INTO location (location_id, name, street, zip, phone) VALUES
  (1, 'Houston Arthritis Center - Medical Center', '4100 Kirby Dr, Suite 200', '77098', '713-555-0100'),
  (2, 'Houston Arthritis Center - Memorial City', '9450 Katy Fwy, Suite 310', '77055', '713-555-0200'),
  (3, 'Houston Arthritis Center - Clear Lake', '17200 El Camino Real, Suite 120', '77058', '281-555-0300');

-- Rooms: 4 standard rooms + 1 infusion suite (3 chairs) per location
INSERT INTO room (room_id, location_id, room_name, room_type, capacity) VALUES
  (1, 1, 'Exam 1', 'Standard', 1),
  (2, 1, 'Exam 2', 'Standard', 1),
  (3, 1, 'Exam 3', 'Standard', 1),
  (4, 1, 'Procedure Room', 'Standard', 1),
  (5, 1, 'Infusion Suite', 'Infusion', 3),
  (6, 2, 'Exam 1', 'Standard', 1),
  (7, 2, 'Exam 2', 'Standard', 1),
  (8, 2, 'Exam 3', 'Standard', 1),
  (9, 2, 'Procedure Room', 'Standard', 1),
  (10, 2, 'Infusion Suite', 'Infusion', 3),
  (11, 3, 'Exam 1', 'Standard', 1),
  (12, 3, 'Exam 2', 'Standard', 1),
  (13, 3, 'Exam 3', 'Standard', 1),
  (14, 3, 'Procedure Room', 'Standard', 1),
  (15, 3, 'Infusion Suite', 'Infusion', 3);

-- Employees: 1-8 providers, 9-13 staff
INSERT INTO employee (employee_id, first_name, last_name, email, phone, hire_date) VALUES
  (1, 'Maria', 'Gonzalez', 'mgonzalez@hac-clinic.example', '713-555-1001', '2015-08-03'),
  (2, 'James', 'Whitfield', 'jwhitfield@hac-clinic.example', '713-555-1002', '2017-01-09'),
  (3, 'Priya', 'Nair', 'pnair@hac-clinic.example', '713-555-1003', '2019-06-17'),
  (4, 'Daniel', 'Okafor', 'dokafor@hac-clinic.example', '713-555-1004', '2020-02-24'),
  (5, 'Rachel', 'Kim', 'rkim@hac-clinic.example', '713-555-1005', '2018-04-02'),
  (6, 'Luis', 'Herrera', 'lherrera@hac-clinic.example', '713-555-1006', '2021-09-13'),
  (7, 'Emily', 'Tran', 'etran@hac-clinic.example', '713-555-1007', '2022-03-07'),
  (8, 'Sarah', 'Mitchell', 'smitchell@hac-clinic.example', '713-555-1008', '2016-11-14'),
  (9, 'Jessica', 'Moreno', 'jmoreno@hac-clinic.example', '713-555-1009', '2019-01-07'),
  (10, 'Kevin', 'Brooks', 'kbrooks@hac-clinic.example', '713-555-1010', '2020-07-20'),
  (11, 'Angela', 'Reyes', 'areyes@hac-clinic.example', '713-555-1011', '2014-05-12'),
  (12, 'Tyler', 'Nguyen', 'tnguyen@hac-clinic.example', '281-555-1012', '2023-02-06'),
  (13, 'Monica', 'Patel', 'mpatel@hac-clinic.example', '713-555-1013', '2021-10-04');

-- Providers (subtype of Employee)
INSERT INTO provider (employee_id, npi, credential) VALUES
  (1, '1407852963', 'MD'),
  (2, '1588231470', 'MD'),
  (3, '1730496285', 'NP'),
  (4, '1295840317', 'PA'),
  (5, '1902374658', 'RN'),
  (6, '1649027381', 'RN'),
  (7, '1376518042', 'RN'),
  (8, '1823659074', 'RN');

-- Staff (subtype of Employee)
INSERT INTO staff (employee_id, job_title) VALUES
  (9, 'Front Desk'),
  (10, 'Scheduler'),
  (11, 'Clinic Manager'),
  (12, 'Front Desk'),
  (13, 'Scheduler');

-- Where each employee works (M:N)
INSERT INTO employee_location (employee_id, location_id) VALUES
  (1, 1),
  (1, 2),
  (2, 2),
  (2, 3),
  (3, 1),
  (3, 3),
  (4, 1),
  (4, 2),
  (4, 3),
  (5, 1),
  (5, 2),
  (6, 1),
  (6, 3),
  (7, 2),
  (7, 3),
  (8, 1),
  (8, 2),
  (8, 3),
  (9, 1),
  (10, 1),
  (10, 2),
  (11, 1),
  (11, 2),
  (11, 3),
  (12, 3),
  (13, 2),
  (13, 3);

-- Patients
INSERT INTO patient (patient_id, home_location_id, first_name, last_name, date_of_birth, email, phone, street, city, zip) VALUES
  (1, 1, 'Linda', 'Carter', '1958-03-14', 'linda.carter@mail.example', '713-555-2001', '2718 Westheimer Rd', 'Houston', '77098'),
  (2, 1, 'Robert', 'Alvarez', '1965-07-22', 'r.alvarez@mail.example', '713-555-2002', '5310 Bissonnet St', 'Bellaire', '77401'),
  (3, 1, 'Patricia', 'Nguyen', '1972-11-03', 'pnguyen72@mail.example', '713-555-2003', '1846 Bolsover St', 'Houston', '77005'),
  (4, 2, 'Michael', 'Johnson', '1980-01-19', 'mjohnson80@mail.example', '713-555-2004', '1422 Blalock Rd', 'Houston', '77055'),
  (5, 2, 'Jennifer', 'Lee', '1969-09-30', 'jen.lee@mail.example', '713-555-2005', '11807 Taylorcrest Rd', 'Houston', '77024'),
  (6, 2, 'David', 'Ramirez', '1975-04-12', 'd.ramirez@mail.example', '281-555-2006', '3215 Westgreen Blvd', 'Katy', '77449'),
  (7, 3, 'Susan', 'Brooks', '1950-12-08', 'susan.brooks@mail.example', '281-555-2007', '1602 Bay Area Blvd', 'Houston', '77058'),
  (8, 3, 'Christopher', 'Hall', '1988-06-25', 'chall88@mail.example', '281-555-2008', '420 Marina Bay Dr', 'Kemah', '77565'),
  (9, 3, 'Karen', 'Wright', '1962-02-17', 'kwright@mail.example', '281-555-2009', '2405 W Main St', 'League City', '77573'),
  (10, 1, 'Anthony', 'Davis', '1979-08-05', 'adavis79@mail.example', '713-555-2010', '6620 Main St', 'Houston', '77030'),
  (11, 2, 'Maria', 'Santos', '1984-10-11', 'msantos@mail.example', '713-555-2011', '8902 Long Point Rd', 'Houston', '77055'),
  (12, 3, 'Thomas', 'Green', '1955-05-29', 'tgreen55@mail.example', '281-555-2012', '1118 Clear Lake City Blvd', 'Houston', '77062');

-- Service types
INSERT INTO service_type (service_type_id, name, default_duration_min, required_room_type) VALUES
  (1, 'New-patient evaluation', 60, 'Standard'),
  (2, 'Follow-up visit', 30, 'Standard'),
  (3, 'IV biologic infusion', 120, 'Infusion'),
  (4, 'Joint injection', 30, 'Standard'),
  (5, 'Lab draw', 30, 'Standard');

-- Provider qualifications: MD/NP/PA do evaluations, follow-ups, injections; RNs do infusions and labs
INSERT INTO provider_qualification (employee_id, service_type_id) VALUES
  (1, 1),
  (1, 2),
  (1, 4),
  (2, 1),
  (2, 2),
  (2, 4),
  (3, 1),
  (3, 2),
  (3, 4),
  (4, 1),
  (4, 2),
  (4, 4),
  (5, 3),
  (5, 5),
  (6, 3),
  (6, 5),
  (7, 3),
  (7, 5),
  (8, 3),
  (8, 5);

-- Medication catalog
INSERT INTO medication (medication_id, generic_name, brand_name, drug_class) VALUES
  (1, 'infliximab', 'Remicade', 'Biologic'),
  (2, 'rituximab', 'Rituxan', 'Biologic'),
  (3, 'tocilizumab', 'Actemra', 'Biologic'),
  (4, 'abatacept', 'Orencia', 'Biologic'),
  (5, 'golimumab', 'Simponi Aria', 'Biologic'),
  (6, 'triamcinolone acetonide', 'Kenalog-40', 'Corticosteroid'),
  (7, 'methylprednisolone acetate', 'Depo-Medrol', 'Corticosteroid'),
  (8, 'acetaminophen', 'Tylenol', 'Premedication'),
  (9, 'diphenhydramine', 'Benadryl', 'Premedication'),
  (10, 'ondansetron', 'Zofran', 'Premedication');

-- Treatment plans
INSERT INTO treatment_plan (plan_id, patient_id, ordering_provider_id, start_date, end_date, status, notes) VALUES
  (1, 1, 1, '2026-06-01', NULL, 'Active', 'Rheumatoid arthritis; infliximab every 8 weeks with labs before each infusion'),
  (2, 2, 3, '2026-04-15', NULL, 'Active', 'Rituximab course; follow-up every 12 weeks'),
  (3, 4, 2, '2026-05-04', NULL, 'Active', 'Tocilizumab every 4 weeks; monitor labs'),
  (4, 5, 1, '2026-03-09', NULL, 'Active', 'Abatacept every 4 weeks'),
  (5, 7, 2, '2026-02-16', NULL, 'Active', 'Golimumab IV every 8 weeks'),
  (6, 9, 3, '2026-07-13', NULL, 'Active', 'Infliximab every 8 weeks'),
  (7, 10, 4, '2026-08-10', NULL, 'Active', 'Tocilizumab every 4 weeks with labs'),
  (8, 3, 4, '2026-07-20', NULL, 'Active', 'Knee osteoarthritis; corticosteroid injection every 12 weeks, up to 4'),
  (9, 8, 3, '2026-07-22', NULL, 'Active', 'Corticosteroid injection every 12 weeks, up to 3'),
  (10, 12, 2, '2026-04-08', '2027-04-08', 'Active', 'Monitoring after taper; follow-up and labs every 12 weeks');

-- Treatment plan items (fixed intervals)
INSERT INTO treatment_plan_item (plan_item_id, plan_id, service_type_id, medication_id, interval_weeks, total_sessions) VALUES
  (1, 1, 3, 1, 8, NULL),
  (2, 1, 5, NULL, 8, NULL),
  (3, 1, 2, NULL, 12, NULL),
  (4, 2, 3, 2, 24, 4),
  (5, 2, 2, NULL, 12, NULL),
  (6, 3, 3, 3, 4, NULL),
  (7, 3, 5, NULL, 4, NULL),
  (8, 4, 3, 4, 4, NULL),
  (9, 4, 2, NULL, 12, NULL),
  (10, 5, 3, 5, 8, NULL),
  (11, 5, 5, NULL, 8, NULL),
  (12, 6, 3, 1, 8, NULL),
  (13, 7, 3, 3, 4, NULL),
  (14, 7, 5, NULL, 4, NULL),
  (15, 8, 4, 6, 12, 4),
  (16, 9, 4, 7, 12, 3),
  (17, 10, 2, NULL, 12, 4),
  (18, 10, 5, NULL, 12, 4);

-- Appointments: past (Completed / Missed / Canceled) and upcoming (Scheduled)
INSERT INTO appointment (appointment_id, patient_id, provider_id, room_id, service_type_id, plan_item_id, rescheduled_from_id, created_by, start_time, end_time, status, created_at, notes) VALUES
  (1, 1, 5, 5, 3, 1, NULL, 10, '2026-10-05 09:00:00', '2026-10-05 11:00:00', 'Completed', '2026-09-25 09:00:00', NULL),
  (2, 2, 6, 5, 3, 4, NULL, 10, '2026-10-05 09:00:00', '2026-10-05 13:00:00', 'Completed', '2026-09-25 09:00:00', 'Rituximab runs about 4 hours'),
  (3, 10, 8, 5, 3, 13, NULL, 9, '2026-10-05 10:00:00', '2026-10-05 12:00:00', 'Completed', '2026-09-25 09:00:00', NULL),
  (4, 1, 5, 3, 5, 2, NULL, 10, '2026-10-05 08:00:00', '2026-10-05 08:30:00', 'Completed', '2026-09-25 09:00:00', 'Pre-infusion CBC and CMP'),
  (5, 4, 7, 10, 3, 6, NULL, 13, '2026-10-07 09:00:00', '2026-10-07 11:00:00', 'Completed', '2026-09-27 09:00:00', NULL),
  (6, 5, 8, 10, 3, 8, NULL, 13, '2026-10-07 09:00:00', '2026-10-07 10:00:00', 'Completed', '2026-09-27 09:00:00', NULL),
  (7, 4, 7, 9, 5, 7, NULL, 13, '2026-10-07 08:00:00', '2026-10-07 08:30:00', 'Completed', '2026-09-27 09:00:00', NULL),
  (8, 7, 6, 15, 3, 10, NULL, 12, '2026-10-08 13:00:00', '2026-10-08 14:00:00', 'Completed', '2026-09-28 09:00:00', NULL),
  (9, 9, 7, 15, 3, 12, NULL, 12, '2026-10-08 13:00:00', '2026-10-08 15:00:00', 'Completed', '2026-09-28 09:00:00', NULL),
  (10, 3, 4, 4, 4, 15, NULL, 9, '2026-10-12 10:00:00', '2026-10-12 10:30:00', 'Completed', '2026-10-02 09:00:00', 'Right knee'),
  (11, 8, 3, 14, 4, 16, NULL, 12, '2026-10-14 14:00:00', '2026-10-14 14:30:00', 'Completed', '2026-10-04 09:00:00', 'Left knee'),
  (12, 12, 2, 11, 2, 17, NULL, 12, '2026-10-14 09:00:00', '2026-10-14 09:30:00', 'Completed', '2026-10-04 09:00:00', NULL),
  (13, 6, 1, 6, 2, NULL, NULL, 13, '2026-10-19 10:00:00', '2026-10-19 10:30:00', 'Missed', '2026-10-09 09:00:00', 'Patient did not arrive'),
  (14, 6, 1, 6, 2, NULL, 13, 13, '2026-10-21 10:00:00', '2026-10-21 10:30:00', 'Completed', '2026-10-19 10:30:00', 'Rescheduled from missed visit'),
  (15, 11, 2, 7, 1, NULL, NULL, 10, '2026-10-20 13:00:00', '2026-10-20 14:00:00', 'Completed', '2026-10-10 09:00:00', 'New patient: suspected lupus'),
  (16, 1, 1, 1, 2, 3, NULL, 9, '2026-10-26 11:30:00', '2026-10-26 12:00:00', 'Completed', '2026-10-16 09:00:00', NULL),
  (17, 5, 1, 6, 2, 9, NULL, 10, '2026-10-28 09:00:00', '2026-10-28 09:30:00', 'Canceled', '2026-10-18 09:00:00', NULL),
  (18, 2, 3, 2, 2, 5, NULL, 9, '2026-11-02 14:00:00', '2026-11-02 14:30:00', 'Canceled', '2026-10-23 09:00:00', NULL),
  (19, 10, 8, 3, 5, 14, NULL, 9, '2026-11-02 08:00:00', '2026-11-02 08:30:00', 'Completed', '2026-10-23 09:00:00', NULL),
  (20, 4, 7, 10, 3, 6, NULL, 13, '2026-11-04 09:00:00', '2026-11-04 11:00:00', 'Completed', '2026-10-25 09:00:00', NULL),
  (21, 5, 8, 10, 3, 8, NULL, 13, '2026-11-04 09:00:00', '2026-11-04 10:00:00', 'Completed', '2026-10-25 09:00:00', NULL),
  (22, 10, 8, 5, 3, 13, NULL, 9, '2026-11-02 09:00:00', '2026-11-02 11:00:00', 'Completed', '2026-10-23 09:00:00', NULL),
  (23, 9, 7, 13, 5, NULL, NULL, 12, '2026-11-09 08:00:00', '2026-11-09 08:30:00', 'Canceled', '2026-10-30 09:00:00', NULL),
  (24, 12, 2, 11, 2, NULL, NULL, 12, '2026-11-11 10:00:00', '2026-11-11 10:30:00', 'Canceled', '2026-11-01 09:00:00', NULL),
  (25, 2, 3, 2, 2, 5, NULL, 9, '2026-11-16 14:00:00', '2026-11-16 14:30:00', 'Scheduled', '2026-11-06 09:00:00', 'Rebooked after cancellation'),
  (26, 11, 2, 7, 2, NULL, NULL, 13, '2026-11-17 13:00:00', '2026-11-17 13:30:00', 'Scheduled', '2026-11-07 09:00:00', NULL),
  (27, 5, 1, 6, 2, 9, NULL, 13, '2026-11-18 09:00:00', '2026-11-18 09:30:00', 'Scheduled', '2026-11-08 09:00:00', 'Rebooked after clinic cancellation'),
  (28, 7, 2, 12, 2, NULL, NULL, 12, '2026-11-19 10:00:00', '2026-11-19 10:30:00', 'Scheduled', '2026-11-09 09:00:00', NULL),
  (29, 1, 1, 1, 2, NULL, NULL, 9, '2026-11-23 11:00:00', '2026-11-23 11:30:00', 'Scheduled', '2026-11-13 09:00:00', NULL),
  (30, 6, 1, 6, 2, NULL, NULL, 10, '2026-11-24 10:00:00', '2026-11-24 10:30:00', 'Scheduled', '2026-11-14 09:00:00', NULL),
  (31, 10, 4, 1, 2, NULL, NULL, 11, '2026-11-25 14:00:00', '2026-11-25 14:30:00', 'Canceled', '2026-11-15 09:00:00', NULL),
  (32, 1, 5, 3, 5, 2, NULL, 10, '2026-11-30 08:00:00', '2026-11-30 08:30:00', 'Scheduled', '2026-11-20 09:00:00', NULL),
  (33, 1, 5, 5, 3, 1, NULL, 10, '2026-11-30 09:00:00', '2026-11-30 11:00:00', 'Scheduled', '2026-11-20 09:00:00', NULL),
  (34, 10, 8, 5, 3, 13, NULL, 9, '2026-11-30 09:00:00', '2026-11-30 11:00:00', 'Scheduled', '2026-11-20 09:00:00', NULL),
  (35, 4, 7, 10, 3, 6, NULL, 13, '2026-12-02 09:00:00', '2026-12-02 11:00:00', 'Scheduled', '2026-11-22 09:00:00', NULL),
  (36, 5, 8, 10, 3, 8, NULL, 13, '2026-12-02 09:00:00', '2026-12-02 10:00:00', 'Scheduled', '2026-11-22 09:00:00', NULL),
  (37, 7, 6, 15, 3, 10, NULL, 12, '2026-12-03 13:00:00', '2026-12-03 14:00:00', 'Scheduled', '2026-11-23 09:00:00', NULL),
  (38, 9, 7, 15, 3, 12, NULL, 12, '2026-12-03 13:00:00', '2026-12-03 15:00:00', 'Scheduled', '2026-11-23 09:00:00', NULL),
  (39, 3, 4, 4, 4, 15, NULL, 9, '2027-01-04 10:00:00', '2027-01-04 10:30:00', 'Scheduled', '2026-12-25 09:00:00', NULL),
  (40, 12, 2, 11, 2, 17, NULL, 12, '2027-01-06 09:00:00', '2027-01-06 09:30:00', 'Scheduled', '2026-12-27 09:00:00', NULL);

-- Medications given (biologics, steroid injections, premedications)
INSERT INTO medication_administration (administration_id, appointment_id, medication_id, administered_by, dose, dose_unit, route, site, lot_number, reason, administered_at) VALUES
  (1, 1, 8, 5, 650, 'mg', 'PO', NULL, 'AC24-1187', 'Scheduled', '2026-10-05 09:00:00'),
  (2, 1, 9, 5, 25, 'mg', 'PO', NULL, 'DP25-0442', 'Scheduled', '2026-10-05 09:00:00'),
  (3, 1, 1, 5, 5, 'mg/kg', 'IV', NULL, 'IFX-77310', 'Scheduled', '2026-10-05 09:30:00'),
  (4, 2, 8, 6, 650, 'mg', 'PO', NULL, 'AC24-1187', 'Scheduled', '2026-10-05 09:00:00'),
  (5, 2, 9, 6, 50, 'mg', 'IV', NULL, 'DP25-0517', 'Scheduled', '2026-10-05 09:00:00'),
  (6, 2, 2, 6, 1000, 'mg', 'IV', NULL, 'RTX-40921', 'Scheduled', '2026-10-05 09:30:00'),
  (7, 2, 10, 6, 4, 'mg', 'IV', NULL, 'ON26-0093', 'As-needed', '2026-10-05 11:15:00'),
  (8, 3, 3, 8, 8, 'mg/kg', 'IV', NULL, 'TCZ-55102', 'Scheduled', '2026-10-05 10:00:00'),
  (9, 5, 3, 7, 8, 'mg/kg', 'IV', NULL, 'TCZ-55102', 'Scheduled', '2026-10-07 09:00:00'),
  (10, 6, 4, 8, 750, 'mg', 'IV', NULL, 'ABT-31876', 'Scheduled', '2026-10-07 09:00:00'),
  (11, 8, 5, 6, 2, 'mg/kg', 'IV', NULL, 'GLM-20457', 'Scheduled', '2026-10-08 13:00:00'),
  (12, 9, 8, 7, 650, 'mg', 'PO', NULL, 'AC24-1203', 'Scheduled', '2026-10-08 13:00:00'),
  (13, 9, 9, 7, 25, 'mg', 'PO', NULL, 'DP25-0442', 'Scheduled', '2026-10-08 13:00:00'),
  (14, 9, 1, 7, 5, 'mg/kg', 'IV', NULL, 'IFX-77344', 'Scheduled', '2026-10-08 13:30:00'),
  (15, 10, 6, 4, 40, 'mg', 'Intra-articular', 'Right knee', 'TRI-8812', 'Scheduled', '2026-10-12 10:05:00'),
  (16, 11, 7, 3, 40, 'mg', 'Intra-articular', 'Left knee', 'MPA-3390', 'Scheduled', '2026-10-14 14:05:00'),
  (17, 20, 3, 7, 8, 'mg/kg', 'IV', NULL, 'TCZ-55218', 'Scheduled', '2026-11-04 09:00:00'),
  (18, 20, 10, 7, 4, 'mg', 'PO', NULL, 'ON26-0101', 'As-needed', '2026-11-04 10:00:00'),
  (19, 21, 4, 8, 750, 'mg', 'IV', NULL, 'ABT-31902', 'Scheduled', '2026-11-04 09:00:00'),
  (20, 22, 3, 8, 8, 'mg/kg', 'IV', NULL, 'TCZ-55218', 'Scheduled', '2026-11-02 09:00:00');

-- Patient appointment requests
INSERT INTO appointment_request (request_id, patient_id, service_type_id, preferred_location_id, preferred_start, patient_notes, status, submitted_at, reviewed_by, reviewed_at, decision_note, appointment_id) VALUES
  (1, 11, 1, 2, '2026-10-20 13:00:00', 'Joint pain and rash; referred by PCP', 'Approved', '2026-10-09 15:12:00', 10, '2026-10-12 09:40:00', 'Booked with Dr. Whitfield', 15),
  (2, 2, 2, 1, '2026-11-16 14:00:00', 'Need to rebook canceled follow-up', 'Approved', '2026-10-30 11:05:00', 9, '2026-11-02 08:55:00', NULL, 25),
  (3, 5, 2, 2, '2026-11-18 09:00:00', 'Rebooking after clinic canceled', 'Approved', '2026-10-28 16:20:00', 13, '2026-10-29 10:15:00', NULL, 27),
  (4, 7, 2, 3, '2026-11-19 10:00:00', 'Stiffness increasing', 'Approved', '2026-11-05 09:30:00', 12, '2026-11-06 14:00:00', NULL, 28),
  (5, 8, 2, 3, '2026-11-21 10:00:00', 'Saturday preferred', 'Denied', '2026-11-03 18:45:00', 12, '2026-11-04 09:10:00', 'Clinic is closed on weekends; offered weekday slots', NULL),
  (6, 3, 4, 1, '2026-11-20 10:00:00', 'Knee pain returning early', 'Denied', '2026-11-06 12:00:00', 9, '2026-11-09 11:30:00', 'Next injection not due until January per treatment plan', NULL),
  (7, 6, 2, 2, '2026-12-07 10:00:00', NULL, 'Pending', '2026-11-10 08:15:00', NULL, NULL, NULL, NULL),
  (8, 12, 5, 3, '2026-12-09 08:00:00', 'Lab order from Dr. Whitfield', 'Pending', '2026-11-11 13:40:00', NULL, NULL, NULL, NULL),
  (9, 9, 2, 3, '2026-12-10 14:00:00', NULL, 'Pending', '2026-11-12 10:05:00', NULL, NULL, NULL, NULL),
  (10, 4, 2, 2, '2026-12-14 09:00:00', 'Questions about infusion side effects', 'Pending', '2026-11-12 17:30:00', NULL, NULL, NULL, NULL),
  (11, 10, 2, 1, '2026-12-01 14:00:00', 'Rebook canceled follow-up', 'Pending', '2026-11-12 19:02:00', NULL, NULL, NULL, NULL);

-- Cancellation requests (clinic-initiated ones are approved on insert)
INSERT INTO cancellation_request (cancel_request_id, appointment_id, source, submitted_by, reason, status, submitted_at, reviewed_by, reviewed_at) VALUES
  (1, 17, 'Clinic', 11, 'Provider out sick', 'Approved', '2026-10-27 16:40:00', 11, '2026-10-27 16:40:00'),
  (2, 18, 'Patient', NULL, 'Work conflict', 'Approved', '2026-10-29 09:12:00', 9, '2026-10-29 10:30:00'),
  (3, 20, 'Patient', NULL, 'Feeling unwell', 'Denied', '2026-11-03 17:55:00', 13, '2026-11-03 18:20:00'),
  (4, 23, 'Clinic', 12, 'Lab analyzer down for repair', 'Approved', '2026-11-06 15:00:00', 12, '2026-11-06 15:00:00'),
  (5, 24, 'Patient', NULL, 'Out of town', 'Approved', '2026-11-06 08:20:00', 12, '2026-11-06 09:05:00'),
  (6, 31, 'Clinic', 11, 'Provider schedule change', 'Approved', '2026-11-10 12:00:00', 11, '2026-11-10 12:00:00'),
  (7, 26, 'Patient', NULL, 'Family emergency', 'Pending', '2026-11-12 07:45:00', NULL, NULL),
  (8, 29, 'Patient', NULL, 'Transportation issue', 'Pending', '2026-11-12 11:20:00', NULL, NULL),
  (9, 30, 'Patient', NULL, 'Would like a later time', 'Denied', '2026-11-09 10:00:00', 10, '2026-11-09 13:15:00'),
  (10, 30, 'Patient', NULL, 'Cannot attend at all that week', 'Pending', '2026-11-11 16:10:00', NULL, NULL);

-- ---------------------------------------------------------------------
-- The rows above carry explicit ids, so move each identity sequence past
-- them; otherwise the next insert would collide with id 1.
-- ---------------------------------------------------------------------
SELECT setval(pg_get_serial_sequence('location', 'location_id'),                        (SELECT MAX(location_id) FROM location));
SELECT setval(pg_get_serial_sequence('room', 'room_id'),                                (SELECT MAX(room_id) FROM room));
SELECT setval(pg_get_serial_sequence('employee', 'employee_id'),                        (SELECT MAX(employee_id) FROM employee));
SELECT setval(pg_get_serial_sequence('patient', 'patient_id'),                          (SELECT MAX(patient_id) FROM patient));
SELECT setval(pg_get_serial_sequence('service_type', 'service_type_id'),                (SELECT MAX(service_type_id) FROM service_type));
SELECT setval(pg_get_serial_sequence('medication', 'medication_id'),                    (SELECT MAX(medication_id) FROM medication));
SELECT setval(pg_get_serial_sequence('treatment_plan', 'plan_id'),                      (SELECT MAX(plan_id) FROM treatment_plan));
SELECT setval(pg_get_serial_sequence('treatment_plan_item', 'plan_item_id'),            (SELECT MAX(plan_item_id) FROM treatment_plan_item));
SELECT setval(pg_get_serial_sequence('appointment', 'appointment_id'),                  (SELECT MAX(appointment_id) FROM appointment));
SELECT setval(pg_get_serial_sequence('medication_administration', 'administration_id'), (SELECT MAX(administration_id) FROM medication_administration));
SELECT setval(pg_get_serial_sequence('appointment_request', 'request_id'),              (SELECT MAX(request_id) FROM appointment_request));
SELECT setval(pg_get_serial_sequence('cancellation_request', 'cancel_request_id'),      (SELECT MAX(cancel_request_id) FROM cancellation_request));

-- Loading the sample rows fired the audit triggers. The audit log should
-- record changes made through the system, not the initial load.
TRUNCATE TABLE audit_log RESTART IDENTITY;
