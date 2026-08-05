# Change management

Security-sensitive changes require traceability from intent through implementation, verification, release, deployment, and rollback.

## Change classes

| Class | Examples | Required review |
|---|---|---|
| Standard | Documentation corrections, non-behavioral maintenance | Normal pull request and CI |
| Normal | Product behavior, public API, provider code, dependencies, workflow controls | Risk assessment, CODEOWNERS review, targeted tests, full gate |
| High risk | Authentication, authorization, tenancy, tokens, secrets, migrations, release signing | Security review, rollback plan, full evidence |
| Emergency | Active incident containment or critical vulnerability | Minimum safe approval, immediate validation, retrospective review |

## Required record

Use `docs/templates/CHANGE-RECORD.md` for normal, high-risk, and emergency changes. Record:

- objective and scope;
- affected packages and providers;
- security, privacy, persistence, and compatibility impact;
- verification commands and evidence;
- migration and rollback procedure;
- approvals and risk acceptance;
- deployment result and follow-up.

## Standard Git procedure

Review the dirty tree, run targeted checks, stage and review the exact commit, commit, run the complete clean-tree `verify-local` gate, confirm a clean status, and push. Verification corrections belong in the unpublished commit through amend or autosquash.

## Emergency changes

Emergency changes may reduce process latency but must not remove security controls without an explicit, expiring risk decision. Complete missing review, tests, documentation, and postmortem work immediately after containment.

## Risk exceptions

Use `docs/templates/RISK-ACCEPTANCE.md`. Every exception must have:

- an owner;
- a specific control and reason;
- affected scope;
- compensating controls;
- expiry no later than `RiskExceptionMaximumDays` unless reapproved;
- remediation plan;
- approval and review evidence.

An expired exception is not approval to continue operating.
