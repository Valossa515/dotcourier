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
| [`DotCourier.Caching.Redis`](https://www.nuget.org/packages/DotCourier.Caching.Redis) | Redis lock so an `[Idempotent]` command runs once across all instances |
| [`DotCourier.EntityFrameworkCore`](https://www.nuget.org/packages/DotCourier.EntityFrameworkCore) | Every command in an EF Core transaction (commit / rollback), with after-commit side effects |
| [`DotCourier.Outbox.EntityFrameworkCore`](https://www.nuget.org/packages/DotCourier.Outbox.EntityFrameworkCore) | Transactional outbox: notifications stored with your data, delivered at least once in the background |
| [`DotCourier.Kafka`](https://www.nuget.org/packages/DotCourier.Kafka) | Delivers the outbox to Kafka topics, so other services consume the events |
| [`DotCourier.SourceGenerator`](https://www.nuget.org/packages/DotCourier.SourceGenerator) | Compile-time registration: no assembly scanning with reflection at startup |

All packages target **net8.0** and **net10.0**.

## 🧪 Sample application

[**dotcourier-store**](https://github.com/Valossa515/dotcourier-store) is a store (catalog, orders,
payments, reports) in **hexagonal architecture with DDD** that uses every feature below through real
HTTP calls: validation, error codes, caching, idempotency, retry, circuit breaker, rate limiting,
bulkhead, timeouts, notifications and translated messages. It has Swagger UI, EF Core with SQLite,
and an end-to-end test that its CI runs on every push.

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

### Error messages and translation

DotCourier writes a few messages itself: the generic 500, the validation summary, and timeout and
resilience rejections. All of them live in `cfg.Messages`, so you can translate them and give them
codes:

```csharp
builder.Services.AddCourier(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();

    // fixed text, optional code
    cfg.Messages.UnexpectedError = new("Ocorreu um erro inesperado", code: "000");

    // evaluated on every error, so it follows each request's culture (.resx)
    cfg.Messages.ValidationFailed = CourierMessage.From(() => Resources.ValidationFailed, code: "VAL-000");

    // placeholders are filled in
    cfg.Messages.RequestTimeout = "{request} took longer than {timeoutMs} ms";
});
```

| Message | Status | Default | Placeholders |
|---|---|---|---|
| `UnexpectedError` | 500 | An internal error occurred | |
| `ValidationFailed` | 400 | Validation failed with {count} error(s) | `{count}`, `{request}` |
| `HandlerNotFound` | 500 | No handler registered for request type '{request}'. | `{request}` |
| `RequestTimeout` | 504 | Request '{request}' timed out after {timeoutMs} ms | `{request}`, `{timeoutMs}` |
| `CircuitOpen` / `BulkheadRejected` | 503 | Service temporarily unavailable / Service busy... | `{request}` |
| `RateLimitExceeded` | 429 | Too many requests... | `{request}` |

`CourierMessage.From(() => ...)` runs on every error, on the request's own flow, so with ASP.NET
Core request localization the same `.resx` answers in Portuguese or English per request. Messages
you write yourself (`Response.NotFound("...")`, `CourierException`) are untouched.

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
  `Idempotency-Key` header instead of the request content. The response only leaves once the record
  is in the shared cache, so a retry that reaches another instance gets it too. Duplicates arriving
  **at the same time on different instances** need a shared lock (below); without one, waiting for
  the first execution only happens within each instance.
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
| `IdempotencyLockDuration` | 1 min | with a lock: longest a request can hold it; keep it above your slowest idempotent handler |
| `IdempotencyLockWait` | 10 s | with a lock: how long a duplicate waits before answering **409** |
| `ResultSerializerOptions` | — | JSON options for stored results (and keys); **required under Native AOT** (your source-generated context) |

### Several instances: the idempotency lock

```bash
dotnet add package DotCourier.Caching.Redis
```

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379"); // shared results
builder.Services.AddCourier(cfg => cfg
    .RegisterServicesFromAssemblyContaining<Program>()
    .AddCaching()
    .AddRedisIdempotencyLock("localhost:6379"));                                     // shared lock
```

Only the instance holding the lock runs the command. The others wait, find the recorded result as soon
as it is written, and return it. A duplicate that waits longer than `IdempotencyLockWait` gets
**409** (`cfg.Messages.IdempotencyInProgress`) and can retry later. If the instance running the command
crashes, the lock expires after `IdempotencyLockDuration`. Failures still release the lock and are not
recorded, so a retry runs again. For another store, implement `IIdempotencyLock` (one method) and
register it.

## 💾 Transactions (EF Core)

```bash
dotnet add package DotCourier.EntityFrameworkCore
```

```csharp
builder.Services.AddDbContext<StoreDbContext>(o => o.UseSqlServer(connectionString));
builder.Services.AddCourier(cfg => cfg
    .RegisterServicesFromAssemblyContaining<Program>()
    .AddEntityFrameworkTransactions<StoreDbContext>());
```

Every **command** now runs in a transaction: it **commits** when the handler succeeds and **rolls
back** when the handler throws *or* returns an error `Response<T>` (a `409 Conflict` after
`SaveChangesAsync` undoes the save). Queries are not wrapped. Handlers keep calling
`SaveChangesAsync` as usual; nothing is written for good until the command finishes.

```csharp
public sealed class PlaceOrderHandler(StoreDbContext db, ICourierTransaction tx, IEmailSender email)
    : ICommandHandler<PlaceOrder, Response<OrderDto>>
{
    public async Task<Response<OrderDto>> Handle(PlaceOrder command, CancellationToken ct)
    {
        var order = Order.Place(command.Customer, command.Lines);
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        // Runs only after the commit, and never if the command rolls back.
        await tx.OnCommittedAsync(t => email.SendConfirmation(order.Id, t), ct);
        return Response.Created(OrderDto.From(order));
    }
}
```

- **Nested commands** (sent from inside a handler) join the outer transaction: one commit at the
  end, or one rollback of everything.
- **`ICourierTransaction.OnCommittedAsync`** is Spring's `afterCommit`: e-mails, messages and other
  side effects wait for the commit and are dropped on rollback. Outside a transaction they run
  immediately. A failing action is logged; it cannot undo the commit.
- **Retry** sits outside the transaction, so each attempt gets a fresh one. A rollback also
  **clears the change tracker** (like JPA detaching the persistence context), so the next attempt
  does not re-save the failed attempt's entities.
- Works with `EnableRetryOnFailure`: the transaction runs through the context's execution strategy.
- `[NotTransactional]` keeps a command out (e.g. one that only calls an external API);
  `[Transactional]` brings in a plain `IRequest<T>`. Both live in the core package.

| Option (`AddEntityFrameworkTransactions<T>(o => ...)`) | Default | |
|---|---|---|
| `IsolationLevel` | provider default | e.g. `IsolationLevel.Serializable` |
| `AllRequests` | `false` | wrap every request, for apps that model writes as plain `IRequest<T>` (MediatR style) |

## 📮 Transactional outbox

```bash
dotnet add package DotCourier.Outbox.EntityFrameworkCore
```

`OnCommittedAsync` runs side effects after the commit, but in memory: if the process dies right
after the commit, they are lost. The **outbox** stores the notification **in the same transaction**
as your data and delivers it afterwards, from a background processor, until it succeeds.

```csharp
// 1. Map the table in your DbContext, then add a migration as usual.
protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddCourierOutbox();

// 2. Enable it (with transactions, so the message commits or rolls back with the command).
builder.Services.AddCourier(cfg => cfg
    .RegisterServicesFromAssemblyContaining<Program>()
    .AddEntityFrameworkTransactions<StoreDbContext>()
    .AddOutbox<StoreDbContext>());

// 3. Capture notifications instead of publishing them.
public sealed class PlaceOrderHandler(StoreDbContext db, IOutbox outbox) : ICommandHandler<PlaceOrder, Response<OrderDto>>
{
    public async Task<Response<OrderDto>> Handle(PlaceOrder command, CancellationToken ct)
    {
        var order = Order.Place(command.Customer, command.Lines);
        db.Orders.Add(order);
        outbox.Add(new OrderPlaced(order.Id, order.Total)); // written by the SaveChanges below
        await db.SaveChangesAsync(ct);
        return Response.Created(OrderDto.From(order));
    }
}
```

- **Atomic:** the message is a row saved by your `SaveChangesAsync` (`outbox.Add`), or by
  `outbox.PublishAsync`, which saves for you. It exists if and only if the data does.
- **At least once:** the processor delivers each message to the notification handlers, every handler
  runs, and any failure has the message retried (up to `MaxAttempts`, then `Failed`). A crash between
  delivering and recording it delivers it again. **Handlers must tolerate duplicates.**
- **Fast:** the processor wakes up right after the commit; polling is only the safety net.
- **Several instances:** a message is claimed with the status as the concurrency token, so only one
  instance delivers it. Messages left `Processing` by a crashed instance return after `ProcessingTimeout`.
- **Elsewhere:** register your own `IOutboxDispatcher` to send messages to a broker instead of the
  in-process handlers, or use [Kafka](#-kafka-outbox-delivery).

| Option (`AddOutbox<T>(o => ...)`) | Default | |
|---|---|---|
| `PollInterval` | 5 s | safety-net polling |
| `BatchSize` | 100 | messages per cycle |
| `MaxAttempts` | 5 | then `Failed` (with `LastError` in the row) |
| `ProcessingTimeout` | 1 min | after which an abandoned message is delivered again |
| `RunProcessor` | `true` | `false` on instances that only write |
| `JsonSerializerOptions` | web defaults | how notifications are stored; give it your source-generated context under Native AOT |

The row stores the notification as JSON and its type without the assembly version, so messages
written before a deploy are still read after it.

## 📡 Kafka (outbox delivery)

```bash
dotnet add package DotCourier.Kafka
```

The outbox delivers to the handlers **of the same application**, which is what a monolith wants. When
the events must **leave** the application, send the outbox to Kafka instead. The producing code does
not change at all:

```csharp
builder.Services.AddCourier(cfg => cfg
    .RegisterServicesFromAssemblyContaining<Program>()
    .AddEntityFrameworkTransactions<StoreDbContext>()
    .AddOutbox<StoreDbContext>()
    .AddKafkaDelivery(o => o.BootstrapServers = "localhost:9092"));
```

This **redirects** delivery (the in-process handlers no longer receive outbox messages); it does not
duplicate it.

| Record part | Content |
|---|---|
| Topic | `TopicPrefix` + event type name: `courier.OrderPlaced`, `courier.DomainEventNotification-OrderPlaced` |
| Key | the outbox message id |
| Value | the stored JSON, **as is** (consumers do not need your event classes) |
| Header `courier-message-id` | the outbox message id, to **deduplicate**: delivery is at least once |
| Header `courier-event-type` | the event's type name |

- **The send is awaited:** a message is only marked delivered after Kafka acknowledges it. A failure
  or a timeout (`SendTimeout`) leaves it pending, and the outbox retries it.
- **Durable producer:** `acks=all` and idempotence on by default. Adjust anything else (SASL, TLS,
  compression) with `ConfigureProducer`.
- **Ordering:** the key is the message id, which spreads load over partitions but gives **no order per
  aggregate**. Register your own `IOutboxDispatcher` if you need it.
- **Routing:** `Topic = "store-events"` sends everything to one topic; register an
  `IOutboxTopicResolver` to decide per message.

| Option (`AddKafkaDelivery(o => ...)`) | Default | |
|---|---|---|
| `BootstrapServers` | (required) | |
| `TopicPrefix` | `courier.` | |
| `Topic` | — | one topic for every event |
| `SendTimeout` | 10 s | wait for the acknowledgement; bounds how long one message can hold up a cycle |
| `ConfigureProducer` | — | any Confluent `ProducerConfig` setting |

## ⏱️ Timeouts

```csharp
[HandlerTimeout(2_000)]
public sealed record GenerateReport(int Year) : IQuery<Report>;
```

The handler's `CancellationToken` is cancelled after 2 s, and the caller gets a
`RequestTimeoutException` (504) on time even if the handler ignores the token. It runs on the
same async flow, so it composes with transactions and ambient context. With retry enabled, each
attempt gets its own timeout.

## 🌊 Streaming requests

For results too large (or too slow) to return at once, such as an export or a feed, the handler
**streams** the items with `yield return` and the caller reads them as they arrive. Same API as
MediatR:

```csharp
public sealed record ExportOrders(DateOnly From) : IStreamRequest<OrderRow>;

public sealed class ExportOrdersHandler(StoreDbContext db) : IStreamRequestHandler<ExportOrders, OrderRow>
{
    public async IAsyncEnumerable<OrderRow> Handle(ExportOrders request, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var order in db.Orders.Where(o => o.Date >= request.From).AsAsyncEnumerable().WithCancellation(ct))
        {
            yield return OrderRow.From(order);
        }
    }
}

await foreach (var row in courier.CreateStream(new ExportOrders(from), ct))
{
    await writer.WriteLineAsync(row.ToCsv());
}
```

In ASP.NET Core, return the stream from an endpoint and it is serialized as a JSON array without
collecting the items first: `app.MapGet("/orders/export", (ICourier c, CancellationToken ct) => c.CreateStream(new ExportOrders(...), ct));`

- **Lazy:** nothing runs until the stream is read; a missing handler or an invalid request surfaces
  on the first read.
- **Validation** (DataAnnotations and `IRequestValidator<T>`, FluentValidation included) runs before
  the first item. **Logging** reports the item count and whether the stream completed, was stopped
  early by the caller, was cancelled or failed. **Telemetry** opens one span for the whole stream,
  parent of the handler's own spans.
- **Cancellation:** the token passed to `CreateStream` and the one passed to `WithCancellation` both
  reach the handler; stopping early (`break`) ends the handler too.
- **Behaviors:** implement `IStreamPipelineBehavior<TRequest, TItem>` and register it with
  `cfg.AddOpenStreamBehavior(typeof(MyBehavior<,>), order)` or `cfg.AddStreamBehavior<...>()`.
  Request behaviors (retry, caching, transactions...) do not apply to streams.
- Open generic stream handlers work like the request ones (`StreamAll<T>` served by `StreamAllHandler<T>`).

## 📣 Notifications

```csharp
await courier.Publish(new ProductCreated(id), ct);
```

| `cfg.NotificationPublishStrategy` | Behavior |
|---|---|
| `Sequential` (default) | one by one; failures are logged, the rest still run, `Publish` does not throw |
| `ParallelWhenAll` | all at once; failures logged; avoid sharing a scoped `DbContext` |
| `StopOnFirstError` | one by one; first failure throws `NotificationHandlerException` |
| `SequentialThenThrow` | one by one; every handler runs, then failures are thrown (the outbox uses it) |

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

## ⚙️ Compile-time registration (source generator)

```bash
dotnet add package DotCourier.SourceGenerator
```

That's all: no code changes. In every project that references it, the generator writes the
registrations of that assembly's handlers, processors and validators at compile time, and
`AddCourier` uses them instead of scanning the assembly with reflection at startup. Dispatching also
skips building its per-request wrappers at runtime (`MakeGenericType`).

- **Same result as the scan:** the same services, lifetimes and **order** (which decides the order
  notification handlers run in). DotCourier's own tests run on the generated registrations, and one
  test compares them, entry by entry, with the scan.
- **Per project:** add the package to each project that declares handlers (a `Handlers` or
  `Application` project). Assemblies without it keep being scanned, so you can mix both.
- **Warnings:** `DOTCOURIER001` when a handler (or its request) is `private`/`protected`, so
  generated code cannot reach it: that assembly falls back to scanning; make it `internal`.
  `DOTCOURIER002` when two classes handle the same request (only the first is used, as with the scan).
- `cfg.UseGeneratedRegistrations = false` turns it off without removing the package.

## 🚀 Native AOT

`DotCourier`, `DotCourier.AspNetCore`, `DotCourier.Resilience` and `DotCourier.Caching` work in apps
published with `<PublishAot>true</PublishAot>`, together with `DotCourier.SourceGenerator` in every project
that declares handlers. CI publishes a test app with Native AOT on every push and runs it: requests
(including value-type responses), validation, exception handlers, notifications, streams, timeouts, retry,
caching, idempotency and ASP.NET Core results.

Under Native AOT, DotCourier runs in **compile-time mode**: nothing is built with `MakeGenericType` at
runtime. Behaviors are registered already closed for each request (by the core, by each package, and by the
generator for the open behaviors you declare), and exception handlers are called through typed invokers.

```csharp
builder.Services.AddCourier(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

// Response<T> is serialized with your source-generated JSON context, as everything in an AOT app.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJson.Default));

[JsonSerializable(typeof(Response<ProductDto>))]
[JsonSerializable(typeof(Response<Unit>))]
[JsonSerializable(typeof(IReadOnlyList<ValidationError>))] // the details of a 400
internal sealed partial class AppJson : JsonSerializerContext;

// Caching: results (and requests, for the keys) through the same context.
cfg.AddCaching(o => o.ResultSerializerOptions = AppJson.Default.Options);
```

- **Find problems before publishing:** set `cfg.RequireCompileTimeRegistrations = true` in your tests to run
  the same compile-time mode on the regular runtime. (`PublishAot=true` already turns it on for `dotnet run`.)
- **Not supported in compile-time mode, with a clear error at startup or on dispatch:**
  - open generic request handlers (`Create<T>` handled by `CreateHandler<T>`);
  - open behaviors with constraints other than `notnull`;
  - assemblies without the generator.
- **Library authors:** register a `BehaviorTypes` with `cfg.AddBehaviorTypes(...)` next to your
  `AddOpenBehavior` calls, so your open behaviors can be closed too.
- **Caching:** HybridCache's own default serializer uses reflection, so a trimmed app gets warnings from
  `Microsoft.Extensions.Caching.Hybrid` (IL2026/IL2070). With `ResultSerializerOptions` set, that serializer is
  never used for DotCourier's entries. On net10.0, DotCourier.Caching uses HybridCache 10.x.
- **Outbox, transactions, Kafka:** DotCourier's own code is free of trim/AOT warnings. The outbox serializes
  through `CourierOutboxOptions.JsonSerializerOptions` (give it your context) and maps stored type names
  through the generated registrations. These packages still don't claim AOT support: EF Core's own Native
  AOT support is experimental, and Confluent.Kafka is not verified there.
- **Not AOT-ready:** `DotCourier.FluentValidation` (FluentValidation itself isn't).

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
- Stream behaviors are registered with `AddOpenStreamBehavior(type, order)`: the second parameter
  is the pipeline order, not the lifetime (as with `AddOpenBehavior`).

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
| Transaction per command + after-commit actions | ❌ write your own behavior | ✅ `DotCourier.EntityFrameworkCore` |
| Transactional outbox | ❌ | ✅ `DotCourier.Outbox.EntityFrameworkCore` |
| Outbox delivery to Kafka | ❌ | ✅ `DotCourier.Kafka` |
| Exceptions → status-coded `Response<T>` | ❌ | ✅ `Dispatch` |
| Explicit behavior ordering | registration order | ✅ `BehaviorOrder` |
| Streaming requests (`CreateStream`) | ✅ | ✅ same names |
| Native AOT | ❌ | ✅ with `DotCourier.SourceGenerator` |

## 🗺️ Roadmap

Each [Spring Courier](https://github.com/Valossa515/spring-courier) module gets a .NET counterpart:

- [x] Core: dispatcher, pipeline, validation, telemetry, timeouts
- [x] ASP.NET Core integration
- [x] Resilience: retry, circuit breaker, rate limiter, bulkhead (Polly v8)
- [x] FluentValidation integration
- [x] Generic requests and open generic handlers
- [x] Status helpers and machine-readable error codes
- [x] Caching and idempotency: `HybridCache` / `IDistributedCache` (Redis included)
- [x] Transactions: EF Core, with after-commit actions
- [x] Transactional outbox with EF Core
- [x] Outbox delivery to Kafka
- [x] Streaming requests
- [x] Source generator: compile-time registrations, no assembly scanning
- [x] Native AOT: behaviors closed at compile time, verified with `PublishAot` in CI

## 🤝 Contributing

Contributions are welcome. Read the [contributing guide](CONTRIBUTING.md) and the
[code of conduct](CODE_OF_CONDUCT.md). To report a vulnerability, follow the
[security policy](SECURITY.md); please don't open a public issue.

## 📄 License

[MIT](LICENSE). Free for commercial use, forever.
