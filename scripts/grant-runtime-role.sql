-- Run with psql as the migration owner AFTER migrations, not as the application.
-- Create the LOGIN role and set its password separately (psql \password).
-- Example: psql -v runtime_role=trade_app -v migration_role=trade_owner -f scripts/grant-runtime-role.sql
\set ON_ERROR_STOP on
\if :{?runtime_role}
\else
  \echo 'Required psql variable: runtime_role'
  \quit 2
\endif
\if :{?migration_role}
\else
  \echo 'Required psql variable: migration_role'
  \quit 2
\endif

SELECT format('GRANT CONNECT ON DATABASE %I TO %I', current_database(), :'runtime_role') \gexec
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO :"runtime_role";
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO :"runtime_role";
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO :"runtime_role";
ALTER DEFAULT PRIVILEGES FOR ROLE :"migration_role" IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"runtime_role";
ALTER DEFAULT PRIVILEGES FOR ROLE :"migration_role" IN SCHEMA public
    GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO :"runtime_role";

-- The API can append/read audit records but cannot mutate history or run migrations.
REVOKE UPDATE, DELETE ON audit_logs FROM :"runtime_role";
REVOKE ALL ON "__EFMigrationsHistory" FROM :"runtime_role";
GRANT SELECT ON "__EFMigrationsHistory" TO :"runtime_role";
