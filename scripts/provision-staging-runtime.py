#!/usr/bin/env python3
"""Provision the restricted DB role only inside the labelled staging Compose project."""
import argparse
import json
import re
import shlex
import subprocess
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--env-file", type=Path, required=True)
    args = parser.parse_args()
    file = args.env_file.resolve()
    if file.stat().st_mode & 0o077:
        parser.error("The private env file must have mode 600 or stricter.")
    config = {}
    for line in file.read_text().splitlines():
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        key, separator, value = line.partition("=")
        if not separator:
            parser.error("Invalid env file; values are not printed.")
        parts = shlex.split(value, comments=True)
        if len(parts) > 1:
            parser.error("Quote env values; values are not printed.")
        config[key.strip()] = parts[0] if parts else ""
    required = ["POSTGRES_DB", "POSTGRES_USER", "POSTGRES_RUNTIME_USER", "POSTGRES_RUNTIME_PASSWORD"]
    if any(not config.get(key) for key in required):
        parser.error("Required database provisioning variables are missing.")
    database, owner, role, password = (config[key] for key in required)
    if any(not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]{0,62}", name) for name in [database, owner, role]) or role == owner:
        parser.error("Use distinct owner/runtime roles with simple SQL identifiers.")
    if any(character in password for character in "\r\n\0"):
        parser.error("Invalid runtime password; values are not printed.")
    root = Path(__file__).resolve().parents[1]
    compose = ["docker", "compose", "--env-file", str(file), "-p", "trade-system-staging", "-f", str(root / "compose.production.yaml"), "-f", str(root / "compose.staging.yaml")]

    def run(command, data=None):
        result = subprocess.run(command, input=data, capture_output=True, cwd=root)
        if result.returncode:
            raise SystemExit("Staging database operation failed; command output is suppressed to protect secrets.")
        return result.stdout

    container = run(compose + ["ps", "-q", "postgres"]).decode().strip()
    if not container or "\n" in container:
        raise SystemExit("Exactly one running staging PostgreSQL container is required.")
    metadata = json.loads(run(["docker", "inspect", container]))[0]
    labels = metadata["Config"].get("Labels", {})
    if labels.get("trade.environment") != "staging" or labels.get("com.docker.compose.project") != "trade-system-staging":
        raise SystemExit("Refusing a container outside the labelled staging project.")
    environment = dict(value.split("=", 1) for value in metadata["Config"]["Env"])
    if environment.get("POSTGRES_DB") != database or environment.get("POSTGRES_USER") != owner:
        raise SystemExit("Staging database metadata does not match the private env file.")
    psql = ["docker", "exec", "-i", container, "psql", "-X", "-U", owner, "-d", database, "-At", "-v", "ON_ERROR_STOP=1"]
    literal = lambda value: "'" + value.replace("'", "''") + "'"
    # SQL travels only through stdin, never argv or an on-disk SQL file.
    create = f"SELECT format('CREATE ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L', {literal(role)}, {literal(password)}) WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname={literal(role)})\n\\gexec\n"
    run(psql, create.encode())
    unsafe = f"SELECT (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls OR EXISTS (SELECT 1 FROM pg_auth_members WHERE member=pg_roles.oid) OR EXISTS (SELECT 1 FROM pg_database WHERE datdba=pg_roles.oid) OR EXISTS (SELECT 1 FROM pg_tables WHERE tableowner={literal(role)})) FROM pg_roles WHERE rolname={literal(role)};"
    if run(psql, unsafe.encode()).decode().strip() != "f":
        raise SystemExit("Runtime role has unsafe privileges; refusing to apply grants.")
    grants = (root / "scripts/grant-runtime-role.sql").read_bytes()
    run(psql + ["-v", "runtime_role=" + role, "-v", "migration_role=" + owner], grants)
    print("Restricted staging runtime role and grants are ready. Existing passwords were not rotated.")


if __name__ == "__main__":
    main()
