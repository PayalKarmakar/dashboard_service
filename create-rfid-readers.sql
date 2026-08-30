CREATE TABLE IF NOT EXISTS public.master_rfid_readers (
    reader_id bigserial PRIMARY KEY,
    reader_name character varying(100) NOT NULL,
    reader_serialno character varying(100) NOT NULL,
    ip_address character varying(50) NOT NULL UNIQUE,
    port integer NOT NULL UNIQUE,
    reader_purpose character varying(30) NOT NULL
        CHECK (
            reader_purpose IN (
                'ENTRY',
                'EXIT',
                'EMPLOYEE_REGISTRATION',
                'ENTRY_EXIT'
            )
        ),
    is_active boolean NOT NULL DEFAULT TRUE,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone,
    last_updated_by bigint
);

CREATE TABLE IF NOT EXISTS public.rfid_reader_configuration_log (
    id bigserial PRIMARY KEY,
    reader_id bigint NOT NULL,
    reader_name character varying(100),
    reader_serialno character varying(255) NOT NULL,
    old_ip_address character varying(50),
    new_ip_address character varying(50),
    old_port integer,
    new_port integer,
    old_reader_purpose character varying(30),
    new_reader_purpose character varying(30),
    action_type character varying(20) NOT NULL
        CHECK (
            action_type IN (
                'CREATED',
                'UPDATED',
                'ACTIVATED',
                'DEACTIVATED'
            )
        ),
    changed_at timestamp without time zone DEFAULT now(),
    changed_by bigint,
    CONSTRAINT fk_reader_config_log_reader
        FOREIGN KEY (reader_id)
        REFERENCES public.master_rfid_readers(reader_id)
);
