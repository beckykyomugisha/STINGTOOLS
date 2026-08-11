# KNP26 — operator corrections sheet

Every setting that is currently wrong or unset in the live model, what to change it to, and
where. Measured 2026-08-11 against the deployed build.

**Read the first section before the others.** Two of these must happen in a specific order or
the rest silently fail.

---

## 1 · The ordering rule that governs everything

Three parameters are written with **`SetIfEmpty`** — they take the first value they are given
and refuse every later one:

| Parameter | Written at | Consequence of tagging too early |
|---|---|---|
| `ASS_SEQ_NUM_TXT` | `TagConfig.cs:2514` | SEQ sticks at whatever it first got; re-tagging will not fix it |
| `ASS_ROOM_NAME_TXT` | `ParameterHelpers.cs:2803` | an element tagged while its room is "Room 1" keeps "Room 1" forever |
| `ASS_ROOM_NUM_TXT` | `ParameterHelpers.cs:2804` | same |

So the order is not a style preference:

```
1. Draw rooms
2. Name and Number them          <- Revit's native fields, NOT a STING parameter
3. RoomAudit                     <- catch unnamed / unbounded before they propagate
4. RoomZoneAssign                <- writes the ZONE token from the room
5. RoomParamPush                 <- room name/number onto every element in the room
6. Tag & Combine                 <- everything else
7. Place STING - Room Tag
```

If steps 2–5 have already been skipped, `Tags_RepairPolluted` clears the stuck values so a
re-tag can write them. It reports counts before changing anything.

---

## 2 · Tags still render ten lines

The tier gates are **type parameters on the tag family**, not on the model element. Code that
writes them to the element is a no-op — `TAG_PARA_STATE_2_BOOL` has zero binding rows in both
`CATEGORY_BINDINGS.csv` and `RESOLVED_BINDINGS.csv`, and `MR_PARAMETERS.csv` declares it
`Generic Models, Type`.

`TagStudio_SetTierDefaults` reported **`Gate values written: 0`** — it found all 16 types,
correctly declined to invent `TAG_PARA_STATE_1/2/3` and `TAG_WARN_VISIBLE_BOOL` (absent from
the type, they live instance-side), and wrote nothing for 4–10 either. That is a defect in the
command, registered separately.

**Until it is fixed, set them by hand — once per type, not once per tag:**

1. Select any placed tag → **Edit Type**
2. Untick `TAG_PARA_STATE_3_BOOL` … `TAG_PARA_STATE_10_BOOL`
3. Tick `TAG_WARN_VISIBLE_BOOL`
4. OK

Every tag of that type updates immediately. **Do not reload the family to do this** — reloading
with *"Overwrite the existing version and its parameter values"* discards everything set in the
project.

Expect two lines afterwards: the ISO code, and Status.

---

## 3 · Room schedules show codes and no names

22 Rooms-category schedules ship across four packs. **Only 5 carry a name field.** The other 17
list `ASS_ID_TXT`, `ASS_LOC_TXT`, `ASS_TAG_1_TXT`, `PRJ_COMMENTS_TXT` — codes with nothing a
human can read.

They are also thin and duplicated: Environmental 3 fields, Accessibility 4, Acoustic 5, and
"Accessibility Schedule" exists three times over with 4, 4 and 6 fields.

**Every room schedule should open with the same four columns**, so a reader can orient
themselves before the discipline-specific data starts:

| Column | Parameter | Why |
|---|---|---|
| Number | `ASS_ROOM_NUM_TXT` | `COT01-01` — the key |
| Name | `ASS_ROOM_NAME_TXT` | `Executive Room` — what a human reads |
| Building | `ASS_LOC_TXT` | which of the seven cottages |
| ISO tag | `ASS_TAG_1_TXT` | the full 8-segment code |

Then the schedule's own subject. That is a data-file change to `MR_SCHEDULES.csv`, not a Revit
change — registered.

---

## 4 · Where you type names, and where you do not

**Nothing writes the `ASS_*` name fields directly.** They are all mirrors of Revit's own
parameters. Typing into them by hand works until the next Tag & Combine overwrites them.

| Type this in Revit | STING mirrors it to | Mapped at |
|---|---|---|
| Type Properties → **Description** | `ASS_DESCRIPTION_TXT` | `ParameterHelpers.cs:2737`, type fallback `:3914` |
| Type Properties → **Model** | `ASS_MODEL_NR_TXT` | `:3918` |
| Type Properties → **Manufacturer** | `ASS_MANUFACTURER_TXT` | `:3921` |
| Room → **Name** | `ASS_ROOM_NAME_TXT` on elements in the room | `:2803` |
| Room → **Number** | `ASS_ROOM_NUM_TXT` | `:2804` |

Description is **type-level**, so it is written once per type. Twelve door types, not
ninety-six doors.

---

## 5 · Rooms cannot be tagged with the universal tag

`STING_Tag_Universal` is a **Multi-Category** tag — confirmed because Revit offers it against
both Air Terminal Tags and Area Based Load Tags. Revit forbids Multi-Category tags on **Rooms,
Spaces and Areas**.

Use **`STING - Room Tag.rfa`**. It is the only family that can tag a room, and it already
exists in the content library.

---

## 6 · Buttons that do nothing

Measured across every command handler and every panel XAML: **117 buttons carry a `Tag` with
no matching case**, so clicking them does nothing. Two are in the room group:

| Button | Where |
|---|---|
| **Room Tag Apply** (`Tagging_RoomTagApply`) | TAGGING tab |
| **Bedroom** (`Bedroom`) | placement |

And two you have been looking at on the CREATE TAGS tab:

| Button | Where |
|---|---|
| **Apply** on the *Scope* row (`CreateTags_ScopeApply`) | CREATE TAGS |
| **Apply** on the *Overwrite existing tags* row (`CreateTags_OverwriteApply`) | CREATE TAGS |

The count is now a ratcheted CI gate, so it can fall but not rise.

---

## 7 · Project Information

| Field | Set to | Note |
|---|---|---|
| Project Number | `KNP26` | drives the folder tree; falls back to `PRJ` if unset |
| Organization Name | `ACE` | the architects, whose title block we issue on — leave it |
| `PRJ_PROJECT_COD_TXT` | `KNP26` | feeds `{project}` in sheet numbers |
| `PRJ_ORG_ORIGINATOR_CODE_TXT` | `ACE` | ACE is the author, per the appointment |
| Project Name | Kibale… | **`KIBALE`** spelling, not `KIBAALE` |

---

## 8 · Values written before 2026-08-10 22:34 are suspect

Sixteen parameters were storing values in Revit's internal units against a name declaring
metric — `HVC_AIRFLOW_LPS` holding ft³/s, `HVC_DCT_WIDTH_MM` holding feet, `ELC_CKT_PWR_KW`
holding VA. The writer is fixed; **the values it already wrote are not**, and they do not
self-heal.

`Tags_RepairPolluted` clears them so a re-run rewrites them correctly. Run it **report-only**
first — the recovered-versus-cleared split is the number that decides whether it is safe on a
project with issued drawings.
