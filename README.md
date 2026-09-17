# WALKA Amazon Intelligence

Windows-first Amazon seller and advertising intelligence for the WALKA brand. The application is designed as a local, evidence-backed, read-only intelligence system: collect permitted Seller and Advertising data, retain raw evidence, import idempotently into SQLite, calculate nullable metrics, and surface traceable operational recommendations.

## Status

`PROJECT_PLAN.md` is the authoritative scope and implementation tracker. Do not infer completion from the presence of a class, screen, migration, or connector: each tracker unit must be verified against its definition, automated tests, and relevant live acceptance gates before it is marked complete.

The repository currently contains the layered solution baseline, WPF desktop shell, domain/application contracts, SQLite persistence, infrastructure helpers, Seller/Ads connector foundations, worker orchestration, analytics code, and automated test projects. Amazon credentials and account entitlements are intentionally not stored in Git.

## Technology

- .NET 10, pinned by `global.json`
- WPF desktop application targeting Windows x64
- EF Core 10 + SQLite for local persistence
- xUnit test projects
- Windows DPAPI for protected local secrets
- GitHub Actions Windows CI for restore, Release build, and tests

## Repository layout

- `src/WalkaAmazonIntelligence.Domain` — identities, entities, metrics and domain rules
- `src/WalkaAmazonIntelligence.Application` — use-case contracts and application models
- `src/WalkaAmazonIntelligence.Persistence` — EF Core SQLite context, migrations, importing, dashboard reads and backup services
- `src/WalkaAmazonIntelligence.Infrastructure` — HTTP, evidence and secret-protection infrastructure
- `src/WalkaAmazonIntelligence.Connectors.AmazonSeller` — Seller reporting connector/parser foundation
- `src/WalkaAmazonIntelligence.Connectors.AmazonAds` — Advertising reporting connector/parser foundation
- `src/WalkaAmazonIntelligence.Connectors.Browser` — isolated browser-fallback project boundary
- `src/WalkaAmazonIntelligence.Analytics` — deterministic analytical/recommendation logic
- `src/WalkaAmazonIntelligence.Worker` — sync orchestration
- `src/WalkaAmazonIntelligence.Desktop` — WPF shell, resources and desktop composition
- `tests/*` — automated unit/integration-oriented tests
- `docs/DECISIONS.md` — durable architecture and engineering decisions
- `PROJECT_PLAN.md` — authoritative roadmap, acceptance rules and tracker

## Build and test

Use Windows with the SDK version declared in `global.json`.

```powershell
dotnet restore .\WalkaAmazonIntelligence.sln
dotnet build .\WalkaAmazonIntelligence.sln --configuration Release --no-restore
dotnet test .\WalkaAmazonIntelligence.sln --configuration Release --no-build --no-restore
```

CI runs the same restore/build/test sequence on `windows-latest` for pushes and pull requests targeting `main`.

## Development configuration

Development data and tool output belong under `.local/` and must remain untracked. Desktop configuration files contain non-secret settings only. Refresh tokens, client secrets, passwords, Authorization headers and token responses must never be committed or logged.

The production design uses owner-authorized Amazon access, capability-aware connectors, bounded retries, raw evidence retention and idempotent imports. Production must never silently substitute fixtures or sample success.

## Contribution rules

1. Fetch the latest `main` before selecting work.
2. Read `PROJECT_PLAN.md` and recover from the latest verified state instead of recreating existing work.
3. Select the highest-priority dependency-valid unfinished tracker item.
4. Implement production behavior and meaningful tests; do not mark interface-only scaffolding complete.
5. Run restore, Release build and tests and fix regressions.
6. Update `PROJECT_PLAN.md` and `docs/DECISIONS.md` when architecture or durable behavior changes.
7. Commit only a verified, coherent unit.

## Security and data safety

The project starts read-only with respect to Amazon business mutations. Any future write capability requires explicit proposal/approval/execution/authoritative re-read/verification/audit semantics. Raw evidence and local databases may contain commercially sensitive data and should be stored in user-controlled protected locations. DPAPI-protected secrets are user/machine-context dependent and are excluded from backups by design.
