# -*- coding: utf-8 -*-
"""Shared plumbing for the KUT issued-document pipeline.

Two jobs, deliberately in one stdlib-only module so that the WRITE side (the
generators, which need python-docx and openpyxl) and the READ side (the gate,
which must run on a bare CI runner) cannot drift apart:

  1. Determinism + staleness stamps for the generated .docx / .xlsx.
  2. Reading text and tables back out of those files with `zipfile` alone.

WHY THE READ SIDE IS STDLIB-ONLY
`tools/check_kut_documents.py` is the gate. It has to run on a runner with
nothing installed, the same way `tools/check_smoke_test.py` does -- that checker
is the pattern this module follows, and the reasoning in its `smoke_test_lib`
counterpart applies here unchanged. A .docx and a .xlsx are both OPC zips of
XML, so reading them needs no library; only WRITING them does.

WHY THE STAMPS EXIST
The three issued documents are generated. Nothing stopped someone opening one in
Word, fixing a typo and shipping a document that no longer round-trips to its
generator -- which is precisely the two-hand-copies failure the KUT workstream
exists to remove, one level down. So each document carries two digests in its
core properties:

  inputs-sha256:   over the GENERATORS that produced it. Answers "was this built
                   from the current source?" A generator edit invalidates it.
  parts-sha256:    over the document's own OPC parts, excluding the one holding
                   the stamps. Answers "has anyone touched the file since?" A
                   hand-edit in Word invalidates it.

Neither implies the other, which is why both are carried. Provenance without
content integrity would miss the Word edit; content integrity without provenance
would miss a generator change that was never re-run.

WHY DETERMINISM HAD TO BE ADDED
The documents were NOT byte-deterministic before this module, despite being
described as such. Measured on 2026-08-23 against the committed pack:

  * both .docx  -- content byte-identical across runs, but all 18 zip entries
    carried the wall clock, so every regeneration produced a different binary;
  * the .xlsx   -- the same, PLUS openpyxl stamping dcterms:created/modified
    from the wall clock into docProps/core.xml, so even the part bytes differed.

An always-dirty binary diff is worse than no diff: it trains reviewers to ignore
`git status` on exactly the files a hand-edit would show up in. Pinning the epoch
and the core dates makes regeneration a genuine no-op, so a real content change
is the only thing that ever appears.
"""
from __future__ import annotations

import hashlib
import os
import re
import shutil
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

# -- stamping ---------------------------------------------------------------

INPUTS_PREFIX = "inputs-sha256:"
PARTS_PREFIX = "parts-sha256:"

# The part carrying the stamps, and therefore excluded from parts-sha256.
STAMP_PART = "docProps/core.xml"

# Fixed epoch for zip entry timestamps and core dates. Any constant would do;
# this one is obviously synthetic so nobody reads it as a real authoring date.
EPOCH = (2020, 1, 1, 0, 0, 0)

_INPUTS_RX = re.compile(re.escape(INPUTS_PREFIX) + r"([0-9a-f]{64})")
_PARTS_RX = re.compile(re.escape(PARTS_PREFIX) + r"([0-9a-f]{64})")

# The generated pack. Keyed by the committed filename; the value is every source
# file whose content determines that document's bytes.
GENERATED = {
    "KUT_BIM_Execution_Plan.docx": ("tools/build_bep.py", "tools/corporate_docx.py",
                                    "tools/kut_docs_lib.py"),
    "KUT_Project_Delivery_Playbook.docx": ("tools/build_team_playbook.py",
                                           "tools/corporate_docx.py",
                                           "tools/kut_docs_lib.py"),
    "KUT_Master_Information_Delivery_Plan.xlsx": ("tools/build_midp.py",
                                                  "tools/midp_schema.py",
                                                  "tools/kut_docs_lib.py"),
}

# Read by the client and by every consultant. The internal playbook is excluded
# by name on purpose -- it is ours, and it is allowed to name the tooling.
ISSUED = tuple(GENERATED)
INTERNAL_DOC = "KUT_BIM_MANAGER_PLAYBOOK_INTERNAL_STINGTOOLS.docx"


def inputs_digest(root: Path, name: str) -> str:
    """SHA-256 over the generators that produce `name`.

    Bytes are LF-normalised so a Windows checkout with core.autocrlf=true agrees
    with a Linux runner -- the same defence smoke_test_lib applies, and for the
    same reason: tools/*.py is not pinned in .gitattributes.
    """
    h = hashlib.sha256()
    for rel in GENERATED[name]:
        p = root / rel
        h.update(rel.encode("utf-8") + b"\0")
        h.update(p.read_bytes().replace(b"\r\n", b"\n") + b"\0")
    return h.hexdigest()


def parts_digest(path: Path):
    """SHA-256 over every OPC part except the one holding the stamps.

    Sorted by name, so a zip rewritten in a different entry order still hashes
    the same: it is the content that matters, not the packaging. None means the
    file is missing or not a zip, which the caller treats as "regenerate".
    """
    try:
        with zipfile.ZipFile(path) as z:
            names = sorted(n for n in z.namelist() if n != STAMP_PART)
            h = hashlib.sha256()
            for n in names:
                h.update(n.encode("utf-8") + b"\0")
                h.update(z.read(n) + b"\0")
    except (OSError, zipfile.BadZipFile):
        return None
    return h.hexdigest()


def _stamp(path: Path, rx):
    try:
        with zipfile.ZipFile(path) as z:
            xml = z.read(STAMP_PART).decode("utf-8", "replace")
    except (OSError, KeyError, zipfile.BadZipFile):
        return None
    m = rx.search(xml)
    return m.group(1) if m else None


def read_inputs_stamp(path: Path):
    return _stamp(path, _INPUTS_RX)


def read_parts_stamp(path: Path):
    return _stamp(path, _PARTS_RX)


def finalise(path: Path) -> None:
    """Make a just-saved OPC file deterministic and stamp its content digest.

    Call this LAST, after the library has written the file and after the inputs
    stamp is already in the core properties. It:

      * pins every zip entry timestamp to EPOCH (entry order preserved, part
        bytes untouched);
      * computes parts-sha256 over the finished parts and injects it beside the
        inputs stamp.

    The content digest has to be computed here rather than at render time: it
    hashes the finished parts, so it cannot exist until every part does.
    docProps/core.xml carries the stamps and is excluded from the digest, so
    patching that one part afterwards leaves the digest valid.
    """
    with zipfile.ZipFile(path) as zin:
        entries = [(i, zin.read(i.filename)) for i in zin.infolist()]

    digest = parts_digest(path)
    marker = INPUTS_PREFIX.encode("utf-8")
    replacement = (PARTS_PREFIX + str(digest) + " " + INPUTS_PREFIX).encode("utf-8")
    entries = [
        (i, _pin_core(data.replace(marker, replacement, 1))
            if i.filename == STAMP_PART else data)
        for i, data in entries
    ]

    fd, tmp_name = tempfile.mkstemp(suffix=path.suffix)
    os.close(fd)                     # Windows will not rename a file still open
    tmp = Path(tmp_name)
    with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as zout:
        for info, data in entries:
            new = zipfile.ZipInfo(info.filename, date_time=EPOCH)
            new.compress_type = info.compress_type
            new.external_attr = info.external_attr
            new.create_system = 0    # pin to FAT so the host OS is not encoded
            zout.writestr(new, data)
    shutil.move(str(tmp), str(path))


# dc:description is capped at 255 characters by the OPC schema, and python-docx
# enforces it. The stamp is 78 of those and is load-bearing, so the wording
# around it is kept short and the caller trims its own prefix, never the digest.
DESCRIPTION_LIMIT = 255


_EPOCH_W3CDTF = "%04d-%02d-%02dT%02d:%02d:%02dZ" % EPOCH

_DCTERMS_RX = re.compile(
    br"(<dcterms:(?:created|modified)[^>]*>)[^<]*(</dcterms:(?:created|modified)>)")


def _pin_core(data: bytes) -> bytes:
    """Force dcterms:created / dcterms:modified in core.xml to the fixed epoch.

    Assigning the properties before save is not enough: openpyxl overwrites
    dcterms:modified with the wall clock as it writes, so the .xlsx stayed
    non-deterministic even with both dates pinned on the workbook object. Doing
    it here, on the finished bytes, makes determinism independent of which
    library wrote the file -- and core.xml is excluded from parts-sha256, so
    rewriting it cannot invalidate the content digest.
    """
    return _DCTERMS_RX.sub(lambda m: m.group(1) + _EPOCH_W3CDTF.encode("ascii") + m.group(2), data)


def provenance(generator: str, digest: str) -> str:
    """The core-properties comment carrying the inputs stamp."""
    return ("Generated by " + generator + "; do not hand-edit - regenerate. "
            + INPUTS_PREFIX + digest)


def with_provenance(existing: str, generator: str, digest: str) -> str:
    """`existing` followed by the stamp, trimmed to fit dc:description.

    If the two together overflow, the PREFIX is dropped -- it is the issue
    status, which the document itself states on its title page, whereas the
    digest is the only copy of the provenance and losing it silently disarms
    the gate.
    """
    stamp = provenance(generator, digest)
    existing = (existing or "").strip()
    joined = (existing + " " + stamp).strip()
    if len(joined) <= DESCRIPTION_LIMIT:
        return joined
    return stamp[:DESCRIPTION_LIMIT]


# -- reading .docx and .xlsx without python-docx or openpyxl -----------------

_W = "{http://schemas.openxmlformats.org/wordprocessingml/2006/main}"
_S = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"
_R = "{http://schemas.openxmlformats.org/officeDocument/2006/relationships}"


def docx_paragraphs(path: Path):
    """Body paragraphs of a .docx, in document order, EXCLUDING table cells.

    Runs inside a paragraph are concatenated, because a single sentence is
    routinely split across runs by formatting and a naive per-run read would
    never match a phrase that straddles a bold word.

    Paragraphs inside tables are excluded because docx_tables() returns them,
    and anything that adds the two together would count table content twice.
    That is not hypothetical: it inflated the [FILL] count of the BEP from 66
    to 131 before it was caught, which would have baselined a number twice the
    real one and then reported a fall every time a placeholder was closed.
    """
    with zipfile.ZipFile(path) as z:
        root = ET.fromstring(z.read("word/document.xml"))
    in_table = {id(p) for tbl in root.iter(_W + "tbl") for p in tbl.iter(_W + "p")}
    return ["".join(t.text or "" for t in p.iter(_W + "t"))
            for p in root.iter(_W + "p") if id(p) not in in_table]


def docx_tables(path: Path):
    """Every table as rows of cell strings."""
    with zipfile.ZipFile(path) as z:
        root = ET.fromstring(z.read("word/document.xml"))
    tables = []
    for tbl in root.iter(_W + "tbl"):
        rows = []
        for tr in tbl.iter(_W + "tr"):
            cells = []
            for tc in tr.iter(_W + "tc"):
                cells.append(" ".join(
                    "".join(t.text or "" for t in p.iter(_W + "t"))
                    for p in tc.iter(_W + "p")).strip())
            rows.append(cells)
        tables.append(rows)
    return tables


def docx_text(path: Path) -> str:
    """All body text of a .docx -- paragraphs and tables -- newline-joined.

    Headers and footers are read too: the document reference and the revision
    live in the footer, and a leak could just as easily land there.
    """
    parts = list(docx_paragraphs(path))
    for tbl in docx_tables(path):
        for row in tbl:
            parts.extend(row)
    with zipfile.ZipFile(path) as z:
        for name in z.namelist():
            if re.match(r"word/(header|footer)\d*\.xml$", name):
                root = ET.fromstring(z.read(name))
                parts.extend("".join(t.text or "" for t in p.iter(_W + "t"))
                             for p in root.iter(_W + "p"))
    return "\n".join(parts)


def xlsx_sheets(path: Path):
    """Every worksheet as rows of cell strings, keyed by sheet name.

    Values only -- formulas are returned as their cached value if one exists,
    else the formula text. Shared strings are resolved. Blank cells inside a
    used row are preserved as "" so column position stays meaningful, which
    matters: a prior defect in this pack was seven drop-downs pointing at the
    wrong column, and a reader that silently collapses blanks cannot see that.
    """
    out = {}
    with zipfile.ZipFile(path) as z:
        shared = []
        if "xl/sharedStrings.xml" in z.namelist():
            sst = ET.fromstring(z.read("xl/sharedStrings.xml"))
            for si in sst.iter(_S + "si"):
                shared.append("".join(t.text or "" for t in si.iter(_S + "t")))
        for name, part in _sheet_parts(z):
            out[name] = _sheet_rows(ET.fromstring(z.read(part)), shared)
    return out


def xlsx_text(path: Path) -> str:
    """Every cell of every sheet, newline-joined."""
    parts = []
    for rows in xlsx_sheets(path).values():
        for row in rows:
            parts.extend(c for c in row if c)
    return "\n".join(parts)


def xlsx_data_validations(path: Path):
    """(sheet, sqref, formula1) for every data validation in the workbook.

    Carried because the drop-downs are load-bearing: the permitted-value lists
    are what a returned TIDP is validated against, and a validation pointing at
    the wrong column looks entirely correct in Excel.
    """
    out = []
    with zipfile.ZipFile(path) as z:
        for name, part in _sheet_parts(z):
            ws = ET.fromstring(z.read(part))
            for dv in ws.iter(_S + "dataValidation"):
                f1 = dv.find(_S + "formula1")
                out.append((name, dv.get("sqref") or "",
                            (f1.text if f1 is not None else "") or ""))
    return out


def _sheet_parts(z: zipfile.ZipFile):
    """(sheet name, zip part name) for each worksheet, in workbook order."""
    wb = ET.fromstring(z.read("xl/workbook.xml"))
    rels = ET.fromstring(z.read("xl/_rels/workbook.xml.rels"))
    rid_to_target = {r.get("Id"): r.get("Target") for r in rels}
    names = set(z.namelist())
    for sheet in wb.iter(_S + "sheet"):
        target = (rid_to_target.get(sheet.get(_R + "id")) or "").lstrip("/")
        part = target if target.startswith("xl/") else "xl/" + target
        if part in names:
            yield sheet.get("name"), part


def _sheet_rows(ws, shared):
    """Rows padded to their real positions: rows[i] is spreadsheet row i+1.

    A worksheet omits rows that carry nothing, so a naive append collapses the
    gaps and every row index after the first blank is wrong. That matters
    wherever a position is reported back to a person: the TIDP table starts at
    row 16 under a header block, and telling somebody to fix "row 13" sends them
    to the wrong row of their own return.
    """
    rows = []
    for row in ws.iter(_S + "row"):
        try:
            n = int(row.get("r") or 0)
        except ValueError:
            n = 0
        if n:
            while len(rows) < n - 1:
                rows.append([])
        cells = []
        for c in row.iter(_S + "c"):
            idx = _col_index(c.get("r") or "")
            while len(cells) < idx:
                cells.append("")
            v = c.find(_S + "v")
            f = c.find(_S + "f")
            if c.get("t") == "inlineStr":
                text = "".join(t.text or "" for t in c.iter(_S + "t"))
            elif v is not None and c.get("t") == "s":
                try:
                    text = shared[int(v.text)]
                except (TypeError, ValueError, IndexError):
                    text = ""
            elif v is not None and (v.text or "") != "":
                text = v.text
            elif f is not None:
                # A formula cell often carries an EMPTY cached <v> alongside its
                # <f>, so testing for <v> first reported every formula in the
                # workbook as a blank cell -- the whole Summary sheet, and the
                # variance column of the register.
                text = "=" + (f.text or "")
            else:
                text = ""
            cells.append(text)
        rows.append(cells)
    return rows


def _col_index(ref: str) -> int:
    """0-based column index from a cell reference like 'AB12'."""
    n = 0
    for ch in ref:
        if not ch.isalpha():
            break
        n = n * 26 + (ord(ch.upper()) - 64)
    return n - 1 if n else 0
