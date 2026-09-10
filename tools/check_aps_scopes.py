#!/usr/bin/env python3
"""Assert the OAuth scopes we REQUEST cover the APS calls we actually MAKE.

WHY THIS EXISTS
A scope mismatch is silent until the moment it matters. The sign-in succeeds, the
token is stored, the connection panel goes green, and then the one call that needed
the missing scope returns 403 -- typically the first time someone tries it against a
real tenant, with the client's credential, in front of people.

That is exactly what happened here. `AccOAuthFlow.DefaultScope` requested
`data:read data:write account:read`, and `AccModelUpload` POSTs to three data/v1
endpoints that create resources (`/storage`, `/items`, `/versions`). Those need
`data:create`. Every upload would have 403'd on its first call. Nothing in the build,
the tests or the type system could see it, because the scope is a string and the
requirement lives in Autodesk's documentation.

WHAT THIS PROVES
The requirement is derived FROM THE SOURCE, not from a hand-maintained list: the
checker looks for resource-creating POSTs in the ACC clients and, if it finds any,
requires `data:create` in the requested scope set. Delete the upload code and the
requirement relaxes on its own; drop the scope while the upload exists and this fails.

WHAT IT CANNOT PROVE
It cannot verify Autodesk's actual scope requirements -- that lives in APS
documentation and can change without us knowing. The endpoint -> scope mapping below
is our declared understanding, and a wrong entry here produces a confidently wrong
result. It checks that our declared mapping is applied consistently; it does not
check that the mapping is right. The only thing that proves that is a live call.
"""
import re
import sys
from pathlib import Path

ROOT = Path(sys.argv[1] if len(sys.argv) > 1 else ".")

OAUTH = ROOT / "StingTools" / "V6" / "AccOAuthFlow.cs"
CLIENT_GLOB = "StingTools/V6/Acc*.cs"

# Our declared endpoint -> scope mapping. See "WHAT IT CANNOT PROVE" above.
# A data/v1 POST that mints a new resource requires data:create; reads require
# data:read; modifying an existing resource requires data:write.
CREATING_ENDPOINTS = ("/storage", "/items", "/versions")

SCOPE_RE = re.compile(r'DefaultScope\s*=\s*"([^"]*)"')
# A POST whose URL mentions one of the creating endpoints, on one line or split
# across the argument list -- so match the call and look ahead a little.
POST_RE = re.compile(r'HttpMethod\.Post\s*,\s*\$?"([^"]*)"')


def fail(msg):
    print("FAIL: " + msg)
    sys.exit(1)


def requested_scopes():
    if not OAUTH.exists():
        fail("%s not found -- cannot read the requested scopes." % OAUTH)
    text = OAUTH.read_text(encoding="utf-8", errors="replace")
    m = SCOPE_RE.search(text)
    if not m:
        fail("DefaultScope not found in %s -- the constant was renamed or removed, so "
             "this checker can no longer see what is requested. Fix the checker rather "
             "than deleting it." % OAUTH)
    return set(m.group(1).split()), m.group(1)


def creating_calls():
    """(file, endpoint) for every POST to a resource-creating data/v1 endpoint."""
    hits = []
    for path in sorted(ROOT.glob(CLIENT_GLOB)):
        text = path.read_text(encoding="utf-8", errors="replace")
        for m in POST_RE.finditer(text):
            url = m.group(1)
            for ep in CREATING_ENDPOINTS:
                if url.rstrip("/").endswith(ep) and "DataBase" in url:
                    hits.append((path.name, url))
                    break
    return hits


def selftest():
    """Prove the extractor discriminates before trusting anything it says."""
    print("GATE SELF-TEST")
    probe_yes = 'await SendAsync(HttpMethod.Post, $"{DataBase}/projects/{p}/storage",'
    probe_no = 'await SendAsync(HttpMethod.Get, $"{DataBase}/projects/{p}/storage",'
    found_yes = bool(POST_RE.search(probe_yes))
    found_no = bool(POST_RE.search(probe_no))
    print("  a creating POST is recognised            : %s  (expect True)" % found_yes)
    print("  the same URL via GET is not              : %s  (expect False)" % found_no)
    if not found_yes or found_no:
        fail("the extractor does not discriminate -- its findings below mean nothing.")
    print("  extractor discriminates correctly")
    print()


def main():
    selftest()

    scopes, raw = requested_scopes()
    print("Requested scopes: %s" % raw)

    hits = creating_calls()
    print("Resource-creating POSTs found in %s: %d" % (CLIENT_GLOB, len(hits)))
    for fn, url in hits:
        print("   %-22s %s" % (fn, url))
    print()

    if not hits:
        print("No resource-creating call found, so data:create is not required.")
        print("OK.")
        return 0

    if "data:create" not in scopes:
        fail("%d resource-creating POST(s) exist (above) but the requested scope set is "
             "'%s' -- it does not include data:create. Every one of those calls will "
             "return 403 against a real tenant. Add data:create to "
             "AccOAuthFlow.DefaultScope.\n\n"
             "  Note: scopes are fixed at consent. Anyone who signed in before the fix "
             "holds a refresh token that cannot widen them, and must sign in again."
             % (len(hits), raw))

    print("OK: data:create is requested, and %d call(s) need it." % len(hits))
    return 0


if __name__ == "__main__":
    sys.exit(main())
