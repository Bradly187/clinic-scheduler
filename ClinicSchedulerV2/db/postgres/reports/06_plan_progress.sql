-- title: Treatment plan progress
-- question: How far along are plans with a session limit?
SELECT p.first_name || ' ' || p.last_name AS patient,
       st.name AS service,
       COALESCE(m.brand_name, '-') AS medication,
       tpi.total_sessions,
       COUNT(a.appointment_id) AS sessions_completed,
       tpi.total_sessions - COUNT(a.appointment_id) AS sessions_remaining
FROM treatment_plan_item tpi
JOIN treatment_plan tp ON tp.plan_id = tpi.plan_id
JOIN patient p         ON p.patient_id = tp.patient_id
JOIN service_type st   ON st.service_type_id = tpi.service_type_id
LEFT JOIN medication m  ON m.medication_id = tpi.medication_id
LEFT JOIN appointment a ON a.plan_item_id = tpi.plan_item_id AND a.status = 'Completed'
WHERE tpi.total_sessions IS NOT NULL
GROUP BY tpi.plan_item_id, p.first_name, p.last_name, st.name, m.brand_name, tpi.total_sessions
ORDER BY sessions_remaining, patient;
