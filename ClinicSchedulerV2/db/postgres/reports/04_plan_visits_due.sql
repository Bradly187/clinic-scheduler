-- title: Plan visits due in the next 4 weeks
-- question: Which plan visits are due soon with nothing booked?
SELECT patient, service, medication, interval_weeks,
       to_char(last_completed, 'YYYY-MM-DD') AS last_completed,
       to_char(next_due, 'YYYY-MM-DD')       AS next_due,
       to_char(next_booked, 'YYYY-MM-DD')    AS next_booked,
       CASE
         WHEN next_booked IS NOT NULL  THEN 'Booked'
         WHEN next_due < @today::date  THEN 'Overdue'
         ELSE 'Due soon - needs booking'
       END AS booking_status
FROM (
  SELECT p.first_name || ' ' || p.last_name AS patient,
         st.name AS service, m.generic_name AS medication, tpi.interval_weeks,
         (SELECT MAX(a.start_time) FROM appointment a
           WHERE a.plan_item_id = tpi.plan_item_id AND a.status = 'Completed') AS last_completed,
         (SELECT MAX(a.start_time) FROM appointment a
           WHERE a.plan_item_id = tpi.plan_item_id AND a.status = 'Completed')::date
           + tpi.interval_weeks * 7 AS next_due,
         (SELECT MIN(a.start_time) FROM appointment a
           WHERE a.plan_item_id = tpi.plan_item_id AND a.status = 'Scheduled'
             AND a.start_time >= @today::date) AS next_booked
  FROM treatment_plan_item tpi
  JOIN treatment_plan tp ON tp.plan_id = tpi.plan_id
  JOIN patient p         ON p.patient_id = tp.patient_id
  JOIN service_type st   ON st.service_type_id = tpi.service_type_id
  LEFT JOIN medication m ON m.medication_id = tpi.medication_id
  WHERE tp.status = 'Active'
) due
WHERE last_completed IS NOT NULL
  AND next_due <= @today::date + 28
ORDER BY (next_booked IS NOT NULL), next_due;
