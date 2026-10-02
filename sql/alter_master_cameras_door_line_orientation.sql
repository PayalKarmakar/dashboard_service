-- Per-camera door line: HORIZONTAL | VERTICAL | DIAGONAL
ALTER TABLE public.master_cameras
    ADD COLUMN IF NOT EXISTS door_line_orientation VARCHAR(20) NOT NULL DEFAULT 'HORIZONTAL';
