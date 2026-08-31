-- Voice alert message templates (sensor + employee), bilingual en-IN / bn-IN.
-- Run against smart_monitoring database:
--   psql -h localhost -U postgres -d smart_monitoring -f create-voice-alert-messages.sql

CREATE TABLE IF NOT EXISTS public.voice_alert_messages (
    message_id       BIGSERIAL PRIMARY KEY,
    category         VARCHAR(30)  NOT NULL,
    alert_type       VARCHAR(30)  NOT NULL,
    culture          VARCHAR(10)  NOT NULL,
    message_template TEXT         NOT NULL,
    is_active        BOOLEAN      NOT NULL DEFAULT TRUE,
    updated_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_voice_alert_messages UNIQUE (category, alert_type, culture)
);

CREATE INDEX IF NOT EXISTS ix_voice_alert_messages_lookup
    ON public.voice_alert_messages (category, alert_type, culture)
    WHERE is_active = TRUE;

INSERT INTO public.voice_alert_messages (category, alert_type, culture, message_template)
VALUES
    ('SENSOR', 'WARNING', 'en-IN',
     'Warning. {Parameter} level in {ChamberName} has exceeded the permitted limit.'),
    ('SENSOR', 'WARNING', 'bn-IN',
     N'সতর্কতা। {ChamberName}-এ {Parameter}-এর মাত্রা অনুমোদিত সীমা অতিক্রম করেছে।'),
    ('SENSOR', 'CRITICAL', 'en-IN',
     'Critical alert. {Parameter} level in {ChamberName} has reached a critical level. Please take immediate action.'),
    ('SENSOR', 'CRITICAL', 'bn-IN',
     N'গুরুতর সতর্কতা। {ChamberName}-এ {Parameter}-এর মাত্রা গুরুতর পর্যায়ে পৌঁছেছে। অবিলম্বে ব্যবস্থা নিন।'),

    ('EMPLOYEE', 'ATTENTION', 'en-IN',
     'Attention {EmployeeName} ji. You have completed {AttentionMinutes} minutes inside {ChamberName}.'),
    ('EMPLOYEE', 'ATTENTION', 'bn-IN',
     N'মনোযোগ {EmployeeName}। আপনি {ChamberName}-এ {AttentionMinutes} মিনিট সময় কাটিয়েছেন।'),
    ('EMPLOYEE', 'WARNING', 'en-IN',
     'Warning {EmployeeName} ji. Only {WarningRemainingMinutes} minutes remain before your permitted duration expires in {ChamberName}.'),
    ('EMPLOYEE', 'WARNING', 'bn-IN',
     N'সতর্কতা {EmployeeName}। {ChamberName}-এ অনুমোদিত সময় শেষ হতে {WarningRemainingMinutes} মিনিট বাকি।'),
    ('EMPLOYEE', 'VIOLATION', 'en-IN',
     'Alert {EmployeeName} ji. Your permitted duration inside {ChamberName} has expired. Please exit immediately.'),
    ('EMPLOYEE', 'VIOLATION', 'bn-IN',
     N'সতর্কতা {EmployeeName}। {ChamberName}-এ আপনার অনুমোদিত সময় শেষ। দয়া করে অবিলম্বে বেরিয়ে আসুন।'),
    ('EMPLOYEE', 'VIOLATION_REPEAT', 'en-IN',
     'Warning {EmployeeName} ji. You have exceeded the permitted duration inside {ChamberName}. Please exit immediately.'),
    ('EMPLOYEE', 'VIOLATION_REPEAT', 'bn-IN',
     N'সতর্কতা {EmployeeName}। {ChamberName}-এ অনুমোদিত সময় অতিক্রম করেছেন। দয়া করে অবিলম্বে বেরিয়ে আসুন।')
ON CONFLICT (category, alert_type, culture)
DO UPDATE SET
    message_template = EXCLUDED.message_template,
    is_active = TRUE,
    updated_at = NOW();
