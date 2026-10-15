# CLAUDE.md — DotCourier

## Project Overview

DotCourier is a **.NET library** providing **CQRS + Mediator** with built-in pipeline
behaviors. It is the .NET sibling of Spring Courier (Java, `../spring-courier`) and was
started after MediatR moved to a commercial license. Distributed via **NuGet**, MIT licensed.

- **Package prefix:** `DotCourier` (the bare `Courier` ID on NuGet belongs to someone else)
- **Targets:** `net8.0` and `net10.0`
- **Version:** 0.4.0 (in `Directory.Build.props`; CI overrides it from the release tag)

## Design principles

1. **MediatR-compatible surface.** `IRequest<T>`, `IRequestHandler<,>`, `INotification`,
   `IPipelineBehavior<,>`, `IRequestPreProcessor<>`, `IRequestPostProcessor<,>`,
   `IRequestExceptionHandler<,,>`, `IMediator`/`ISender`/`IPublisher` and the
   `RegisterServicesFromAssembly*`/`AddBehavior`/`AddOpenBehavior` names match MediatR so
   migration is mostly a `using` swap. Do not rename these.
2. **Spring Courier semantics where they add value.** `Response<T>`, `Dispatch` (never throws,
   hides non-`CourierException` messages), notification strategies, `BehaviorOrder` positions
   and behavior placement rules all mirror the Java library.
3. **Async + CancellationToken everywhere.** No sync-over-async.
4. **Core has no third-party dependencies** beyond `Microsoft.Extensions.*.Abstractions`.
   Integrations (Polly, EF Core, Redis, Kafka) go in separate packages.

## Repository Structure

```
dotcourier/
├── Directory.Build.props        # shared props: LangVersion, Nullable, warnings-as-errors, package metadata
├── Directory.Packages.props     # central package versions
├── DotCourier.slnx
├── src/
│   ├── Directory.Build.props    # multi-targeting, XML docs, symbols, README packing
│   ├── DotCourier/              # core package
│   │   ├── Requests.cs Handlers.cs Pipeline.cs ICourier.cs   # public contracts
│   │   ├── Courier.cs           # dispatcher (Send / Dispatch / Publish)
│   │   ├── Response.cs          # Response<T>, ResponseError, IResponseStatus
│   │   ├── Exceptions.cs        # CourierException hierarchy + ValidationError
│   │   ├── BehaviorOrder.cs     # pipeline positions (mirror of Spring Courier orders)
│   │   ├── Behaviors/           # Logging, Telemetry, Validation, Timeout, processors, exception handlers
│   │   ├── Validation/          # IRequestValidator<T>, DataAnnotations evaluator
│   │   ├── DependencyInjection/ # AddCourier + CourierConfiguration
│   │   └── Internal/            # per-request-type cached wrappers (no reflection on the hot path)
│   ├── DotCourier.AspNetCore/   # Response<T>.ToResult() / ToActionResult()
│   ├── DotCourier.Resilience/   # retry / circuit breaker / rate limiter / bulkhead on Polly v8
│   ├── DotCourier.FluentValidation/ # IValidator<T> in the validation step + exception translation
│   └── DotCourier.Caching/      # [Cacheable]/[Idempotent]/[InvalidatesCache] on HybridCache
├── tests/DotCourier.Tests/      # xUnit, runs on net8.0 and net10.0 (Resilience/ subfolder per module)
├── assets/                      # logo + icon (SVG sources, PNGs rendered from them), before-after.svg
├── .github/                     # CI, NuGet publish, dependabot, issue/PR templates
└── README.md / README.pt-BR.md, CONTRIBUTING*, SECURITY*, CODE_OF_CONDUCT*  (EN + pt-BR pairs)
```

### Modules

| Package | Purpose |
|---|---|
| `DotCourier` | Dispatcher, ordered pipeline, notifications, `Response<T>`, logging/telemetry/validation/timeout behaviors |
| `DotCourier.AspNetCore` | `Response<T>` → `IResult` / `IActionResult` |
| `DotCourier.FluentValidation` | `cfg.AddFluentValidation()`: registers an open `IRequestValidator<>` adapter over `IValidator<T>` (merged into the core validation 400) and scans validators; an innermost (`int.MaxValue`) behavior turns FluentValidation's exception into DotCourier's `ValidationException` so retry never repeats an invalid request (pinned by a test). |
| `DotCourier.Caching` | `cfg.AddCaching(o => ...)`: attributes live in **core** (no dependency) so contracts can be annotated. Behaviors: idempotency (-995), cache invalidation (-951, outside transactions → after commit), caching (-950). Storage is `HybridCache` (pinned to 9.x so net8 apps stay on Microsoft.Extensions 9.0); any registered `IDistributedCache` becomes L2 (but HybridCache ignores `MemoryDistributedCache`). Keys hash the request serialized as JSON (record `ToString()` collides on collections; pinned by a test). Non-successful/null results are carried out of the factory in an internal `SkipCacheException` so they are never stored; stampede protection gives in-process dedupe for concurrent idempotent duplicates. |
| `DotCourier.Resilience` | `cfg.AddResilience(o => ...)`: Polly pipelines cached per (request type, strategy) in a singleton; rejections thrown as `CourierException` subclasses (`CircuitOpenException` 503, `RateLimitExceededException` 429, `BulkheadRejectedException` 503). Only the breaker is on by default. Breaker outside retry and bulkhead inside retry are pinned by tests. |

## Key mechanics

- **Pipeline order:** behaviors are registered sorted by `order` (stable), because MS DI
  returns `IEnumerable<IPipelineBehavior<,>>` in registration order and the first is outermost.
  Behaviors registered directly in `IServiceCollection` (outside `AddCourier`) bypass sorting.
- **Extension modules** plug in through `CourierConfiguration`: `AddOpenBehavior(type, BehaviorOrder.X)`
  for behaviors and `ConfigureServices(...)` for their own services, so everything lands in the
  single sorted registration. A module's `cfg.AddX()` extension lives in namespace `DotCourier`.
- **Dispatch path:** `Courier` caches one `RequestHandlerWrapperImpl<TRequest,TResponse>` per
  request type in a static `ConcurrentDictionary`; the handler is resolved eagerly so a missing
  one fails before any behavior runs.
- **Error mapping (`Response<T>.FromException`):** `ValidationException` → 400 + errors as
  `ErrorDetails`; other `CourierException` → its message and `StatusCode`; anything else →
  `"An internal error occurred"` / 500. Keep this leak-proof.
- **Validation:** DataAnnotations are read from properties *and* matching constructor
  parameters, so positional records work.
- **Generics:** scanning keeps open generic types. If the interface's type arguments are exactly
  the class's type parameters (`X<T> : INotificationHandler<T>`), it is registered as an open
  generic for the container (enumerable services only). Open `IRequestHandler<,>` implementations
  (`CreateHandler<T> : IRequestHandler<Create<T>, T>`) go to `GenericRequestHandlerRegistry`, which
  unifies the handler's interface with the closed service on the first miss, checks constraints via
  `MakeGenericType`, caches an `ActivatorUtilities` factory, and throws if two open handlers fit.
- **Own handler interfaces:** apps keep their own `IHandler<TIn, TOut>.Execute` by making the
  interface inherit `IRequestHandler<,>` with a default interface method forwarding `Handle` to
  `Execute`; scanning sees the inherited closed interface. Pinned by `OwnHandlerInterfaceTests`
  and documented in both READMEs. (Spring Courier does this by method-name convention instead.)
- **Names:** always use `CourierDiagnostics.GetRequestName(type)` for request types in logs,
  metrics and messages (`Create<Product>`, not ``Create`1``).
- **Error codes:** `Response<T>.ErrorCode` (JSON `errorCode`, omitted when null) comes from the
  helpers' `code:`, `CourierException.Code` or `ResponseError.Code`; `ValidationError.Code` per field.

## Build & Test

```bash
dotnet build
dotnet test
dotnet pack -c Release -o artifacts
```

`TreatWarningsAsErrors` is on and `src/` generates XML docs, so every public member needs a
`<summary>`.

Assets: the PNGs in `assets/` are rendered from the SVGs with headless Edge/Chrome
(`--headless=new --screenshot`, `--default-background-color=00000000` for transparency). Edit the
SVG, then re-render; `icon.png` is the NuGet package icon. READMEs reference images by absolute
`raw.githubusercontent.com` URLs so they also render on nuget.org.

Docs come in EN + pt-BR pairs; update both.

## Release

1. Create a GitHub Release tagged `vX.Y.Z`.
2. `publish-nuget.yml` tests, packs with `-p:Version=X.Y.Z` and pushes to NuGet
   via NuGet Trusted Publishing (OIDC, no stored API key). Needs the repo variable
   `NUGET_USER` and a trusted publishing policy on nuget.org for `publish-nuget.yml`.

## Roadmap (Spring Courier module → DotCourier package)

| Spring Courier | DotCourier |
|---|---|
| `spring-courier-resilience` + core `RetryBehavior` | `DotCourier.Resilience` (Polly v8) ✅ |
| `spring-courier-cache-redis` + core cache/idempotency | `DotCourier.Caching` (`HybridCache`/`IDistributedCache`) ✅ |
| `TransactionBehavior` | `DotCourier.EntityFrameworkCore` |
| `spring-courier-outbox` | `DotCourier.Outbox.EntityFrameworkCore` |
| `spring-courier-messaging` | `DotCourier.Kafka` |
