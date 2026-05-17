#!/usr/bin/env python3
"""Quick helper: login to Planscape Server and list projects with their IDs.

Usage:
    # With environment variables (recommended):
    STING_PLANSCAPE_SERVER=http://localhost:5000 \
    STING_PLANSCAPE_EMAIL=you@example.com \
    STING_PLANSCAPE_PASSWORD=yourpassword \
    python get_project_id.py

    # Or pass credentials on the command line:
    python get_project_id.py --server http://localhost:5000 \
                             --email you@example.com --password yourpassword
"""

import argparse
import json
import os
import sys
import urllib.error
import urllib.request


def _parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="List Planscape projects and their IDs")
    p.add_argument("--server",   default=os.environ.get("STING_PLANSCAPE_SERVER", ""),
                   help="Server base URL (env: STING_PLANSCAPE_SERVER)")
    p.add_argument("--email",    default=os.environ.get("STING_PLANSCAPE_EMAIL", ""),
                   help="Login email (env: STING_PLANSCAPE_EMAIL)")
    p.add_argument("--password", default=os.environ.get("STING_PLANSCAPE_PASSWORD", ""),
                   help="Login password (env: STING_PLANSCAPE_PASSWORD)")
    return p.parse_args()


def _post(base: str, path: str, body: dict) -> dict:
    data = json.dumps(body).encode()
    req  = urllib.request.Request(
        f"{base}{path}", data=data,
        headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req) as r:
        return json.loads(r.read())


def _get(base: str, path: str, token: str) -> object:
    req = urllib.request.Request(
        f"{base}{path}",
        headers={"Authorization": f"Bearer {token}"})
    with urllib.request.urlopen(req) as r:
        return json.loads(r.read())


def main() -> None:
    args = _parse_args()

    if not args.server:
        print("ERROR: server URL is required. Set STING_PLANSCAPE_SERVER or pass --server.")
        sys.exit(1)
    if not args.email or not args.password:
        print("ERROR: credentials required. Set STING_PLANSCAPE_EMAIL / STING_PLANSCAPE_PASSWORD "
              "or pass --email / --password.")
        sys.exit(1)

    base = args.server.rstrip("/")

    try:
        resp  = _post(base, "/api/auth/login", {"email": args.email, "password": args.password})
        token = resp.get("token") or resp.get("accessToken")
        if not token:
            print("ERROR: no token in login response:", json.dumps(resp, indent=2))
            sys.exit(1)
        print(f"Logged in OK.")

        projects = _get(base, "/api/projects", token)
        if not projects:
            print("No projects found — seed data may not have run.")
            sys.exit(1)

        project_list = projects if isinstance(projects, list) else [projects]
        print("\nProjects:")
        for p in project_list:
            print(f"  id={p.get('id')}  name={p.get('name')}")

        first_id = project_list[0].get("id")
        print(f"\nRun these exports then start the watcher:")
        print(f"  export STING_PLANSCAPE_SERVER={base}")
        print(f"  export STING_PLANSCAPE_EMAIL=<your-email>")
        print(f"  export STING_PLANSCAPE_PASSWORD=<your-password>")
        print(f"  export STING_PLANSCAPE_PROJECT_ID={first_id}")
        print(f"  python -m StingBridge.bridge watch-ifc")

    except urllib.error.URLError as e:
        print(f"Connection error: {e}. Is the server running? docker compose up -d")
        sys.exit(1)
    except Exception as e:
        print(f"Error: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()
