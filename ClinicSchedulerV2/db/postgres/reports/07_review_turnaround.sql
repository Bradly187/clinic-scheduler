-- title: Request review turnaround by staff member
-- question: How quickly does each staff member review requests?
SELECT e.first_name || ' ' || e.last_name AS reviewer,
       s.job_title,
       COUNT(*) FILTER (WHERE r.kind = 'Appointment request')  AS appointment_requests,
       COUNT(*) FILTER (WHERE r.kind = 'Patient cancellation') AS patient_cancellations,
       COUNT(*) FILTER (WHERE r.status = 'Approved') AS approved,
       COUNT(*) FILTER (WHERE r.status = 'Denied')   AS denied,
       ROUND((AVG(EXTRACT(EPOCH FROM (r.reviewed_at - r.submitted_at))) / 3600)::numeric, 1) AS avg_hours_to_review
FROM (
  SELECT 'Appointment request' AS kind, reviewed_by, status, submitted_at, reviewed_at
  FROM appointment_request WHERE reviewed_by IS NOT NULL
  UNION ALL
  SELECT 'Patient cancellation', reviewed_by, status, submitted_at, reviewed_at
  FROM cancellation_request WHERE reviewed_by IS NOT NULL AND source = 'Patient'
) r
JOIN staff s    ON s.employee_id = r.reviewed_by
JOIN employee e ON e.employee_id = s.employee_id
GROUP BY s.employee_id, e.first_name, e.last_name, s.job_title
ORDER BY avg_hours_to_review;
