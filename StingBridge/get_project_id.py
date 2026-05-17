#!/usr/bin/env python3
"""Quick helper: login to Planscape Server and list projects with their IDs."""
import json, sys, urllib.request, urllib.error

BASE = "http://localhost:5000"
EMAIL = "admin@planscape.demo"
PASSWORD = "admin123"

def post(path, body):
    data = json.dumps(body).encode()
    req = urllib.request.Request(f"{BASE}{path}", data=data,
                                  headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req) as r:
        return json.loads(r.read())

def get(path, token):
    req = urllib.request.Request(f"{BASE}{path}",
                                  headers={"Authorization": f"Bearer {token}"})
    with urllib.request.urlopen(req) as r:
        return json.loads(r.read())

try:
    resp = post("/api/auth/login", {"email": EMAIL, "password": PASSWORD})
    token = resp.get("token") or resp.get("accessToken")
    if not token:
        print("ERROR: no token. Login response:", json.dumps(resp, indent=2))
        sys.exit(1)
    print(f"Logged in OK. Token: {token[:30]}...")

    projects = get("/api/projects", token)
    if not projects:
        print("No projects found — seed data may not have run.")
        sys.exit(1)

    print("\nProjects:")
    for p in (projects if isinstance(projects, list) else [projects]):
        print(f"  id={p.get('id')}  name={p.get('name')}")

    first_id = (projects if isinstance(projects, list) else [projects])[0].get("id")
    print(f"\nRun these exports then start the watcher:")
    print(f"  export STING_PLANSCAPE_EMAIL={EMAIL}")
    print(f"  export STING_PLANSCAPE_PASSWORD={PASSWORD}")
    print(f"  export STING_PLANSCAPE_PROJECT_ID={first_id}")
    print(f"  python -m StingBridge.bridge watch-ifc")

except urllib.error.URLError as e:
    print(f"Connection error: {e}. Is the server running? docker compose up -d")
    sys.exit(1)
except Exception as e:
    print(f"Error: {e}")
    sys.exit(1)
