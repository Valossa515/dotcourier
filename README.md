<p align="center">
  <img src="https://raw.githubusercontent.com/Valossa515/dotcourier/main/assets/dotcourier-logo.png" alt="DotCourier logo" width="600"/>
</p>

<h1 align="center">DotCourier</h1>

<p align="center">
  🚀 <strong>CQRS + Mediator for .NET, batteries included.</strong><br/>
  A MediatR-compatible API with the pipeline behaviors you would otherwise write yourself. MIT licensed.
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/DotCourier"><img src="https://img.shields.io/nuget/v/DotCourier?logo=nuget&label=nuget&color=512BD4" alt="NuGet"/></a>
  <a href="https://www.nuget.org/packages/DotCourier"><img src="https://img.shields.io/nuget/dt/DotCourier?logo=nuget&label=downloads&color=512BD4" alt="NuGet downloads"/></a>
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-8%20%7C%2010-512BD4?logo=dotnet" alt=".NET 8 | 10"/></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="License: MIT"/></a>
</p>

<p align="center">
  <a href="https://github.com/Valossa515/dotcourier/actions/workflows/ci.yml"><img src="https://github.com/Valossa515/dotcourier/actions/workflows/ci.yml/badge.svg" alt="CI"/></a>
  <a href="https://github.com/Valossa515/dotcourier/stargazers"><img src="https://img.shields.io/github/stars/Valossa515/dotcourier?style=flat&logo=github" alt="GitHub stars"/></a>
  <a href="https://github.com/Valossa515/dotcourier/commits/main"><img src="https://img.shields.io/github/last-commit/Valossa515/dotcourier" alt="Last commit"/></a>
</p>

---

> 🌐 **Language / Idioma:** 🇺🇸 **English** (current) | 🇧🇷 [Português](README.pt-BR.md)

## 🧠 Why DotCourier?

MediatR 13+ is commercially licensed. DotCourier is a **free, MIT-licensed** alternative that
speaks the same API, so moving over is mostly a `using` swap. It also ships the parts every team
ends up hand-rolling around MediatR.

- 🔁 **Drop-in for MediatR.** Same contract names: `IRequest<T>`, `IRequestHandler<,>`,
  `INotification`, `IPipelineBehavior<,>`, `IMediator`, `ISender`, `IPublisher`, and
  `AddCourier` takes the same configuration methods as `AddMediatR`.
- 🛡️ **Production-grade out of the box.** Logging, validation, OpenTelemetry traces and metrics,
  and timeouts are built in. Retry, circuit breaker, rate limiting and bulkhead come from one extra package.
- 🧾 **Errors become responses, not leaks.** `Dispatch` returns a `Response<T>` with a status
  code. Exceptions are mapped without ever putting a stack trace or connection string in an HTTP body.
- ☕ **Sibling of [Spring Courier](https://github.com/Valossa515/spring-courier).** Same design on
  the JVM, so mixed Java/.NET shops get one mental model.

## 🔄 Before / After

<p align="center">
  <img src="https://raw.githubusercontent.com/Valossa515/dotcourier/main/assets/before-after.svg" alt="Before and after using DotCourier" width="100%"/>
</p>

<details>
<summary>See the same comparison as plain code</summary>

**Before**: MediatR, a license key, behaviors you maintain, and a `try/catch` per endpoint:

```csharp
builder.Services.AddMediatR(cfg =>
{
    cfg.LicenseKey = config["MediatR:Key"];
    cfg.RegisterServicesFromAssemblyContaining<Program>();
    cfg.AddOpenBehavior(typeof(MyValidation<,>));   // hand-written
    cfg.AddOpenBehavior(typeof(MyLogging<,>));      // hand-written
    cfg.AddOpenBehavior(typeof(MyPollyRetry<,>));   // hand-written
});

app.MapPost("/products", async (CreateProduct c, ISender s) =>
{
    try { return Results.Ok(await s.Send(c)); }
    catch (ValidationException e) { return Results.BadRequest(e.Errors); }
    catch (NotFoundException e) { return Results.NotFound(e.Message); }
    catch (Exception) { return Results.Problem(); }
});
```

**After**:

```csharp
builder.Services.AddCourier(cfg => cfg
    .RegisterServicesFromAssemblyContaining<Program>()
    .AddResilience(o => o.Retry.Enabled = true));

app.MapPost("/products", (CreateProduct c, ICourier courier) =>
    courier.Dispatch(c).ToResult());
```

</details>

## 📦 Packages

| Package | What it adds |
|---|---|
| [`DotCourier`](https://www.nuget.org/packages/DotCourier) | Dispatcher, pipeline, notifications, `Response<T>`, logging, validation, telemetry, timeouts |
| [`DotCourier.AspNetCore`](https://www.nuget.org/packages/DotCourier.AspNetCore) | `Response<T>.ToResult()` (Minimal APIs) and `ToActionResult()` (MVC) |
| [`DotCourier.Resilience`](https://www.nuget.org/packages/DotCourier.Resilience) | Retry, circuit breaker, rate limiter and bulkhead on Polly v8 |
| [`DotCourier.FluentValidation`](https://www.nuget.org/packages/DotCourier.FluentValidation) | Runs your `IValidator<T>` in the pipeline; FluentValidation exceptions become 400 |
| [`DotCourier.Caching`](https://www.nuget.org/packages/DotCourier.Caching) | Query caching, idempotent commands and cache invalidation on HybridCache (Redis-ready) |

All packages target **net8.0** and **net10.0**.

## ⚡ Quick start

```bash
dotnet add package DotCourier
dotnet add package DotCourier.AspNetCore
```

```csharp
builder.Services.AddCourier(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
```

```csharp
public sealed record CreateProduct([Required] string Name, [Range(0.01, 10_000)] decimal Price) : ICommand<Guid>;

public sealed class CreateProductHandler(AppDbContext db) : ICommandHandler<CreateProduct, Guid>
{
    public async Task<Guid> Handle(CreateProduct command, CancellationToken ct)
    {
        var product = new Product(command.Name, command.Price);
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return product.Id;
    }
}

public sealed record GetProduct(Guid Id) : IQuery<Response<ProductDto>>;

public sealed class GetProductHandler(AppDbContext db) : IQueryHandler<GetProduct, Response<ProductDto>>
{
    public async Task<Response<ProductDto>> Handle(GetProduct query, CancellationToken ct) =>
        await db.Products.FindAsync([query.Id], ct) is { } p
            ? new ProductDto(p.Id, p.Name, p.Price)          // implicit success
            : Response.NotFound($"Product {query.Id} not found");
}
```

```csharp
app.MapPost("/products", (CreateProduct cmd, ICourier courier, CancellationToken ct) =>
    courier.Dispatch(cmd, ct).ToResult());

app.MapGet("/products/{id:guid}", (Guid id, ICourier courier, CancellationToken ct) =>
    courier.Dispatch(new GetProduct(id), ct).ToResult());
```

An invalid `CreateProduct` never reaches the handler and comes back as:

```json
{ "data": null, "error": "Validation failed with 1 error(s)", "success": false, "statusCode": 400,
  "errorDetails": [ { "field": "Price", "message": "The field Price must be between 0.01 and 10000." } ] }
```

## 📨 `Send` vs `Dispatch`

| | Returns | On failure |
|---|---|---|
| `Send(request)` | `TResponse` | throws (MediatR semantics) |
| `Dispatch(request)` | `Response<TResponse>` | returns an error response; only rethrows cancellation you requested |

`Dispatch` only puts the message of a `CourierException` (and its subclasses) in the response.
Anything else becomes `"An internal error occurred"` with status 500. Throw your own business
errors as `CourierException` subclasses to surface them:

```csharp
public sealed class OutOfStockException(string sku) : CourierException($"SKU {sku} is out of stock", 409);
```

When a handler already returns `Response<T>`, `Dispatch` hands it back as-is instead of wrapping it twice.

### Status helpers and error codes

Handlers that return `Response<T>` pick the status with helpers. No base class, no `try/catch`:

```csharp
public sealed class ShipOrderHandler(AppDbContext db) : ICommandHandler<ShipOrder, Response<ShipmentDto>>
{
    public async Task<Response<ShipmentDto>> Handle(ShipOrder cmd, CancellationToken ct)
    {
        var order = await db.Orders.FindAsync([cmd.OrderId], ct);
        if (order is null) return Response.NotFound("Order not found", code: "ORD-404");
        if (order.OwnerId != cmd.UserId) return Response.Forbidden("Not your order");
        if (order.Shipped) return Response.NoContent();

        var shipment = order.Ship();
        await db.SaveChangesAsync(ct);
        return Response.Created(shipment.ToDto());
    }
}
```

| Helper | Status |
|---|---|
| return the value / `Response.Success(x)` | 200 |
| `Response.Created(x)` | 201 |
| `Response.NoContent()` | 204 (`ToResult()` sends no body) |
| `Response.BadRequest(msg)` | 400 |
| `Response.Unauthorized(msg)` | 401 |
| `Response.Forbidden(msg)` | 403 |
| `Response.NotFound(msg)` | 404 |
| `Response.Conflict(msg)` | 409 |
| `Response.Failure(msg, status)` | any |

Every error helper takes an optional `code`, serialized as `"errorCode"` (omitted when absent) so
clients can branch on it. Exceptions carry one too:
`throw new CourierException("Out of stock", 409) { Code = "STK-001" };`
Validation errors have a per-field `code` (FluentValidation's `WithErrorCode` is kept).

## 🧩 Pipeline

```csharp
builder.Services.AddCourier(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();
    cfg.AddOpenBehavior(typeof(AuditBehavior<,>));                               // order 0: innermost
    cfg.AddOpenBehavior(typeof(TenantBehavior<,>), order: BehaviorOrder.Logging + 1);
    cfg.AddBehavior<CreateProductMetricsBehavior>();                             // closed: one request only
});
```

Behaviors are placed by an explicit **order**, not by registration accident. Lower = outermost:

| Order | Behavior | Package |
|------:|---|---|
| -2000 | exception handlers (`IRequestExceptionHandler<,,>`) | core |
| -1900 / -1800 | pre / post processors | core |
| -1000 | logging | core |
| -995 | idempotency | Caching |
| -990 | telemetry | core |
| -951 / -950 | cache invalidation / caching | Caching |
| -900 | validation | core |
| -870 | rate limiter | Resilience |
| -860 | circuit breaker | Resilience |
| -850 | retry | Resilience |
| -840 | bulkhead | Resilience |
| -700 | timeout | core |
| 0 | your behaviors (default) | |

Turn built-ins off with `cfg.EnableLogging`, `EnableTelemetry`, `EnableValidation`,
`UseDataAnnotations` or `EnableTimeouts`.

## ✅ Validation

DataAnnotations work as written on positional records. The standard `Validator` misses
attributes on record parameters; DotCourier reads them:

```csharp
public sealed record Register([Required] string Name, [EmailAddress] string Email) : ICommand;
```

For rules that need services, implement `IRequestValidator<T>` (picked up by assembly scanning):

```csharp
public sealed class RegisterValidator(AppDbContext db) : IRequestValidator<Register>
{
    public async ValueTask<IEnumerable<ValidationError>> ValidateAsync(Register r, CancellationToken ct) =>
        await db.Users.AnyAsync(u => u.Email == r.Email, ct)
            ? [new ValidationError(nameof(r.Email), "Email already registered")]
            : [];
}
```

### FluentValidation

```bash
dotnet add package DotCourier.FluentValidation
```

```csharp
builder.Services.AddCourier(cfg => cfg
    .RegisterServicesFromAssemblyContaining<Program>()
    .AddFluentValidation());

public sealed class SignupValidator : AbstractValidator<Signup>
{
    public SignupValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress().WithErrorCode("EMAIL-01");
}
```

- Your `IValidator<T>`s run in the validation step, before the handler, and their failures are
  merged with DataAnnotations into a **single 400**. Validators in the scanned assemblies are
  registered for you (pass `registerValidators: false` if you already use `AddValidatorsFromAssembly`).
- A FluentValidation `ValidationException` thrown **inside** a handler (e.g. `ValidateAndThrowAsync`)
  also becomes a 400. Without this package it is an unexpected exception and `Dispatch` answers 500.
  The translation happens right around the handler, so retry never repeats an invalid request.

## 🔌 Your own handler interface

Already have an `IHandler<TInput, TOutput>` with an `Execute` method (often paired with a
`HandlerBase` doing validation and `try/catch`)? Keep it. Make it inherit from
`IRequestHandler` once, with a default interface method that forwards `Handle` to `Execute`,
and every existing handler is discovered and runs through the pipeline unchanged:

```csharp
public interface IHandler<TInput, TOutput> : IRequestHandler<TInput, Response<TOutput>>
    where TInput : IRequest<Response<TOutput>>
{
    Task<Response<TOutput>> Execute(TInput request, CancellationToken ct);

    // The one-time bridge to DotCourier. Handlers never see it.
    Task<Response<TOutput>> IRequestHandler<TInput, Response<TOutput>>.Handle(TInput request, CancellationToken ct)
        => Execute(request, ct);
}

public sealed record GetOrder(int Id) : IQuery<Response<OrderDto>>;

public sealed class GetOrderHandler(AppDbContext db) : IHandler<GetOrder, OrderDto>
{
    public async Task<Response<OrderDto>> Execute(GetOrder request, CancellationToken ct) =>
        await db.Orders.FindAsync([request.Id], ct) is { } order
            ? order.ToDto()
            : Response.NotFound($"Order {request.Id} not found", code: "ORD-404");
}
```

The `HandlerBase` itself can go: validation, error mapping and logging come from the pipeline,
and its `NotFound`/`Created`/... helpers are `Response.NotFound(...)` and friends.

> Spring Courier finds such handlers by method name (`handle`/`execute`) at runtime. In .NET the
> bridge is checked by the compiler and involves no reflection per call.

## 🧬 Generic requests and handlers

Everything Spring Courier supports works, and DotCourier goes further:

```csharp
// One open generic handler serves Create<Product>, Create<Order>, ...
public sealed record Create<T>(T Entity) : ICommand<T>;

public sealed class CreateHandler<T>(AppDbContext db) : ICommandHandler<Create<T>, T>
    where T : class, IEntity
{
    public async Task<T> Handle(Create<T> cmd, CancellationToken ct)
    {
        db.Set<T>().Add(cmd.Entity);
        await db.SaveChangesAsync(ct);
        return cmd.Entity;
    }
}

// Runs for every notification that implements IAudited.
public sealed class AuditNotifications<T>(IAuditLog audit) : INotificationHandler<T>
    where T : INotification, IAudited
{
    public Task Handle(T notification, CancellationToken ct) => audit.Write(notification, ct);
}
```

| Shape | Spring Courier | DotCourier |
|---|---|---|
| Handler inheriting from a generic base (`Handler : BaseHandler<Cmd, Result>`) | ✅ | ✅ |
| Handler through your own interface (`IMyHandler<T> : IRequestHandler<T, X>`) | ✅ | ✅ |
| Generic request with one open handler (`CreateHandler<T>` for `Create<T>`) | ❌ | ✅ |
| Open notification handler / processor / validator (`X<T> : INotificationHandler<T>`) | ❌ | ✅ |

Open request handlers are closed on the **first dispatch** of each request type: the type arguments
are inferred from the request, constraints are checked (a request that does not satisfy them gets
`HandlerNotFoundException`), and the activation factory is cached. There is no startup cost and no
limit on how many closed types you use. If two open handlers fit the same request, DotCourier throws
instead of picking one. Logs, traces and metrics name these requests `Create<Product>`, not
``Create`1``.

## 🛡️ Resilience

```bash
dotnet add package DotCourier.Resilience
```

```csharp
builder.Services.AddCourier(cfg => cfg
    .RegisterServicesFromAssemblyContaining<Program>()
    .AddResilience(o =>
    {
        o.Retry.Enabled = true;                         // opt-in
        o.ForRequest<GenerateReport>(r =>
        {
            r.Bulkhead.Enabled = true;
            r.Bulkhead.MaxConcurrency = 2;
        });
        o.ForRequest<PlaceOrder>(r => r.Retry.Enabled = false); // not idempotent
    }));
```

Each request type gets its **own** circuit, rate-limit window and bulkhead pool.

| Strategy | Default | On rejection |
|---|---|---|
| Circuit breaker | **on** | `CircuitOpenException` → **503** |
| Retry | off | (last failure propagates) |
| Rate limiter | off | `RateLimitExceededException` → **429** |
| Bulkhead | off | `BulkheadRejectedException` → **503** |

**Why only the breaker is on by default:** it only acts once something is already failing, so it
cannot throttle a healthy system. Retry can apply a non-idempotent command twice, and rate limiter
and bulkhead impose hard ceilings; all three are deliberate choices.

The placement in the pipeline is deliberate too, and pinned by tests:

- **Breaker outside retry.** An open circuit is rejected *once* instead of being retried N times.
  Retrying into a failing dependency amplifies the outage.
- **Bulkhead inside retry.** A slot is taken per attempt and released during the backoff delay,
  so sleeping retries do not starve the pool.
- **Rate limiter after validation.** Invalid requests do not consume quota.

Client errors (`CourierException` with a status below 500: validation, not found, conflict) are
neither retried nor counted by the breaker. Override with `o.ShouldHandle`. Invalid settings throw
at startup, not on the first request.

## 🗄️ Caching and idempotency

```bash
dotnet add package DotCourier.Caching
```

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379"); // optional: shared L2
builder.Services.AddCourier(cfg => cfg
    .RegisterServicesFromAssemblyContaining<Program>()
    .AddCaching());

[Cacheable(60)]                                   // seconds; omit for the default (5 min)
public sealed record GetProduct(Guid Id) : IQuery<Response<ProductDto>>;

[InvalidatesCache(typeof(GetProduct), typeof(ListProducts))]
public sealed record UpdatePrice(Guid Id, decimal Price) : ICommand<Response<Unit>>;

[Idempotent]                                      // default: remembered for 1 hour
public sealed record PlaceOrder(string IdempotencyKey, Cart Cart) : ICommand<Response<OrderDto>>, IIdempotencyKey;
```

- **`[Cacheable]`** serves equal requests from the cache. Only successful, non-null results are
  stored; error responses and exceptions never are.
- **`[InvalidatesCache]`** evicts the listed queries once the command succeeds (after the
  transaction, since it sits outside it). `typeof(GetById<>)` evicts every closed type.
- **`[Idempotent]`** runs a command once: repeats get the recorded result, and **concurrent
  duplicates wait for the first execution** instead of running in parallel. Failures are not
  recorded, so a retry runs again. Implement `IIdempotencyKey` to key it by a client-provided
  `Idempotency-Key` header instead of the request content.
- **`ICourierCache`** evicts from code: `InvalidateAsync<GetProduct>()`, `InvalidateAsync(request)`,
  `InvalidateAllAsync()`, `ForgetIdempotentResultAsync(request)`.

Storage is Microsoft **HybridCache**: in-memory per instance, plus any registered
`IDistributedCache` (Redis, SQL Server, ...) as a shared second level, so a result computed on one
instance is reused by the others. `AddDistributedMemoryCache()` does **not** count as a shared level
(HybridCache ignores it, being in-process anyway).

Keys come from the request **serialized as JSON** (hashed), not from `ToString()`: a record's
`ToString()` prints a list's type rather than its items, so two different requests would share an
entry. Implement `ICacheKey` to choose the key yourself. Cached results must be serializable with
System.Text.Json.

| Option (`AddCaching(o => ...)`) | Default | |
|---|---|---|
| `DefaultCacheDuration` | 5 min | for `[Cacheable]` without seconds |
| `DefaultIdempotencyDuration` | 1 h | for `[Idempotent]` without seconds |
| `CacheAllQueries` | `false` | cache every `IQuery<T>` (Spring Courier's behavior) |
| `LocalCacheDuration` | entry duration | caps the in-memory copy; with several instances, an eviction clears the shared level but other instances keep their memory copy until this expires |
| `KeyPrefix` | `dotcourier` | to share one cache server between apps |

## ⏱️ Timeouts

```csharp
[HandlerTimeout(2_000)]
public sealed record GenerateReport(int Year) : IQuery<Report>;
```

The handler's `CancellationToken` is cancelled after 2 s, and the caller gets a
`RequestTimeoutException` (504) on time even if the handler ignores the token. It runs on the
same async flow, so it composes with transactions and ambient context. With retry enabled, each
attempt gets its own timeout.

## 📣 Notifications

```csharp
await courier.Publish(new ProductCreated(id), ct);
```

| `cfg.NotificationPublishStrategy` | Behavior |
|---|---|
| `Sequential` (default) | one by one; failures are logged, the rest still run, `Publish` does not throw |
| `ParallelWhenAll` | all at once; failures logged; avoid sharing a scoped `DbContext` |
| `StopOnFirstError` | one by one; first failure throws `NotificationHandlerException` |

> MediatR's default publisher throws on the first failure. If you depend on that, pick `StopOnFirstError`.

## 📈 Telemetry

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(CourierDiagnostics.SourceName))
    .WithMetrics(m => m.AddMeter(CourierDiagnostics.SourceName));
```

| Signal | Name | Tags |
|---|---|---|
| Activity | `courier {RequestType}` | `courier.request.type`, `error.type` |
| Counter | `courier.requests` | `courier.request.type`, `courier.outcome`, `error.type` |
| Histogram (s) | `courier.request.duration` | same as above |
| Counter | `courier.resilience.rejected` | `courier.request.type`, `courier.resilience.strategy` |
| Counter | `courier.retry.attempts` | `courier.request.type` |
| Counter | `courier.cache.hits` / `courier.cache.misses` | `courier.request.type` |
| Counter | `courier.idempotency.hits` / `courier.idempotency.misses` | `courier.request.type` |

## 🔀 Migrating from MediatR

1. Replace the `MediatR` package with `DotCourier`, and `using MediatR;` with `using DotCourier;`.
2. Replace `services.AddMediatR(cfg => ...)` with `services.AddCourier(cfg => ...)`.
   `RegisterServicesFromAssembly*`, `AddBehavior` and `AddOpenBehavior` keep their names.
3. `IMediator`, `ISender` and `IPublisher` still resolve. Inject `ICourier` where you want `Dispatch`.
4. Using FluentValidation with a hand-written `ValidationBehavior`? Delete it, add
   `DotCourier.FluentValidation` and call `cfg.AddFluentValidation()`.

Differences to watch for:

- `IRequest` is `IRequest<Unit>`, as in MediatR 11, so `Send` on a void request returns `Task<Unit>`.
- Notification failures are logged rather than thrown by default (see above).
- Logging, telemetry, validation and timeouts are on by default.
- Streaming requests (`IStreamRequest`) are not supported yet.

### At a glance

| | MediatR 13+ | DotCourier |
|---|---|---|
| License | commercial (license key) | **MIT** |
| Request / handler / behavior API | ✅ | ✅ same names |
| Built-in validation, logging, telemetry | ❌ write your own | ✅ |
| FluentValidation in the pipeline | ❌ write your own behavior | ✅ `DotCourier.FluentValidation` |
| Generic request handlers (`Create<T>`) | opt-in, closed at startup | ✅ closed on first dispatch |
| Retry / circuit breaker / rate limit / bulkhead | ❌ | ✅ `DotCourier.Resilience` |
| Query caching / idempotent commands | ❌ | ✅ `DotCourier.Caching` |
| Exceptions → status-coded `Response<T>` | ❌ | ✅ `Dispatch` |
| Explicit behavior ordering | registration order | ✅ `BehaviorOrder` |
| Streaming requests | ✅ | 🔜 |

## 🗺️ Roadmap

Each [Spring Courier](https://github.com/Valossa515/spring-courier) module gets a .NET counterpart:

- [x] Core: dispatcher, pipeline, validation, telemetry, timeouts
- [x] ASP.NET Core integration
- [x] Resilience: retry, circuit breaker, rate limiter, bulkhead (Polly v8)
- [x] FluentValidation integration
- [x] Generic requests and open generic handlers
- [x] Status helpers and machine-readable error codes
- [x] Caching and idempotency: `HybridCache` / `IDistributedCache` (Redis included)
- [ ] Transactions: EF Core / `TransactionScope` behavior
- [ ] Transactional outbox with EF Core
- [ ] Outbox delivery to Kafka
- [ ] Streaming requests
- [ ] Source generator for reflection-free, AOT-friendly dispatch

## 🤝 Contributing

Contributions are welcome. Read the [contributing guide](CONTRIBUTING.md) and the
[code of conduct](CODE_OF_CONDUCT.md). To report a vulnerability, follow the
[security policy](SECURITY.md); please don't open a public issue.

## 📄 License

[MIT](LICENSE). Free for commercial use, forever.
