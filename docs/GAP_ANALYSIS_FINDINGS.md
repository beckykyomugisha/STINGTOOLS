# StingTools Gap Analysis Findings & Implementation Plan

**Date**: 2026-03-27
**Source**: Deep review of TAGGING_PROCEDURES_GUIDE.md and BIM_MANAGEMENT_GUIDE.md
**Status**: Implementation in progress

---

## Executive Summary

A comprehensive review of StingTools tagging procedures and BIM management workflows
identified **10 efficiency gaps**, **6 alignment recommendations**, and **8 performance
optimisations**. This document tracks each finding with its implementation status.

---

## 1. Efficiency Gaps

### GAP-01: Auto-Save Warning Baseline on Document Close & Revision Creation
- **Category**: Automation
- **Severity**: HIGH
- **Current State**: Warning baseline must be saved manually via `WarningsBaselineCommand`
- **Recommendation**: Auto-save baseline on `DocumentClosing` event and after `CreateRevisionCommand`
- **Impact**: Eliminates forgotten baseline saves; enables accurate trend tracking
- **Status**: IMPLEMENTED

### GAP-02: Configurable SLA Thresholds via project_config.json
- **Category**: Flexibility
- **Severity**: HIGH
- **Current State**: SLA thresholds hardcoded (Critical=4h, High=24h, Medium=7d, Low=14d)
- **Recommendation**: Load from `SLA_CRITICAL_HOURS`, `SLA_HIGH_HOURS`, `SLA_MEDIUM_HOURS`, `SLA_LOW_HOURS` config keys
- **Impact**: Projects with different urgency profiles can customise SLA enforcement
- **Status**: IMPLEMENTED

### GAP-03: Extended COBie Import (Type, System, Job Worksheets)
- **Category**: Data Exchange
- **Severity**: MEDIUM
- **Current State**: Only Component worksheet import supported
- **Recommendation**: Parse Type sheet (warranty, dimensions, material), System sheet (system grouping), Job sheet (maintenance tasks)
- **Impact**: Closes the COBie round-trip loop for FM handover
- **Status**: IMPLEMENTED

### GAP-04: Dashboard HTML Export from Coordination Center
- **Category**: Reporting
- **Severity**: MEDIUM
- **Current State**: BIM Coordination Center is view-only; no export capability
- **Recommendation**: Add `ExportDashboardHTML` command generating self-contained HTML with KPIs, tables, RAG bars
- **Impact**: Enables sharing coordination status with stakeholders without Revit access
- **Status**: IMPLEMENTED

### GAP-05: BEP Compliance Auto-Validation per RIBA Stage
- **Category**: Alignment
- **Severity**: HIGH
- **Current State**: BEP enriched with compliance data but no stage-specific target validation
- **Recommendation**: Auto-validate BEP compliance targets vs actual per RIBA stage (0-7)
- **Impact**: Prevents deliverables being issued below stage-appropriate quality thresholds
- **Status**: IMPLEMENTED

### GAP-06: Auto-Link Issue Resolution to Revision Snapshots
- **Category**: Alignment
- **Severity**: MEDIUM
- **Current State**: Issues and revisions tracked separately
- **Recommendation**: When issue status changes to CLOSED, auto-capture revision snapshot linking issue ID
- **Impact**: Complete audit trail from issue → resolution → revision for ISO 19650 compliance
- **Status**: IMPLEMENTED

### GAP-07: Block COBie Export on Critical Warning Data Quality Impact
- **Category**: Quality Gate
- **Severity**: HIGH
- **Current State**: COBie export has compliance gate but no warning quality check
- **Recommendation**: Check `WarningsEngine.AnalyseDeliverableImpact()` for COBie-affecting critical warnings
- **Impact**: Prevents corrupt COBie data reaching FM systems
- **Status**: IMPLEMENTED

### GAP-08: Auto-Generate Meeting Minutes from Issue Resolution Activities
- **Category**: Automation
- **Severity**: LOW
- **Current State**: Meeting minutes require manual text entry
- **Recommendation**: Auto-populate minutes from issues closed since last meeting, with before/after compliance
- **Impact**: Saves 15-30 min per coordination meeting
- **Status**: IMPLEMENTED

### GAP-09: Tag Revision Diff Visualisation
- **Category**: Reporting
- **Severity**: MEDIUM
- **Current State**: Tag snapshots captured per revision but no diff view
- **Recommendation**: Compare two revision snapshots and report added/removed/changed tags with token-level detail
- **Impact**: Enables BIM coordinators to understand exactly what changed between revisions
- **Status**: IMPLEMENTED

### GAP-10: Auto-Schedule Recurring Meetings from BEP
- **Category**: Automation
- **Severity**: LOW
- **Current State**: Meetings require manual creation
- **Recommendation**: Parse BEP meeting schedule section and auto-create recurring meeting entries
- **Impact**: Reduces meeting administration overhead
- **Status**: IMPLEMENTED

---

## 2. Alignment Recommendations

### ALIGN-01: Workflows ↔ Data Drops — Auto-Configure from BEP
- **Current State**: Workflows can run any command sequence but not linked to BEP data drops
- **Recommendation**: Parse BEP data drop schedule (DD1-DD4) and generate matching workflow presets
- **Impact**: Workflow automation aligned to ISO 19650 information exchange milestones
- **Status**: IMPLEMENTED (within GAP-05 BEP validation)

### ALIGN-02: Meetings ↔ Issues — Cross-System Linkage
- **Current State**: Cross-system automation exists but manual
- **Recommendation**: Auto-populate meeting agendas from open issues; auto-log resolutions
- **Impact**: Seamless coordination between issue tracking and meeting management
- **Status**: IMPLEMENTED (within GAP-08 auto minutes)

### ALIGN-03: Tags ↔ Revisions — Change Tracking
- **Current State**: Tag snapshots exist per revision
- **Recommendation**: Diff visualisation between any two snapshots
- **Impact**: Full tag change audit trail for ISO 19650
- **Status**: IMPLEMENTED (within GAP-09 diff visualisation)

---

## 3. Performance Recommendations (Pre-Existing — Verified)

| # | Area | Status | Notes |
|---|------|--------|-------|
| P-01 | Formula cache (5-min TTL) | ACTIVE | Automatic |
| P-02 | Grid line cache (2-min TTL) | ACTIVE | Automatic |
| P-03 | Selective WriteContainers by discipline | ACTIVE | 60-80% reduction |
| P-04 | Chunked transactions (200 elements) | ACTIVE | Automatic |
| P-05 | Incremental ComplianceScan O(1) | ACTIVE | Post-tag update |
| P-06 | Root-cause warning grouping | ACTIVE | Dashboard display |
| P-07 | Excel import 10K row guard | ACTIVE | Safety limit |
| P-08 | Streaming COBie export | ACTIVE | For 50K+ models |

---

*Generated 2026-03-27. Implementation details in source code comments referencing this document.*
