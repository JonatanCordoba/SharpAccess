# Incident response

This procedure covers security and operational incidents involving SharpAccess packages, authentication flows, provider persistence, releases, or supply-chain controls.

Suspected vulnerabilities must be reported privately according to the root `SECURITY.md`. Do not place exploit details, credentials, tokens, or personal data in public issues.

## Severity

| Severity | Example | Initial response target |
|---|---|---|
| Critical | Active credential compromise, signing-key compromise, broad authorization bypass, malicious package or build compromise | Immediate escalation |
| High | Exploitable authentication or tenant-isolation defect without confirmed broad compromise | Same business day |
| Moderate | Limited security weakness with mitigations available | Within two business days |
| Low | Hardening issue with low direct impact | Normal maintenance planning |

The root `SECURITY.md` and `docs/VULNERABILITY-MANAGEMENT.md` define vulnerability acknowledgment and remediation targets.

## Response flow

1. **Detect and preserve evidence.** Record the time, affected version, environment, indicators, and reporter. Preserve relevant logs and build records without copying secrets.
2. **Classify.** Determine security, privacy, availability, integrity, and provider impact.
3. **Contain.** Revoke sessions, rotate exposed material, disable affected features, block a release, or roll back as appropriate.
4. **Eradicate.** Correct the root cause, validate dependencies and artifacts, and remove unsafe configuration.
5. **Recover.** Restore service and data through tested procedures. Increase monitoring during the observation window.
6. **Communicate.** Coordinate maintainers, hosts, users, and advisory publication according to impact.
7. **Learn.** Complete `docs/templates/POSTMORTEM.md`, track corrective actions, and update tests or controls.

## Authentication-specific containment

Potential actions include:

- rotate JWT signing keys and account for existing token lifetime;
- rotate token-hashing keys and invalidate outstanding opaque tokens;
- rotate password peppers, retain safe previous versions for verification, or force reset after compromise;
- revoke refresh-token families and increment security versions;
- disable OAuth or email flows temporarily;
- restrict administrative or tenant mutation endpoints;
- suspend package publication and verify release provenance.

## Evidence handling

Do not place raw tokens, passwords, peppers, signing keys, OAuth codes, SMTP credentials, database passwords, or production personal data in tickets or postmortems. Reference protected evidence locations instead.

## Exercises

Run a tabletop incident exercise at least every `IncidentExerciseFrequencyDays` value in `eng/OperationalReadiness.props`, and after material changes to authentication, release, or recovery procedures.
