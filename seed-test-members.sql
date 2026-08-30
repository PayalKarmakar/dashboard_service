INSERT INTO public.master_employees
(employee_code, employee_name, card_uid, department, designation, mobile, chamber_id, created_by, is_active)
VALUES
('EMP003', 'Rahul Das',     'A11B22C3', 'Operations', 'Technician',  '9000000003', 1, 1, TRUE),
('EMP004', 'Amit Roy',      'B22C33D4', 'Safety',     'Supervisor',  '9000000004', 1, 1, TRUE),
('EMP005', 'Suman Paul',    'C33D44E5', 'Production', 'Operator',    '9000000005', 1, 1, TRUE),
('EMP006', 'Priya Sen',     'D44E55F6', 'Quality',    'Inspector',   '9000000006', 1, 1, TRUE),
('EMP007', 'Ankit Ghosh',   'E55F6677', 'Maintenance','Engineer',    '9000000007', 1, 1, TRUE),
('EMP008', 'Neha Banerjee', 'F6677889', 'Operations', 'Helper',      '9000000008', 1, 1, TRUE),
('EMP009', 'Ravi Mondal',   '11223344', 'Security',   'Guard',       '9000000009', 1, 1, TRUE),
('EMP010', 'Kavita Dutta',  '55667788', 'Admin',      'Coordinator', '9000000010', 1, 1, TRUE)
ON CONFLICT (employee_code) DO NOTHING;

INSERT INTO public.rfid_transactions
(employee_id, chamber_id, employee_name, card_uid, entry_time, entry_reader_ip, entry_reader_port, status, alert_triggered)
SELECT e.emp_id, 1, e.employee_name, e.card_uid, NOW() - INTERVAL '20 minutes', '192.168.0.210', 5000, 'OPEN', FALSE
FROM public.master_employees e
WHERE e.employee_code = 'EMP003'
AND NOT EXISTS (
  SELECT 1 FROM public.rfid_transactions t
  WHERE t.employee_id = e.emp_id AND t.status = 'OPEN' AND t.exit_time IS NULL
);

INSERT INTO public.rfid_transactions
(employee_id, chamber_id, employee_name, card_uid, entry_time, entry_reader_ip, entry_reader_port, status, alert_triggered)
SELECT e.emp_id, 1, e.employee_name, e.card_uid, NOW() - INTERVAL '35 minutes', '192.168.0.210', 5000, 'OPEN', FALSE
FROM public.master_employees e
WHERE e.employee_code = 'EMP004'
AND NOT EXISTS (
  SELECT 1 FROM public.rfid_transactions t
  WHERE t.employee_id = e.emp_id AND t.status = 'OPEN' AND t.exit_time IS NULL
);

INSERT INTO public.rfid_transactions
(employee_id, chamber_id, employee_name, card_uid, entry_time, entry_reader_ip, entry_reader_port, status, alert_triggered)
SELECT e.emp_id, 1, e.employee_name, e.card_uid, NOW() - INTERVAL '52 minutes', '192.168.0.210', 5000, 'OPEN', FALSE
FROM public.master_employees e
WHERE e.employee_code = 'EMP005'
AND NOT EXISTS (
  SELECT 1 FROM public.rfid_transactions t
  WHERE t.employee_id = e.emp_id AND t.status = 'OPEN' AND t.exit_time IS NULL
);

INSERT INTO public.rfid_transactions
(employee_id, chamber_id, employee_name, card_uid, entry_time, entry_reader_ip, entry_reader_port, status, alert_triggered)
SELECT e.emp_id, 1, e.employee_name, e.card_uid, NOW() - INTERVAL '70 minutes', '192.168.0.210', 5000, 'OPEN', FALSE
FROM public.master_employees e
WHERE e.employee_code = 'EMP006'
AND NOT EXISTS (
  SELECT 1 FROM public.rfid_transactions t
  WHERE t.employee_id = e.emp_id AND t.status = 'OPEN' AND t.exit_time IS NULL
);

INSERT INTO public.rfid_transactions
(employee_id, chamber_id, employee_name, card_uid, entry_time, entry_reader_ip, entry_reader_port, status, alert_triggered)
SELECT e.emp_id, 1, e.employee_name, e.card_uid, NOW() - INTERVAL '15 minutes', '192.168.0.210', 5000, 'OPEN', FALSE
FROM public.master_employees e
WHERE e.employee_code = 'EMP007'
AND NOT EXISTS (
  SELECT 1 FROM public.rfid_transactions t
  WHERE t.employee_id = e.emp_id AND t.status = 'OPEN' AND t.exit_time IS NULL
);

INSERT INTO public.rfid_transactions
(employee_id, chamber_id, employee_name, card_uid, entry_time, entry_reader_ip, entry_reader_port, status, alert_triggered)
SELECT e.emp_id, 1, e.employee_name, e.card_uid, NOW() - INTERVAL '40 minutes', '192.168.0.210', 5000, 'OPEN', FALSE
FROM public.master_employees e
WHERE e.employee_code = 'EMP008'
AND NOT EXISTS (
  SELECT 1 FROM public.rfid_transactions t
  WHERE t.employee_id = e.emp_id AND t.status = 'OPEN' AND t.exit_time IS NULL
);

INSERT INTO public.rfid_transactions
(employee_id, chamber_id, employee_name, card_uid, entry_time, entry_reader_ip, entry_reader_port, status, alert_triggered)
SELECT e.emp_id, 1, e.employee_name, e.card_uid, NOW() - INTERVAL '58 minutes', '192.168.0.210', 5000, 'OPEN', FALSE
FROM public.master_employees e
WHERE e.employee_code = 'EMP009'
AND NOT EXISTS (
  SELECT 1 FROM public.rfid_transactions t
  WHERE t.employee_id = e.emp_id AND t.status = 'OPEN' AND t.exit_time IS NULL
);

INSERT INTO public.rfid_transactions
(employee_id, chamber_id, employee_name, card_uid, entry_time, entry_reader_ip, entry_reader_port, status, alert_triggered)
SELECT e.emp_id, 1, e.employee_name, e.card_uid, NOW() - INTERVAL '85 minutes', '192.168.0.210', 5000, 'OPEN', FALSE
FROM public.master_employees e
WHERE e.employee_code = 'EMP010'
AND NOT EXISTS (
  SELECT 1 FROM public.rfid_transactions t
  WHERE t.employee_id = e.emp_id AND t.status = 'OPEN' AND t.exit_time IS NULL
);
