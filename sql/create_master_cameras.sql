CREATE TABLE IF NOT EXISTS public.master_cameras
(
    camera_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    chamber_id BIGINT NOT NULL REFERENCES public.master_chambers (chamber_id),
    camera_name VARCHAR(100) NOT NULL,
    camera_purpose VARCHAR(30) NOT NULL DEFAULT 'DOOR',
    ip_address VARCHAR(50),
    rtsp_url VARCHAR(500) NOT NULL,
    rfid_reader_id BIGINT REFERENCES public.master_rfid_readers (reader_id),
    person_detection_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    match_window_seconds INTEGER NOT NULL DEFAULT 10,
    alert_on_no_rfid BOOLEAN NOT NULL DEFAULT TRUE,
    alert_on_tailgate BOOLEAN NOT NULL DEFAULT TRUE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITHOUT TIME ZONE,
    last_updated_by BIGINT,
    CONSTRAINT master_cameras_camera_purpose_check
        CHECK (camera_purpose IN ('ENTRY', 'EXIT', 'DOOR', 'MONITORING')),
    CONSTRAINT master_cameras_match_window_seconds_check
        CHECK (match_window_seconds > 0 AND match_window_seconds <= 120)
);

CREATE INDEX IF NOT EXISTS ix_master_cameras_chamber_id
    ON public.master_cameras (chamber_id);

CREATE INDEX IF NOT EXISTS ix_master_cameras_rfid_reader_id
    ON public.master_cameras (rfid_reader_id);
