-- Reset alerts for open transactions so audio can replay after the TTS fix.
UPDATE public.rfid_transaction_alerts a
SET announcement_played = FALSE
FROM public.rfid_transactions t
WHERE a.rfid_transaction_id = t.id
  AND t.status = 'OPEN'
  AND t.exit_time IS NULL;
