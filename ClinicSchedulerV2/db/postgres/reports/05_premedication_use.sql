-- title: Premedication use by biologic
-- question: How often does each biologic need premedication?
SELECT bio.generic_name AS biologic,
       COUNT(*) AS infusions_given,
       COUNT(*) FILTER (WHERE EXISTS (
                   SELECT 1 FROM medication_administration x
                   JOIN medication xm ON xm.medication_id = x.medication_id
                   WHERE x.appointment_id = ma.appointment_id
                     AND xm.drug_class = 'Premedication'
                     AND x.reason = 'Scheduled')) AS with_scheduled_premeds,
       COUNT(*) FILTER (WHERE EXISTS (
                   SELECT 1 FROM medication_administration x
                   JOIN medication xm ON xm.medication_id = x.medication_id
                   WHERE x.appointment_id = ma.appointment_id
                     AND xm.generic_name = 'ondansetron'
                     AND x.reason = 'As-needed')) AS needed_ondansetron,
       ROUND(100.0 * COUNT(*) FILTER (WHERE EXISTS (
                   SELECT 1 FROM medication_administration x
                   WHERE x.appointment_id = ma.appointment_id
                     AND x.reason = 'As-needed')) / COUNT(*), 1) AS pct_needing_asneeded_med
FROM medication_administration ma
JOIN medication bio ON bio.medication_id = ma.medication_id
WHERE bio.drug_class = 'Biologic'
GROUP BY bio.medication_id, bio.generic_name
ORDER BY infusions_given DESC, biologic;
