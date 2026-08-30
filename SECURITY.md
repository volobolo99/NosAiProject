# Security Policy

## Scope

This policy covers security issues in NosAiProject source code, runtime services, network protocols, authentication, authorization, key management and release artifacts.

## Principles

- Never commit secrets, private keys, tokens or credentials.
- Treat all remote/client input as untrusted.
- Runtime is authoritative for authorization and privileged execution.
- Preserve fail-closed behavior where required.
- Do not weaken cryptographic or validation controls to make tests pass.

## Reporting

Do not publish sensitive vulnerability details in a public issue. Use the project's configured private security-reporting channel when available. Until such a channel is configured, minimize exposed details and notify the project maintainer directly.

## Verification

Security-sensitive changes require negative tests and must be reviewed against the applicable ADRs before release.
