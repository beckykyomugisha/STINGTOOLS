# to_send — the shareable pack, assembled

Everything in this folder may be sent to the Lead Appointed Party. It is **derived**,
**git-ignored**, and rebuilt rather than edited.

| | send as | why |
|---|---|---|
| `KUT_Mobilisation_Information_Request` | **PDF** | addressed to the Lead Appointed Party; asks for the items only they can supply |
| `KUT_Project_Delivery_Playbook` | PDF | how the team works, stage by stage |
| `KUT_BIM_Execution_Plan` | PDF | the contractual instrument |
| `KUT_Document_Control_Standard` | PDF | numbering, revision, issue, retirement |
| `KUT_Master_Information_Delivery_Plan` | **.xlsx, not PDF** | a working register — the thirteen delivery-plan tabs are meant to be filled in and returned. Flattened to PDF it stops being that |

The `.docx` are here too, for anyone who needs to edit rather than read. Their contents
pages are Word fields and are **empty until Word computes them** — Ctrl+A then F9, or print
preview. The PDFs already have theirs built.

## What is NOT here, deliberately

`KUT_BIM_MANAGER_PLAYBOOK_INTERNAL_STINGTOOLS.docx`. It names the software throughout and
is the one document that must never leave. The leakage check exempts it by name for that
reason, so nothing else would stop it being attached by mistake.

## Why this folder is git-ignored

A PDF is a render of a `.docx` that is itself generated. Committing it would put a third
copy of the same content in the repository, and the moment a generator changed, the PDF
beside it would be stale while looking current — the exact failure the document gate exists
to prevent. So the folder is assembled on demand and never committed.

## Rebuilding it

```bash
python tools/build_bep.py
python tools/build_team_playbook.py
python tools/build_document_control.py
python tools/build_midp.py
python tools/build_symbion_request.py
python tools/check_kut_documents.py        # must pass before anything is sent
```

Then copy the five from `KUT_DOCS_WORKING/issued/` and re-export the four PDFs from Word.

**Two things about the PDF export, both learned the hard way.** Word must be told
`AutomationSecurity = 3` or it stalls indefinitely on a trust prompt it cannot display while
hidden — a responsive, silent process consuming no CPU. And it completes **one document per
invocation**: a loop over four hangs after the first. Update fields before exporting
(`Fields.Update()` and each `TablesOfContents.Update()`) or the contents page renders as the
"update field" prompt instead of a table.
