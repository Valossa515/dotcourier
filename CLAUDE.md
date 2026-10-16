# CLAUDE.md — DotCourier

## Project Overview

DotCourier is a **.NET library** providing **CQRS + Mediator** with built-in pipeline
behaviors. It is the .NET sibling of Spring Courier (Java, `../spring-courier`) and was
started after MediatR moved to a commercial license. Distributed via **NuGet**, MIT licensed.

- **Package prefix:** `DotCourier` (the bare `Courier` ID on NuGet belongs to someone else)
- **Targets:** `net8.0` and `net10.0`
- **Version:** 0.12.0 (in `Directory.Build.props`; CI overrides it from the release tag)

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
│   ├── DotCourier.Caching/      # [Cacheable]/[Idempotent]/[InvalidatesCache] on HybridCache
│   ├── DotCourier.Caching.Redis/ # RedisIdempotencyLock: cross-instance lock for [Idempotent] (StackExchange.Redis 2.7.27)
│   ├── DotCourier.EntityFrameworkCore/ # transaction per command + ICourierTransaction (after commit)
│   ├── DotCourier.Outbox.EntityFrameworkCore/ # IOutbox + background processor (at-least-once delivery)
│   ├── DotCourier.Kafka/        # IOutboxDispatcher that publishes the outbox to Kafka (Confluent.Kafka)
│   └── DotCourier.SourceGenerator/ # Roslyn generator (netstandard2.0, shipped as an analyzer): compile-time registrations
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
| `DotCourier.Caching` | `cfg.AddCaching(o => ...)`: attributes live in **core** (no dependency) so contracts can be annotated. Behaviors: idempotency (-995), cache invalidation (-951, outside transactions → after commit), caching (-950). Storage is `HybridCache` (pinned to 9.x so net8 apps stay on Microsoft.Extensions 9.0); any registered `IDistributedCache` becomes L2 (but HybridCache ignores `MemoryDistributedCache`). Keys hash the request serialized as JSON (record `ToString()` collides on collections; pinned by a test). Non-successful/null results are carried out of the factory in an internal `SkipCacheException` so they are never stored; stampede protection gives in-process dedupe for concurrent idempotent duplicates. HybridCache writes L2 **in the background** after returning the value (and only logs L2 failures), so idempotency runs `GetOrCreateAsync` with `DisableDistributedCacheWrite` and then an **awaited** `SetAsync` (token `None`): the response leaves only after Redis has the record (pinned by a test with a slow fake Redis). `[Cacheable]` keeps the background write. |
| `DotCourier.EntityFrameworkCore` | `cfg.AddEntityFrameworkTransactions<TDbContext>(o => ...)`: `TransactionBehavior` (-800, inside retry so each attempt gets its own transaction) wraps `ICommand<>` + `[Transactional]` (or everything with `AllRequests`), minus `[NotTransactional]` (attributes in **core**). Joins an existing `CurrentTransaction` (nested commands). Runs through `CreateExecutionStrategy()` so `EnableRetryOnFailure` works. Rolls back on exception **and** on `IResponseStatus { IsSuccess: false }`, then `ChangeTracker.Clear()` (pinned by a retry test). Scoped `ICourierTransaction.OnCommittedAsync` queues actions while active, runs them after the strategy returns, logs (never rethrows) their failures. References EF Core **8** (Relational) so net8 apps are not forced up. Tests: SQLite in-memory with shared cache (needed for `ReadUncommitted`); test handlers resolve `ShopDb` via `IServiceProvider` because every test host scans the assembly with `ValidateOnBuild`. |
| `DotCourier.Outbox.EntityFrameworkCore` | `cfg.AddOutbox<TDbContext>(o => ...)` + `modelBuilder.AddCourierOutbox()` (the user's migrations create the table; no DDL files). `IOutbox.Add` tracks an `OutboxMessage` saved by the user's `SaveChangesAsync`; `PublishAsync` also saves. `OutboxProcessor<T>` (`BackgroundService`, also a singleton so tests call `ProcessOnceAsync`): reclaims `Processing` rows older than `ProcessingTimeout`, takes `BatchSize` pending oldest first, claims each by saving `Status` (the **concurrency token**; deliberately not `ExecuteUpdate`, whose signature changed in EF 10 and would break binary compatibility with an EF 8-built package), dispatches in its own scope, marks `Processed` or `Attempts++`/`Failed` with the exception chain in `LastError`. Default `IOutboxDispatcher` publishes with the core's `SequentialThenThrow` (the default `Sequential` swallows handler failures, so nothing would ever be retried; pinned by a test). `OutboxSignal` wakes the processor on commit (`ICourierTransaction.OnCommittedAsync`) or, outside a command, on `SavedChanges`. Type names are assembly-qualified **without version** and must implement `INotification` before deserializing (DB content is untrusted). Dates are UTC `DateTime` (SQLite cannot compare `DateTimeOffset`). |
| `DotCourier.Kafka` | `cfg.AddKafkaDelivery(o => o.BootstrapServers = ...)`: `RemoveAll<IOutboxDispatcher>` + singleton `KafkaOutboxDispatcher` (redirects, never duplicates; order-independent). Forwards the stored JSON as is; key = message id; headers `courier-message-id` / `courier-event-type` (same as Spring Courier). **Awaits** `ProduceAsync` with a linked `SendTimeout` CTS and requires `PersistenceStatus.Persisted`; failures become `OutboxPublishException` so the outbox retries. Producer: `acks=all`, idempotence, `MessageTimeoutMs = SendTimeout`, then `ConfigureProducer`; created lazily by `CourierKafkaProducer` (flushed/disposed with the container only if used; internal ctor takes a factory, which is how tests inject `FakeProducer`). Topic = prefix + type name resolved via `Type.GetType` (generic args appended with `-`), text fallback when the type is not loadable, sanitized to Kafka's charset. The real-broker test is `[DockerFact]` (Testcontainers, `confluentinc/cp-kafka`) and only runs with `DOTCOURIER_DOCKER_TESTS=true`, which CI sets. |
| `DotCourier.Resilience` | `cfg.AddResilience(o => ...)`: Polly pipelines cached per (request type, strategy) in a singleton; rejections thrown as `CourierException` subclasses (`CircuitOpenException` 503, `RateLimitExceededException` 429, `BulkheadRejectedException` 503). Only the breaker is on by default. Breaker outside retry and bulkhead inside retry are pinned by tests. |

## Key mechanics

- **AOT for Caching / Outbox / Kafka (0.12.0):** JSON goes through each package's internal `Json` helper:
  options with a `TypeInfoResolver` → `options.GetTypeInfo(type)`; options without one, while reflection is
  enabled → the old reflection overloads (suppressed: never reached under AOT). `GetTypeInfo` on options
  without a resolver *throws*, which is why the helper exists. Caching: `ResultSerializerOptions` registers
  `CourierHybridSerializerFactory` (types the options don't know fall through to HybridCache's default) and
  is also the key options' fallback; `DefaultKeyOptions = new()` keeps keys identical to the old
  `JsonSerializerOptions.Default`. HybridCache pinned 9.10.0 on net8.0 and **10.11.0 on net10.0** (CPM
  conditional `PackageVersion`); both versions' default serializer warns under trimming, so the smoke app keeps
  IL2026/IL2070/IL2104/IL3050 as warnings (our own code is policed by the build analyzers). "Is it a query/command"
  uses `typeof(IQuery<TResponse>).IsAssignableFrom(typeof(TRequest))` instead of `GetInterfaces()`. Outbox and
  Kafka resolve stored type names through `GeneratedCourierRegistry.NotificationTypes` (filled by generated
  `Notification<T>()`), falling back to `Type.GetType`. Outbox, EF and Kafka enable the trim/AOT analyzers but
  not `IsAotCompatible` (EF Core and Confluent.Kafka aren't verified under AOT).

- **Idempotency lock (0.12.0):** with an `IIdempotencyLock` registered, `IdempotencyBehavior.RunLocked` checks
  for a recorded result (`CachedExecution.TryGetAsync`: `GetOrCreateAsync` with read-only flags and a factory
  that throws `NotRecordedException`, since `default(T)` can be a legit recorded value), then polls
  `TryAcquireAsync(key + ":lock")` with backoff, checking for the record between attempts; after
  `IdempotencyLockWait` → `IdempotencyInProgressException` (409, `Messages.IdempotencyInProgress`). Holding the
  lock it calls the unlocked `Run`, which already reads memory and the shared level before running, so no
  re-check is needed there (a mutation pinned that). `RedisIdempotencyLock`: `SET NX PX` + compare-and-delete
  Lua script. Tests use a `SharedLock` fake across two `TestHost` "instances" sharing a `FakeRedis`; the real
  Redis test is a `[DockerFact]` (Testcontainers).

- **Native AOT / compile-time mode (0.11.0):** `cfg.RequireCompileTimeRegistrations` (default
  `!RuntimeFeature.IsDynamicCodeSupported`; `PublishAot=true` makes that false even for `dotnet run`) →
  `CourierOptions.RequireCompileTimeRegistrations`. In that mode:
  - behaviors are **not** registered as open generics: `ClosedBehaviorRegistration` closes every configured
    behavior for every `IKnownRequest` (added by generated `RequestHandler`/`StreamHandler` calls), in the
    same order, asking each `BehaviorTypes` (GVM `CloseRequestBehavior<TReq,TResp>(Type open)` returning
    `typeof(B<TReq,TResp>)`): `CoreBehaviorTypes`, `ResilienceBehaviorTypes` (via `cfg.AddBehaviorTypes`),
    and the generator's `GeneratedBehaviorTypes` for open behaviors in the user assembly (only if their
    type parameters have no constraint beyond `notnull`);
  - exception handlers go through `IExceptionHandlerInvoker<TReq,TResp>` registered by the generated
    `context.ExceptionHandler<...>` (no MakeGenericType / MethodInfo.Invoke);
  - missing wrappers → `HandlerNotFoundException` (streams fail on first read, notifications no-op); the
    generator emits `context.Notification<T>()` for every notification type so open generic notification
    handlers still run; `GenericRequestHandlerRegistry` is empty; assemblies without generated
    registrations throw at startup.
  `IsAotCompatible` is on for DotCourier, DotCourier.AspNetCore (its `ToResult` writes JSON through
  `JsonOptions.GetTypeInfo`, never `Results.Json`) and DotCourier.Resilience; every reflection path left is
  guarded by the mode and suppressed with that justification. Request types carry
  `[DynamicallyAccessedMembers(CompileTimeTypes.Request)]` (public properties + constructors) through
  `BehaviorTypes`, `KnownRequest`, the generated context and `ValidationBehavior`, for DataAnnotations.
  `UnitJsonConverter` is public so STJ source generation can serialize `Response<Unit>`.
  `tests/DotCourier.AotSmoke` (forces compile-time mode) is published with `PublishAot` and run by the CI job
  `native-aot` on Ubuntu; locally it runs with `dotnet run` (publishing needs the VS C++ workload on Windows).
  `CompileTimeModeTests` runs the same scenarios in both modes.

- **Source generator (0.10.0):** `DotCourier.SourceGenerator` emits, per assembly, a module initializer
  that calls `GeneratedCourierRegistry.Register(assembly, register)`; `AddCourier` looks it up with
  `GeneratedCourierRegistry.Find`, which **forces the module constructor** (`typeof(T).Assembly` does not
  run any code of that assembly; pinned by a test that loads a never-executed assembly). The calls are
  generic over concrete types (`context.RequestHandler<TReq, TResp, THandler>()` etc.), which register the
  descriptor and create the dispatch wrapper with `new`; open generics go through `context.OpenGeneric`
  (same rules as the scan); any handler or type argument not accessible from generated code makes the
  whole assembly fall back to `context.ScanAssembly()` (DOTCOURIER001). Registrations are emitted in
  **metadata order** (`MetadataOrder`: namespaces popped from a stack, so children in reverse; nested
  types last, breadth first), which is what `Assembly.DefinedTypes` returns: notification handler order
  and first-handler-wins depend on it. `GeneratedRegistrationTests` asserts generated == scanned, entry
  by entry and in order; the test project references the generator, so the whole suite runs generated.
  Use a type's own `TypeParameters.Length`, never `IsGenericType` (true for types nested in a generic
  type; that crashed the generator once, pinned by a test). Generator tests (`DotCourier.SourceGenerator.Tests`)
  drive it with `CSharpGeneratorDriver` and check the output compiles.

- **Streams (0.9.0):** `IStreamRequest<T>` / `IStreamRequestHandler<,>` / `IStreamPipelineBehavior<,>` /
  `StreamHandlerDelegate<T>()` / `ISender.CreateStream`, MediatR's names. `StreamHandlerWrapperImpl` is an
  async iterator, so even the handler lookup is lazy (as in MediatR); the `[EnumeratorCancellation]` token
  merges `CreateStream`'s and `WithCancellation`'s. Stream handlers share the scanning and the generic
  registry with request handlers. Built-ins: `StreamLoggingBehavior`, `StreamTelemetryBehavior`,
  `StreamValidationBehavior` (same orders and switches as the request ones), built on
  `StreamObserver.Observe`, which reports completed / stopped-by-caller / failed with the item count and
  **re-sets `Activity.Current` around each `MoveNextAsync`**: after a `yield` the iterator resumes in the
  caller's context, and without that the handler's spans would hang off the caller's span (pinned by a
  test). Request-only modules (resilience, caching, transactions) do not apply to streams.

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
- **Library messages:** every fixed message DotCourier writes (unexpected 500, validation summary,
  handler not found, timeout, resilience rejections) comes from `CourierMessages` (`cfg.Messages`,
  copied into the `CourierOptions` singleton). A `CourierMessage` is fixed text or
  `CourierMessage.From(() => ...)` evaluated at throw time (request culture), with an optional code
  and `{request}`/`{count}`/`{timeoutMs}` placeholders. Throw sites take `CourierMessages?` (null →
  `CourierMessages.Default`, the original English texts). Never hardcode a new user-facing message:
  add it to `CourierMessages`.
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
| `TransactionBehavior` | `DotCourier.EntityFrameworkCore` ✅ |
| `spring-courier-outbox` | `DotCourier.Outbox.EntityFrameworkCore` ✅ |
| `spring-courier-messaging` | `DotCourier.Kafka` ✅ |
