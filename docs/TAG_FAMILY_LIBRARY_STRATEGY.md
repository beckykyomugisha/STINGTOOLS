# Where tag families live, and how to reuse them across projects

Answering two questions asked mid-propagation on 2026-09-17: **where does a propagated
family go**, and **how do you avoid re-propagating for every project**.

Short answer: you do not re-propagate. A three-tier content library already exists and is
built for exactly this. It is simply not configured on this machine.

---

## 1 · Where a propagated family goes

Propagation writes atomically: `SaveAs(temp) → LoadFamily(overwrite) → File.Move(canonical)`.
"Canonical" is `TagFamilyCreatorCommand.GetOutputDirectory()`, which resolves:

1. the **shared library** `<content root>/Tags/`, if one is configured **and already holds
   families**;
2. otherwise the **plugin-local baseline** — the running DLL's `data/TagFamilies/`.

On this machine nothing is configured, so today it lands in:

```
C:\Dev\wt-sting-live\CompiledPlugin\data\TagFamilies\
```

⚠️ **Not in git.** Propagation never writes to `StingTools/Data/TagFamilies/`. Copy the
results back by hand if you want them committed, and back the folder up before an ALL run —
it overwrites all 206 in place.

### The "don't strand a library" guard

`GetOutputDirectory` deliberately keeps writing to the legacy folder when the legacy folder
holds families and the shared one holds none. Setting the environment variable alone
therefore changes **nothing**: reads would still find the older local copies first, the
stale families would keep winning, and the move would silently accomplish nothing.
**Migration is a deliberate act — you must seed the shared folder.**

---

## 2 · The three tiers (already built)

`ContentRoots.Resolve` searches, in precedence order:

| Tier | Location | Use |
|---|---|---|
| **project** | `<project>/_BIM_COORD/Content` (+ legacy `Families/…` siblings) | this project's frozen copy |
| **shared** | `<content root>/Tags/` — **the firm library** | one set, every project |
| **baseline** | the deployed `data/Families` + `data/TagFamilies` | what ships with the plugin |

Order comes from `ContentManifest.RootPrecedence`:

- **`projectFirst`** (default) — a project that has its own copy **ignores firm-level
  changes**. This is what you want for an issued job: the tags cannot move under you after
  issue.
- **`sharedFirst`** — the firm library wins everywhere. Use while standardising.

`Data/TagFamilies/Seeds/` is **not** a tier and must never become one again — see §4.

---

## 3 · The sustainable setup — do this once

**Step 1 — pick a shared root** on a path every machine can reach (network share, or a synced
folder). Tags go in a `Tags` sub-folder:

```
\server\BIM\STING_Content\Tags\
```

**Step 2 — point STING at it.** Either:

```
setx STING_CONTENT_LIB "\server\BIM\STING_Content"
```

or `%APPDATA%\STING\sting_content.json`:

```json
{ "content_root": "\\server\BIM\STING_Content" }
```

The env var wins. `STING_SYMBOL_LIB` / `sting_symbols.json` are honoured as legacy fallbacks.

**Step 3 — seed it.** Copy the propagated 206 from
`…\CompiledPlugin\data\TagFamilies\*.rfa` into `<root>\Tags\`. Until this folder holds
families, the guard in §1 keeps everything pointed at the local copy.

**Step 4 — from then on.** Every project resolves tag families from the shared library. No
per-project propagation, no per-project Create Tag Fams. When the master label changes you
re-propagate **once**, into the shared library, and every project picks it up on next open.

**Step 5 — freeze a project when it is issued.** Copy the families that project depends on
into `<project>/_BIM_COORD/Content`. With the default `projectFirst` precedence that copy
wins locally, so a later firm-wide change cannot alter an issued drawing.

---

## 4 · What NOT to do

**Do not recreate `Data/TagFamilies/Seeds/`.** It held 137 pre-Phase-188 families. 88
duplicated the flat set and were shadowed by it; 39 were superseded by a differently-named
file for the same category. Critically, per `ContentRoots.cs`:

> the tag families they carried reference parameters that Phase 188 retyped — **which is what
> produced the recurring "Inconsistent Units" error**

That is the same family of defect as the 12 shared-parameter conflicts. The folder is no
longer searched and the probe is removed; leaving stray copies on disk is inert, but
re-adding the root brings the failure straight back.

**Do not rely on the plugin baseline as your library.** It is overwritten by every deploy.
Anything you propagate into `…\CompiledPlugin\data\TagFamilies\` is lost the next time
someone copies a fresh build over it — which is precisely what happened to this machine on
2026-09-17.

**Do not set `sharedFirst` on issued projects.** It is the right setting while standardising
and the wrong one afterwards: it lets a firm-library edit change a drawing that has already
been issued.

---

## 5 · Why this is the flexible option

- **One master, one propagation.** The label is authored by hand once; propagation clones it;
  the shared library distributes it. A label fix is one propagation, not 206 edits and not
  one run per project.
- **Per-project override without forking.** `projectFirst` means a project can pin its own
  copy for issue, while everything else tracks the firm library — no branching of the library
  itself.
- **Checksums already exist.** `VerifyArtefactChecksum` SHA-256s each `.rfa` against the
  content manifest and warns on mismatch, so a hand-edited family in the shared library is
  detectable rather than silent.
- **Reads and writes are separated on purpose.** `GetTagLibraryRoots` (read) is deliberately
  split from `GetOutputDirectory` (write) so a machine that cannot write to the shared root
  can still read the firm library. Conflating them is what let the two libraries diverge
  unnoticed before.
