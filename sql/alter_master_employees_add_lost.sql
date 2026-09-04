-- Add lost-employee tracking to public.master_employees.
-- Business rule: when is_lost is true, is_active must be false.

ALTER TABLE public.master_employees
    ADD COLUMN IF NOT EXISTS is_lost boolean NOT NULL DEFAULT false;

ALTER TABLE public.master_employees
    ADD COLUMN IF NOT EXISTS lost_created_by bigint;

ALTER TABLE public.master_employees
    ADD COLUMN IF NOT EXISTS lost_updated_at timestamp without time zone;

-- Ensure existing lost rows (if any) are not active before adding the CHECK.
UPDATE public.master_employees
SET is_active = FALSE
WHERE is_lost = TRUE
  AND is_active = TRUE;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'master_employees_lost_implies_inactive_check'
          AND conrelid = 'public.master_employees'::regclass
    ) THEN
        ALTER TABLE public.master_employees
            ADD CONSTRAINT master_employees_lost_implies_inactive_check
            CHECK ((is_lost = FALSE) OR (is_active = FALSE));
    END IF;
END $$;
