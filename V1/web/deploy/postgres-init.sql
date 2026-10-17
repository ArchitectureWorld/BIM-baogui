DO $$
BEGIN
    EXECUTE format('CREATE ROLE hifc_web LOGIN PASSWORD %L', rtrim(pg_read_file('/run/secrets/app_db_password'), E'\r\n'));
END $$;
ALTER DATABASE hifc_web OWNER TO hifc_web;
