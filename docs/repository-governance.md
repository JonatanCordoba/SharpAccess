# Repository governance

SharpAccess contains security-sensitive authentication and authorization packages. Repository settings fail closed before stable publication.

## Supported repository environment

- Windows only.
- PowerShell 7 only.
- .NET 10 only.
- No Bash, Docker, Compose, or service containers.
- Active source cohort: Core, SQLite, PostgreSQL.
- SQL Server and MySQL: roadmap-only, absent from active source and package output.

## Default branch policy

The development default branch is `master`. Configure branch protection or a repository ruleset to:

- require pull requests;
- require at least one approval;
- require CODEOWNERS review for covered paths;
- require resolved conversations;
- require the checks listed under `pullRequestRequiredChecks` in `.github/required-checks.json`;
- disallow force pushes and branch deletion;
- restrict direct pushes to emergency revert use.

Squash feature and refactor pull requests into one coherent commit. Revert bad changes with a new commit rather than rewriting protected history.

## Required local validation

Run targeted checks while a change is uncommitted. After committing, run:

```powershell
./scripts/verify-local.ps1 -RepositoryRoot $PWD
git status --short
```

The complete local gate covers structure/status, SAST, locked restore, warnings-as-errors, tests, coverage, complexity, diagnostics, SQLite recovery, endpoint smoke, packages, consumer smoke, and SBOM evidence. PostgreSQL real-engine evidence remains a separately protected release requirement.

Do not weaken warnings-as-errors, locked restore, security scans, package audit, coverage, telemetry redaction, recovery, or evidence retention to make a change pass.

## Security automation

Keep enabled:

- blocking DevSkim SAST and retained SARIF;
- Dependabot alerts, security updates, and version updates;
- dependency review;
- native GitHub secret scanning and push protection where available;
- the tracked-file secret scanning workflow;
- private vulnerability reporting and Security Advisories;
- CodeQL when the public repository or GitHub security entitlement makes it available.

Dependency updates run the same applicable Windows gates as other changes.

## Operational governance

Use:

- `CHANGE-MANAGEMENT.md` for change classes;
- `INCIDENT-RESPONSE.md` and `templates/POSTMORTEM.md` for incidents;
- `BUSINESS-CONTINUITY.md` and `templates/RECOVERY-DRILL.md` for recovery;
- `templates/RISK-ACCEPTANCE.md` for bounded exceptions;
- `PRIVACY.md` for responsibility boundaries.

Risk exceptions require an owner, compensating controls, remediation, and expiry.

## Release boundary

`JonatanCordoba/SharpAccess` is the only repository authorized to produce release evidence, packages, canonical tags, and GitHub releases.

Stable publication occurs only from the verified signed root in `JonatanCordoba/SharpAccess`, after:

- PostgreSQL is promoted through the complete gate;
- Core, SQLite, and PostgreSQL are the only stable package outputs;
- the Windows release commit passes required checks;
- public API and package contents are inspected;
- consumer smoke, operations, recovery, SBOM, checksums, provenance, and signing evidence pass;
- security and release documentation is current.

SQL Server and MySQL remain future roadmap candidates and are not release blockers.

## Settings audit

- [ ] `master` has branch protection or an equivalent ruleset.
- [ ] PRs, approval, CODEOWNERS, and conversation resolution are required.
- [ ] Required check names match `.github/required-checks.json`.
- [ ] Force pushes and deletion are disabled.
- [ ] Dependabot, SAST, secret scanning, push protection, and vulnerability reporting are enabled.
- [ ] Release evidence and publication originate from the selected SharpAccess revision.
- [ ] The public repository has trusted publication environments.
- [ ] Operational evidence retention and exercise schedules are configured.
