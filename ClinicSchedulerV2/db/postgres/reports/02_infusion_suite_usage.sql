-- title: Infusion suite usage by location
-- question: How busy is each infusion suite?
SELECT l.name AS location,
       r.capacity AS chairs,
       COUNT(a.appointment_id) AS infusion_visits,
       ROUND(COALESCE(SUM(EXTRACT(EPOCH FROM (a.end_time - a.start_time))) / 3600, 0)::numeric, 1) AS chair_hours_used,
       COUNT(DISTINCT a.start_time::date) AS infusion_days,
       ROUND((SUM(EXTRACT(EPOCH FROM (a.end_time - a.start_time))) / 3600
              / NULLIF(COUNT(DISTINCT a.start_time::date) * r.capacity * 9, 0) * 100)::numeric, 1)
             AS pct_of_capacity_on_infusion_days
FROM room r
JOIN location l ON l.location_id = r.location_id
LEFT JOIN appointment a ON a.room_id = r.room_id AND a.status <> 'Canceled'
WHERE r.room_type = 'Infusion'
GROUP BY l.location_id, l.name, r.room_id, r.capacity
ORDER BY chair_hours_used DESC;
