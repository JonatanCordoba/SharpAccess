# SharpAccess support

SharpAccess `0.9.0-rc.1` is a release candidate intended for evaluation and integration testing on the supported Windows, .NET 10, and PowerShell 7 engineering platform.

## Security vulnerabilities

Do **not** open a public issue for a suspected vulnerability. Follow [`SECURITY.md`](SECURITY.md) and use GitHub private vulnerability reporting. Never include production credentials, signing keys, password peppers, raw tokens, connection strings, personal data, or active OIDC secrets.

## Bug reports

Use the bug-report issue form and include:

- the affected SharpAccess package and version;
- the selected DB provider;
- Windows and .NET SDK versions;
- the smallest safe reproduction;
- expected and actual behavior;
- sanitized logs or exception categories;
- whether authentication, authorization, token rotation, tenancy, migrations, OIDC, or recovery is involved.

Reports for unsupported platforms or provider combinations are welcome as feedback, but they are not represented as supported release surfaces.

## Feature requests

Use the feature-request issue form. Describe the user problem, the proposed public behavior, security and compatibility implications, and why the change belongs in the package rather than the host application.

## Usage questions

Before opening an issue, review the [Wiki](https://github.com/JonatanCordoba/SharpAccess/wiki), [`docs/README.md`](docs/README.md), and the [Troubleshooting Wiki page](https://github.com/JonatanCordoba/SharpAccess/wiki/Troubleshooting). When the public repository enables GitHub Discussions, use Discussions for general integration questions and issues for reproducible defects.

## Support boundaries

SharpAccess does not claim Linux, macOS, Bash, Docker, Compose, service-container, SQL Server, or MySQL support in this release candidate. Hosts remain responsible for deployment security, secret storage, email delivery, DB availability, TLS, backup policy, and production monitoring.
