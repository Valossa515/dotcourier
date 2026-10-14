## Description

<!-- What does this PR change, and why? -->

## Type of change

- [ ] 🐛 Bug fix (non-breaking fix)
- [ ] ✨ New feature (non-breaking addition)
- [ ] 💥 Breaking change (existing users must change code)
- [ ] 📖 Documentation only
- [ ] 🔧 Refactoring (no behavior change)
- [ ] ✅ Tests only
- [ ] 🔨 Build / CI

## Checklist

- [ ] `dotnet build` passes (warnings are errors, public APIs have XML docs)
- [ ] `dotnet test` passes on net8.0 and net10.0
- [ ] I added tests that fail without this change
- [ ] Public contracts stay MediatR-compatible (no renames of `IRequest`, `IRequestHandler`, `IPipelineBehavior`, ...)
- [ ] No exception messages other than `CourierException` reach a `Response<T>`, and no request contents are logged
- [ ] New behaviors have a `BehaviorOrder` slot, and ordering decisions are covered by a test
- [ ] Docs updated (`README.md`, `README.pt-BR.md`, `CLAUDE.md`) where relevant

## Related issue

<!-- e.g. Closes #123 -->

## How to test

1.
2.
3.

## Additional notes

<!-- Anything reviewers should know -->
