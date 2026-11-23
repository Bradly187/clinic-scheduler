-- =====================================================================
-- ClinicScheduler V2: Sample data for billing, waitlist and notes
-- Apply after 06_care.sql, on a database that has the 03 sample data.
--
-- Every row goes in through the rules: invoices start as drafts, gain
-- lines, are sent, and are marked paid by the payments that cover them.
-- Login accounts are not created here: the app seeds demo accounts from
-- configuration, so no password (or hash) is committed with the scripts.
-- =====================================================================

-- Fees for each service
UPDATE service_type SET fee = CASE name
  WHEN 'New-patient evaluation' THEN 250.00
  WHEN 'Follow-up visit'        THEN 120.00
  WHEN 'IV biologic infusion'   THEN 650.00
  WHEN 'Joint injection'        THEN 180.00
  WHEN 'Lab draw'               THEN 45.00
  ELSE fee END;

-- Insurance on file
INSERT INTO insurance_policy (patient_id, payer_name, member_id, group_number, is_primary, effective_from) VALUES
  (1,  'Blue Cross Blue Shield of Texas', 'BCX448120931', 'G-77120', TRUE, '2026-01-01'),
  (2,  'Aetna',                           'W2849302711',  'G-30418', TRUE, '2025-07-01'),
  (3,  'Medicare Part B',                 '1EG4-TE5-MK72', NULL,     TRUE, '2024-03-01'),
  (11, 'UnitedHealthcare',                'UHC90012876',  'G-11873', TRUE, '2026-02-01');

-- Invoice 1: Linda Carter, October visits, paid in full by card
INSERT INTO invoice (invoice_id, patient_id, created_by, created_at, notes)
VALUES (1, 1, 11, '2026-10-27 10:00:00', 'October infusion, labs and follow-up');
INSERT INTO invoice_line (invoice_id, appointment_id, description, billing_code, quantity, unit_price) VALUES
  (1, 1,  'IV infusion administration, first hour', '96413', 1, 650.00),
  (1, 4,  'Venipuncture',                           '36415', 1, 45.00),
  (1, 16, 'Established patient follow-up',          '99213', 1, 120.00);
UPDATE invoice SET status = 'Sent', issued_on = '2026-10-27', due_on = '2026-11-26' WHERE invoice_id = 1;
INSERT INTO payment (invoice_id, amount, method, reference, paid_at, recorded_by)
VALUES (1, 815.00, 'Card', 'VISA-4417', '2026-11-03 14:20:00', 11);

-- Invoice 2: Robert Alvarez, rituximab infusion; insurance paid part of it
INSERT INTO invoice (invoice_id, patient_id, created_by, created_at, notes)
VALUES (2, 2, 11, '2026-10-12 09:30:00', 'Rituximab course, infusion 1 of 4');
INSERT INTO invoice_line (invoice_id, appointment_id, description, billing_code, quantity, unit_price) VALUES
  (2, 2,    'IV infusion administration, first hour', '96413', 1, 650.00),
  (2, NULL, 'Rituximab 1000 mg',                      'J9312', 1, 1200.00);
UPDATE invoice SET status = 'Sent', issued_on = '2026-10-12', due_on = '2026-12-11' WHERE invoice_id = 2;
INSERT INTO payment (invoice_id, amount, method, reference, paid_at, recorded_by)
VALUES (2, 500.00, 'Insurance', 'AET-EOB-2210493', '2026-11-06 11:00:00', 11);

-- Invoice 3: Patricia Nguyen, knee injection, still a draft
INSERT INTO invoice (invoice_id, patient_id, created_by, created_at)
VALUES (3, 3, 9, '2026-11-10 16:45:00');
INSERT INTO invoice_line (invoice_id, appointment_id, description, billing_code, quantity, unit_price) VALUES
  (3, 10,   'Major joint injection, right knee', '20610', 1, 180.00),
  (3, NULL, 'Triamcinolone acetonide 40 mg',     'J3301', 4, 6.25);

-- Invoice 4: Maria Santos, new-patient evaluation, sent and now past due
INSERT INTO invoice (invoice_id, patient_id, created_by, created_at)
VALUES (4, 11, 10, '2026-10-21 08:15:00');
INSERT INTO invoice_line (invoice_id, appointment_id, description, billing_code, quantity, unit_price)
VALUES (4, 15, 'New patient evaluation, moderate complexity', '99204', 1, 250.00);
UPDATE invoice SET status = 'Sent', issued_on = '2026-10-21', due_on = '2026-11-20' WHERE invoice_id = 4;

SELECT setval(pg_get_serial_sequence('invoice', 'invoice_id'), (SELECT MAX(invoice_id) FROM invoice));

-- Visit notes from the providers who saw the patients; the last one is unsigned
INSERT INTO visit_note (appointment_id, author_id, body, created_at, signed_at) VALUES
  (1,  5, 'Infliximab 5 mg/kg infused over 2 hours after acetaminophen and diphenhydramine. Vitals stable throughout; no infusion reaction. Next dose in 8 weeks.',
       '2026-10-05 11:05:00', '2026-10-05 11:20:00'),
  (10, 4, 'Right knee: landmark-guided injection of triamcinolone 40 mg with lidocaine. Tolerated well. Ice and rest 24 hours; return in 12 weeks or sooner if swelling.',
       '2026-10-12 10:35:00', '2026-10-12 10:40:00'),
  (14, 1, 'Follow-up after missed visit. Morning stiffness down to 20 minutes on methotrexate 15 mg weekly. Labs reviewed and normal. Continue current plan.',
       '2026-10-21 10:40:00', '2026-10-21 11:00:00'),
  (15, 2, 'New patient with photosensitive rash, oral ulcers and polyarthralgia. ANA 1:640 speckled, anti-dsDNA pending. Likely SLE; start hydroxychloroquine 200 mg twice daily pending results.',
       '2026-10-20 14:10:00', '2026-10-20 14:30:00'),
  (16, 1, 'Doing well on infliximab. Swollen joint count 1 (left MCP2). Continue every 8 weeks with labs before each infusion.',
       '2026-10-26 12:05:00', '2026-10-26 12:15:00'),
  (12, 2, 'Monitoring after prednisone taper. No flare symptoms. Draft: confirm ESR/CRP before signing.',
       '2026-10-14 09:40:00', NULL);

-- Waitlist: patients who want an earlier slot. Windows start from today so the
-- entries are always open whenever the sample data is loaded.
INSERT INTO waitlist_entry (patient_id, service_type_id, preferred_location_id, preferred_provider_id,
                            earliest_date, latest_date, notes, created_at)
SELECT v.patient_id, v.service_type_id, v.location_id, v.provider_id,
       GREATEST(DATE '2026-11-13', clinic_now()::date),
       GREATEST(DATE '2026-11-13', clinic_now()::date) + v.days,
       v.notes, v.created_at
FROM (VALUES
  (5,  2, 2, 1,    45, 'Would take any earlier follow-up with Dr. Gonzalez',  TIMESTAMP '2026-11-08 09:12:00'),
  (8,  4, 3, NULL, 60, 'Left knee flaring; next injection not until January', TIMESTAMP '2026-11-10 15:40:00'),
  (10, 2, 1, NULL, 30, 'Rebook after the clinic canceled on Nov 25',          TIMESTAMP '2026-11-11 08:05:00')
) AS v(patient_id, service_type_id, location_id, provider_id, days, notes, created_at);

-- Loading sample data is not activity to audit
TRUNCATE TABLE audit_log RESTART IDENTITY;
