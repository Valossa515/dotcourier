> 🌐 **Language / Idioma:** 🇺🇸 **English** (current) | 🇧🇷 [Português](CONTRIBUTING.pt-BR.md)

# 🤝 Contributing to DotCourier

Thanks for considering a contribution! Bug reports, docs fixes, tests and features are all
welcome. By participating you agree to the [Code of Conduct](CODE_OF_CONDUCT.md).

## 🚀 How to contribute

### 1. Fork and clone

```bash
git clone https://github.com/<your-user>/dotcourier.git
cd dotcourier
```

You need the **.NET 10 SDK** and the **.NET 8 runtime** (tests run on both target frameworks).

### 2. Create a branch

```bash
git checkout -b feat/my-feature
```

### 3. Build and test

```bash
dotnet build
dotnet test
```

The build treats **warnings as errors**, and every public member of the `src/` projects needs an
XML `<summary>`. If it builds, the docs are complete.

### 4. Commit

We use [Conventional Commits](https://www.conventionalcommits.org/):

```bash
git commit -m "feat(resilience): add per-request timeout to retry"
git commit -m "fix: keep behavior order stable for equal orders"
git commit -m "docs: clarify Dispatch error mapping"
```

### 5. Open a pull request

Describe the problem and the solution, link the issue, and fill in the PR checklist.

## 🧭 Design rules

These keep DotCourier predictable; PRs that break them will be asked to change.

1. **MediatR-compatible surface.** `IRequest<T>`, `IRequestHandler<,>`, `INotification`,
   `IPipelineBehavior<,>`, `IMediator`/`ISender`/`IPublisher` and the `AddCourier` configuration
   method names match MediatR. Don't rename them.
2. **No leaks.** `Response<T>.FromException` only propagates `CourierException` messages. Never
   put arbitrary exception messages in a response.
3. **Async + `CancellationToken` everywhere.** No sync-over-async.
4. **Core stays dependency-free** beyond `Microsoft.Extensions.*.Abstractions`. Integrations
   (Polly, EF Core, Redis, Kafka) live in their own `DotCourier.*` package.
5. **Behavior placement is explicit.** New built-in behaviors take a slot in `BehaviorOrder`, and
   any ordering decision that matters gets a test that fails if the order changes.
6. **Never log request contents.** Request type names only.

## 🗂️ Project layout

```
src/DotCourier/              core package
src/DotCourier.AspNetCore/   Response<T> → IResult / IActionResult
src/DotCourier.Resilience/   Polly-based behaviors
src/DotCourier.FluentValidation/  IValidator<T> in the pipeline
tests/DotCourier.Tests/      xUnit, runs on net8.0 and net10.0
assets/                      logo, icon and README images
```

### Adding a module

1. Create `src/DotCourier.<Name>/DotCourier.<Name>.csproj` (multi-targeting and packaging come
   from `src/Directory.Build.props`).
2. Add package versions to `Directory.Packages.props` (central package management).
3. Expose a `cfg.Add<Name>(...)` extension on `CourierConfiguration`. Register behaviors with
   `AddOpenBehavior(type, BehaviorOrder.X)` and services with `ConfigureServices(...)`, so
   everything lands in the single ordered registration done by `AddCourier`.
4. Add the project to `DotCourier.slnx` and tests under `tests/DotCourier.Tests/<Name>/`.
5. Document it in both READMEs and in `CLAUDE.md`.

## 📦 Release process (maintainers)

Releases are published to NuGet by `.github/workflows/publish-nuget.yml` using
[NuGet Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing):
GitHub's OIDC token is exchanged for a short-lived API key, so **no API key is stored** in the
repository.

One-time setup (already done for this repository):

- A trusted publishing policy on nuget.org for `Valossa515/dotcourier`, workflow
  `publish-nuget.yml`, packages `DotCourier*`.
- The repository variable `NUGET_USER` with the nuget.org profile name.

To release:

1. Update the version in `Directory.Build.props` (keeps local packs honest) and the supported
   versions table in `SECURITY.md`.
2. Merge to `main`.
3. Create a GitHub Release with tag `vX.Y.Z`. The workflow tests, packs with that version and
   pushes every package.

> Publishing to NuGet is irreversible: versions can be unlisted but never deleted.

## 🐛 Reporting bugs and requesting features

Use the issue templates. For security issues, follow the [security policy](SECURITY.md)
instead of opening a public issue.
