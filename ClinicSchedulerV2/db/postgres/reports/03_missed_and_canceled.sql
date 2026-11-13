-- title: Missed and canceled rates by service
-- question: Which services are missed or canceled most?
SELECT st.name AS service,
       COUNT(*)                                        AS total_past,
       COUNT(*) FILTER (WHERE a.status = 'Completed')  AS completed,
       COUNT(*) FILTER (WHERE a.status = 'Missed')     AS missed,
       COUNT(*) FILTER (WHERE a.status = 'Canceled')   AS canceled,
       ROUND(100.0 * COUNT(*) FILTER (WHERE a.status IN ('Missed', 'Canceled')) / COUNT(*), 1) AS pct_not_completed
FROM appointment a
JOIN service_type st ON st.service_type_id = a.service_type_id
WHERE a.start_time < @today::date
GROUP BY st.service_type_id, st.name
ORDER BY pct_not_completed DESC, total_past DESC;
