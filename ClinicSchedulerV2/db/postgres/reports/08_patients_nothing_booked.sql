-- title: Patients with no future appointment
-- question: Which active patients have nothing booked?
SELECT p.first_name || ' ' || p.last_name AS patient,
       p.phone,
       l.name AS home_location,
       to_char((SELECT MAX(a.start_time) FROM appointment a
                 WHERE a.patient_id = p.patient_id AND a.status = 'Completed'),
               'YYYY-MM-DD HH24:MI') AS last_visit
FROM patient p
JOIN location l ON l.location_id = p.home_location_id
WHERE p.is_active
  AND NOT EXISTS (SELECT 1 FROM appointment a
                  WHERE a.patient_id = p.patient_id
                    AND a.status = 'Scheduled'
                    AND a.start_time >= @today::date)
ORDER BY last_visit;
