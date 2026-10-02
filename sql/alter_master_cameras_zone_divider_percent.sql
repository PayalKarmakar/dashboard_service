-- Per-camera door line position (20–80). Used with door_line_orientation.
ALTER TABLE public.master_cameras
    ADD COLUMN IF NOT EXISTS zone_divider_percent INT NULL;

UPDATE public.master_cameras
SET zone_divider_percent = 50
WHERE zone_divider_percent IS NULL;
