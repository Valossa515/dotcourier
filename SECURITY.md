> 🌐 **Language / Idioma:** 🇺🇸 **English** (current) | 🇧🇷 [Português](SECURITY.pt-BR.md)

# 🔒 Security Policy

## Supported Versions

DotCourier is pre-1.0: only the latest minor release receives fixes.

| Version | Supported |
| ------- | --------- |
| 0.7.x   | ✅ Yes    |
| < 0.7   | ❌ No     |

## Reporting a Vulnerability

**⚠️ Do not open a public issue for security vulnerabilities.**

Report privately instead, using either:

1. **[GitHub Security Advisories](https://github.com/Valossa515/dotcourier/security/advisories/new)** (preferred)
2. Email to **fe.mmo515@gmail.com**

### What to include

- Description of the vulnerability and its potential impact
- Steps to reproduce, ideally a minimal project
- Affected package(s) and version(s)
- Suggested fix, if you have one

### What to expect

- **Acknowledgment** within 48 hours
- **Initial assessment** within 7 days
- **Fix and release** according to severity:
  - 🔴 **Critical**: as soon as possible
  - 🟠 **High**: next patch release
  - 🟡 **Medium**: next minor release
  - 🟢 **Low**: a future release

### Responsible disclosure

Please give us reasonable time to ship a fix before disclosing publicly, don't exploit the issue
beyond what is needed to demonstrate it, and don't access or modify other people's data.
Reporters of valid issues are credited (with permission) in the release notes.

## Security best practices when using DotCourier

- Keep the packages up to date.
- Prefer `ICourier.Dispatch` at HTTP boundaries: only `CourierException` messages reach the
  response, so stack traces, SQL and connection strings never leak.
- Only throw `CourierException` subclasses with messages you are comfortable showing to callers.
- Validate requests with DataAnnotations or `IRequestValidator<T>`; the validation behavior runs
  before your handler.
- The logging behavior records request **type names**, never request contents. Keep it that way
  in your own behaviors: requests often carry personal data or secrets.
- Enable `DotCourier.Resilience` rate limiting for operations that are expensive or abusable.
