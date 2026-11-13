-- title: Provider workload, previous month
-- question: Who carried the most clinical hours last month?
SELECT e.first_name || ' ' || e.last_name AS provider,
       pr.credential,
       COUNT(*)                     AS completed_visits,
       COUNT(DISTINCT a.patient_id) AS distinct_patients,
       ROUND((SUM(EXTRACT(EPOCH FROM (a.end_time - a.start_time))) / 3600)::numeric, 1) AS hours_booked
FROM appointment a
JOIN provider pr ON pr.employee_id = a.provider_id
JOIN employee e  ON e.employee_id  = pr.employee_id
WHERE a.status = 'Completed'
  AND a.start_time >= date_trunc('month', @today::date) - INTERVAL '1 month'
  AND a.start_time <  date_trunc('month', @today::date)
GROUP BY pr.employee_id, e.first_name, e.last_name, pr.credential
ORDER BY hours_booked DESC, provider;
