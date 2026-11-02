#!/usr/bin/env python3
"""Generate fresh staging-only secrets without printing them or replacing a file."""
import argparse
import ipaddress
import os
import re
import secrets
import uuid
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--domain", required=True, help="Staging DNS hostname, without scheme/port")
    parser.add_argument("--release-id", required=True, help="Immutable source/release identifier")
    parser.add_argument("--admin-username", default="staging-admin")
    parser.add_argument("--proxy-subnet", default="172.30.251.0/24")
    parser.add_argument("--output", type=Path, default=Path(".env.staging"))
    args = parser.parse_args()
    domain = args.domain.lower().rstrip(".")
    labels = domain.split(".")
    if len(labels) < 2 or len(domain) > 253 or any(
        not re.fullmatch(r"[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?", label) for label in labels
    ):
        parser.error("Use a DNS hostname, without scheme, path, wildcard or port.")
    try:
        ipaddress.ip_address(domain)
    except ValueError:
        pass
    else:
        parser.error("Use a staging DNS hostname, not an IP address.")
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,63}", args.release_id):
        parser.error("Use a release identifier of 1–64 letters/digits/dots/underscores/hyphens.")
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,63}", args.admin_username):
        parser.error("Use a simple, explicit staging admin username.")
    try:
        network = ipaddress.ip_network(args.proxy_subnet)
    except ValueError:
        parser.error("Invalid proxy subnet.")
    private_ranges = [ipaddress.ip_network(value) for value in ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"]]
    if network.version != 4 or network.prefixlen != 24 or not any(network.subnet_of(private) for private in private_ranges):
        parser.error("Choose a free private IPv4 /24 proxy subnet.")
    owner_password, app_password = secrets.token_urlsafe(32), secrets.token_urlsafe(32)
    database, owner, app = "trade_staging", "trade_staging_owner", "trade_staging_app"
    values = {
        "POSTGRES_DB": database, "POSTGRES_USER": owner, "POSTGRES_PASSWORD": owner_password,
        "POSTGRES_RUNTIME_USER": app, "POSTGRES_RUNTIME_PASSWORD": app_password,
        "ConnectionStrings__TradeDatabase": f"Host=postgres;Database={database};Username={app};Password={app_password};Timeout=5;Command Timeout=30",
        "MIGRATION_CONNECTION_STRING": f"Host=postgres;Database={database};Username={owner};Password={owner_password};Timeout=5;Command Timeout=30",
        "ASPNETCORE_ENVIRONMENT": "Production", "CADDY_SITE_ADDRESS": domain, "ALLOWED_HOSTS": domain,
        "BIND_ADDRESS": "0.0.0.0", "HTTP_PORT": "80", "HTTPS_PORT": "443",
        "TRADE_PROXY_SUBNET": str(network),
        "TRADE_API_IMAGE": f"trade-system-api:staging-{args.release_id}",
        "TRADE_WEB_IMAGE": f"trade-system-web:staging-{args.release_id}", "VITE_API_BASE_URL": "/api",
        "BootstrapAdmin__Username": args.admin_username, "BootstrapAdmin__OrganizationId": str(uuid.uuid4()),
        "BootstrapAdmin__Password": "", "BootstrapAdmin__FullName": "Staging Administrator",
    }
    text = "# Private staging-only configuration. Do not commit or print.\n" + "\n".join(
        f"{key}='{value}'" for key, value in values.items()
    ) + "\n"
    try:
        fd = os.open(args.output, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    except FileExistsError:
        parser.error("Refusing to replace an existing env file or rotate credentials implicitly.")
    with os.fdopen(fd, "w", encoding="utf-8") as file:
        file.write(text)
    print(f"Created private staging configuration: {args.output}. Secrets were not printed.")


if __name__ == "__main__":
    main()
