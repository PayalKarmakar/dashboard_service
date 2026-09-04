-- Camera line-crossing + unauthorized entry events (with date/time)
CREATE TABLE IF NOT EXISTS public.camera_access_events (
    event_id          BIGSERIAL PRIMARY KEY,
    camera_id         BIGINT NOT NULL,
    chamber_id        BIGINT NOT NULL,
    camera_name       VARCHAR(200) NOT NULL DEFAULT '',
    chamber_name      VARCHAR(200) NOT NULL DEFAULT '',
    event_type        VARCHAR(40) NOT NULL,
    -- ENTRY | EXIT | NO_RFID | TAILGATE | MATCHED
    person_count      INT NOT NULL DEFAULT 1,
    rfid_scan_count   INT NULL,
    message           TEXT NULL,
    occurred_at       TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_camera_access_events_occurred_at
    ON public.camera_access_events (occurred_at DESC);

CREATE INDEX IF NOT EXISTS ix_camera_access_events_type
    ON public.camera_access_events (event_type);

CREATE INDEX IF NOT EXISTS ix_camera_access_events_camera
    ON public.camera_access_events (camera_id, occurred_at DESC);
