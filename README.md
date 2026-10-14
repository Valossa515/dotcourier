# DotCourier

**CQRS + Mediator for .NET, batteries included. MIT licensed, forever.**

DotCourier is the .NET sibling of [Spring Courier](https://github.com/Valossa515/spring-courier).
It speaks the MediatR API (`IRequest<T>`, `IRequestHandler<,>`, `INotification`,
`IPipelineBehavior<,>`, `IMediator`), so migrating is mostly swapping a `using`. Unlike a bare
mediator, it also ships the cross-cutting pieces every team ends up writing by hand:

| Built in | What it does |
|---|---|
| `Response<T>` + `Dispatch` | Never-throwing dispatch that turns exceptions into status-coded responses without leaking internals |
| Logging | Start / duration / outcome per request, without logging request contents |
| Validation | DataAnnotations (including on positional record parameters) + your own `IRequestValidator<T>` |
| Telemetry | `ActivitySource` traces and `Meter` metrics, ready for OpenTelemetry |
| Timeouts | `[HandlerTimeout(ms)]` cancels the handler and returns 504 |
| Ordering | Behaviors are placed by an explicit `order`, not by registration accident |
| Pre/post processors, exception handlers | MediatR-compatible |

Targets **net8.0** and **net10.0**.

## Install

```bash
dotnet add package DotCourier
dotnet add package DotCourier.AspNetCore   # optional: Response<T> -> IResult / IActionResult
```

## Quick start

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

## `Send` vs `Dispatch`

| | Returns | On failure |
|---|---|---|
| `Send(request)` | `TResponse` | throws (MediatR semantics) |
| `Dispatch(request)` | `Response<TResponse>` | returns an error response; only rethrows cancellation you requested |

`Dispatch` only puts the message of a `CourierException` (and its subclasses) in the response.
Any other exception becomes `"An internal error occurred"` with status 500, so a stack trace or
connection string never ends up in an HTTP body. Throw your own business errors as
`CourierException` subclasses to surface them:

```csharp
public sealed class OutOfStockException(string sku) : CourierException($"SKU {sku} is out of stock", 409);
```

When a handler already returns `Response<T>`, `Dispatch` hands it back as-is instead of wrapping it twice.

## Pipeline

```csharp
builder.Services.AddCourier(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();
    cfg.AddOpenBehavior(typeof(AuditBehavior<,>));                               // order 0: innermost
    cfg.AddOpenBehavior(typeof(TenantBehavior<,>), order: BehaviorOrder.Logging + 1);
    cfg.AddBehavior<CreateProductMetricsBehavior>();                             // closed: one request only
});
```

Lower order = outermost. Built-in positions (`BehaviorOrder`):

| Order | Behavior |
|---|---|
| -2000 | exception handlers (`IRequestExceptionHandler<,,>`) |
| -1900 / -1800 | pre / post processors |
| -1000 | logging |
| -990 | telemetry |
| -900 | validation |
| -700 | timeout |
| 0 | your behaviors (default) |

The gaps leave room for idempotency, caching, rate limiting, circuit breaker, retry, bulkhead and
transactions. They will slot in at the positions Spring Courier uses, so retry stays outside the
transaction and the circuit breaker stays outside retry.

Turn built-ins off with `cfg.EnableLogging`, `EnableTelemetry`, `EnableValidation`,
`UseDataAnnotations` or `EnableTimeouts`.

## Validation

DataAnnotations work as written on positional records. The standard `Validator` misses attributes
on record parameters, and DotCourier does not:

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

## Timeouts

```csharp
[HandlerTimeout(2_000)]
public sealed record GenerateReport(int Year) : IQuery<Report>;
```

The handler's `CancellationToken` is cancelled after 2 s, and the caller gets a
`RequestTimeoutException` (504) on time even if the handler ignores the token. Unlike Spring
Courier's `@Timeout`, this runs on the same async flow, so it composes with transactions and ambient context.

## Notifications

```csharp
await courier.Publish(new ProductCreated(id), ct);
```

| `cfg.NotificationPublishStrategy` | Behavior |
|---|---|
| `Sequential` (default) | one by one; failures are logged, the rest still run, `Publish` does not throw |
| `ParallelWhenAll` | all at once; failures logged; avoid sharing a scoped `DbContext` |
| `StopOnFirstError` | one by one; first failure throws `NotificationHandlerException` |

> MediatR's default publisher throws on the first failure. If you depend on that, pick `StopOnFirstError`.

## Telemetry

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(CourierDiagnostics.SourceName))
    .WithMetrics(m => m.AddMeter(CourierDiagnostics.SourceName));
```

Each request becomes a `courier {RequestType}` activity. Metrics `courier.requests` (counter) and
`courier.request.duration` (histogram, seconds) are tagged with `courier.request.type`,
`courier.outcome` and `error.type`.

## Migrating from MediatR

1. Replace the package `MediatR` with `DotCourier`, and `using MediatR;` with `using DotCourier;`.
2. Replace `services.AddMediatR(cfg => ...)` with `services.AddCourier(cfg => ...)`. `RegisterServicesFromAssembly*`,
   `AddBehavior` and `AddOpenBehavior` keep their names.
3. `IMediator`, `ISender` and `IPublisher` still resolve. Inject `ICourier` where you want `Dispatch`.

Differences to watch for:
- `IRequest` is `IRequest<Unit>`, as in MediatR 11, so `Send` on a void request returns `Task<Unit>`.
- Notification failures are logged rather than thrown by default (see above).
- Logging, telemetry, validation and timeouts are on by default.
- Streaming requests (`IStreamRequest`) are not supported yet.

## Roadmap

Each Spring Courier module will get a .NET counterpart:

- **Resilience**: retry, circuit breaker, rate limiter and bulkhead on Polly v8
- **Caching / idempotency**: `[Cacheable]` and `[Idempotent]` over `HybridCache` / `IDistributedCache` (Redis included)
- **Transactions**: EF Core / `TransactionScope` behavior
- **Outbox**: transactional outbox with EF Core and a background dispatcher
- **Messaging**: outbox delivery to Kafka
- Source generator for reflection-free, AOT-friendly dispatch

## License

MIT
