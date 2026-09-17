# Architecture and engineering decisions

This file records durable decisions that affect implementation or acceptance. New entries are append-only unless a later decision explicitly supersedes an earlier one.

## ADR-001 — Windows-first .NET 10 WPF desktop

**Status:** Accepted — 2026-09-17

The primary product is a Windows x64 desktop application targeting `net10.0-windows` with WPF. The SDK is pinned through `global.json`. Windows is the authoritative build environment because WPF and DPAPI are platform-specific requirements.

## ADR-002 — Layered dependency boundaries

**Status:** Accepted — 2026-09-17

Domain owns entities, value semantics and calculations. Application owns use-case contracts. Persistence, Infrastructure and connector projects implement outer-layer concerns. Desktop composes services and must not call Amazon endpoints directly. Worker orchestration reuses application contracts. Analytics is isolated from UI concerns.

A repository-level architecture test protects the Application-to-Domain boundary so outer-layer dependencies cannot accidentally leak inward.

## ADR-003 — SQLite is the local system of record

**Status:** Accepted — 2026-09-17

EF Core SQLite is the default local persistence mechanism. Business keys are scoped by account/marketplace/profile and source grain. Imports must be transactional and idempotent; replacement report revisions supersede prior source facts instead of accumulating duplicate totals. Migrations are version-controlled.

## ADR-004 — Read-only Amazon posture first

**Status:** Accepted — 2026-09-17

Report creation/download and other permitted read operations are allowed. Business mutations are out of MVP scope. Any future Amazon write must use proposal, explicit approval, execution, authoritative re-read, verification and audit; acceptance of a recommendation alone never executes a remote change.

## ADR-005 — Evidence before derived intelligence

**Status:** Accepted — 2026-09-17

Raw report/evidence artifacts are retained with hashes and source metadata before transformed facts are treated as authoritative. Derived metrics must preserve unknown/null states when denominators, compatible grain, currency, attribution maturity or required inputs are unavailable. No fabricated zero or sample success is allowed in production.

## ADR-006 — Secrets remain outside Git and logs

**Status:** Accepted — 2026-09-17

LWA refresh tokens/client secrets are protected locally with Windows DPAPI CurrentUser. Configuration stores references/non-secret settings only. Access tokens remain short-lived in memory. Authorization headers, token bodies, pre-signed URLs and plaintext credentials must not be logged or committed. Browser profiles and secret-bearing local data are excluded from source control and backups.

## ADR-007 — Baseline CI is a completion gate

**Status:** Accepted — 2026-09-17

The repository baseline is not considered complete solely because the solution exists. GitHub Actions runs on Windows and performs `dotnet restore`, Release `dotnet build`, and `dotnet test` for the solution on pushes and pull requests targeting `main`. New work must keep this gate green; warnings remain errors through `Directory.Build.props`.

## ADR-008 — Derived metrics are reason-coded and scope-aware

**Status:** Accepted — 2026-09-17

Domain calculations expose nullable compatibility wrappers for existing callers and richer result objects that carry a machine-readable unavailability reason. Missing inputs, zero denominators, invalid negative inputs, incompatible account/market/currency/date windows, attribution exceeding total sales, invalid conversion rates and nonpositive inventory velocity are distinct states rather than silent zeroes. Aggregate ratios are calculated from summed numerators and denominators, not averaged row ratios. Organic-sales estimates require compatible scope and reporting windows. Stockout dates round fractional days of supply upward to the next observation date and remain unavailable when velocity is nonpositive.

## ADR-009 — Evidence source and fact type are a persistence invariant

**Status:** Accepted — 2026-09-17

The persistence boundary must reject an artifact whose declared source does not match the fact type being imported. Seller evidence can materialize Seller facts only; Advertising evidence can materialize Advertising facts only and requires an Ads profile. Artifact identity and natural keys are validated before any destructive replacement starts. Revision replacement remains inside one SQLite transaction so a failed insert, foreign-key violation, reconciliation failure or cancellation cannot commit deletion of the previously authoritative facts. Migration/model drift, foreign keys and unique natural-key constraints are verified by automated tests against a real migrated SQLite database.

## ADR-010 — Ambiguous report creation is fail-closed

**Status:** Accepted — 2026-09-17

Amazon report creation is non-idempotent. The worker persists `REPORT_REQUEST_STARTED` before the remote POST and treats network, cancellation, server-side and malformed-success outcomes as creation-uncertain until a remote report ID is durably stored. An uncertain attempt must not be automatically posted again. Recovery requires either attaching the confirmed remote report ID or explicitly confirming that no remote report exists; both actions are audited.

Once a remote report ID is persisted it remains authoritative across polling, download, parsing, import failures and cancellation, so subsequent attempts resume that report instead of creating a replacement. A fresh report may be requested after an explicit confirmed-absent reconciliation or a terminal remote report state. Audit details use bounded machine-readable codes and never exception messages, authorization data, report document URLs or token payloads.
