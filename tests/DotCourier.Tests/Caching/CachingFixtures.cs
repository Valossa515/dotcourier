namespace DotCourier.Tests.Caching;

// --- cacheable queries ---------------------------------------------------------------

[Cacheable]
public sealed record GetPrice(string Sku) : IQuery<string>;

public sealed class GetPriceHandler(CallLog log) : IQueryHandler<GetPrice, string>
{
    public Task<string> Handle(GetPrice query, CancellationToken ct)
    {
        log.Add("price " + query.Sku);
        return Task.FromResult($"{query.Sku}#{log.Entries.Count(e => e == "price " + query.Sku)}");
    }
}

/// <summary>A record whose ToString() would collide for different lists: keys must use the content.</summary>
[Cacheable]
public sealed record SearchProducts(List<string> Terms) : IQuery<string>;

public sealed class SearchProductsHandler(CallLog log) : IQueryHandler<SearchProducts, string>
{
    public Task<string> Handle(SearchProducts query, CancellationToken ct)
    {
        log.Add("search");
        return Task.FromResult(string.Join("+", query.Terms));
    }
}

[Cacheable]
public sealed record GetStock(int Id) : IQuery<Response<int>>;

public sealed class GetStockHandler(CallLog log) : IQueryHandler<GetStock, Response<int>>
{
    public Task<Response<int>> Handle(GetStock query, CancellationToken ct)
    {
        log.Add("stock " + query.Id);
        return Task.FromResult<Response<int>>(query.Id == 0 ? Response.NotFound("no stock", code: "STK-404") : query.Id * 10);
    }
}

[Cacheable]
public sealed record GetFlakyRate(string Currency) : IQuery<decimal>;

public sealed class GetFlakyRateHandler(CallLog log) : IQueryHandler<GetFlakyRate, decimal>
{
    public Task<decimal> Handle(GetFlakyRate query, CancellationToken ct)
    {
        log.Add("rate");
        return log.Entries.Count(e => e == "rate") == 1
            ? throw new InvalidOperationException("upstream down")
            : Task.FromResult(5.2m);
    }
}

[Cacheable(1)]
public sealed record GetShortLived(int Id) : IQuery<string>;

public sealed class GetShortLivedHandler(CallLog log) : IQueryHandler<GetShortLived, string>
{
    public Task<string> Handle(GetShortLived query, CancellationToken ct)
    {
        log.Add("short");
        return Task.FromResult("value");
    }
}

public sealed record GetUnmarked(int Id) : IQuery<string>;

public sealed class GetUnmarkedHandler(CallLog log) : IQueryHandler<GetUnmarked, string>
{
    public Task<string> Handle(GetUnmarked query, CancellationToken ct)
    {
        log.Add("unmarked");
        return Task.FromResult("value");
    }
}

[Cacheable]
public sealed record GetByExplicitKey(string Tenant, DateTime RequestedAt) : IQuery<string>, ICacheKey
{
    public string CacheKey => Tenant; // RequestedAt must not split the cache
}

public sealed class GetByExplicitKeyHandler(CallLog log) : IQueryHandler<GetByExplicitKey, string>
{
    public Task<string> Handle(GetByExplicitKey query, CancellationToken ct)
    {
        log.Add("explicit");
        return Task.FromResult("tenant " + query.Tenant);
    }
}

// --- invalidation ------------------------------------------------------------------------

[InvalidatesCache(typeof(GetPrice))]
public sealed record UpdatePrice(string Sku, bool Fail) : ICommand<Response<Unit>>;

public sealed class UpdatePriceHandler : ICommandHandler<UpdatePrice, Response<Unit>>
{
    public Task<Response<Unit>> Handle(UpdatePrice command, CancellationToken ct) =>
        Task.FromResult<Response<Unit>>(command.Fail ? Response.Conflict("price locked") : Response.Success());
}

[Cacheable]
public sealed record Lookup<T>(int Id) : IQuery<string>;

public sealed class LookupHandler<T>(CallLog log) : IQueryHandler<Lookup<T>, string>
{
    public Task<string> Handle(Lookup<T> query, CancellationToken ct)
    {
        log.Add("lookup " + typeof(T).Name);
        return Task.FromResult(typeof(T).Name + " " + query.Id);
    }
}

public sealed record Supplier;

public sealed record Warehouse;

[InvalidatesCache(typeof(Lookup<>))]
public sealed record RefreshLookups : ICommand;

public sealed class RefreshLookupsHandler : ICommandHandler<RefreshLookups>
{
    public Task Handle(RefreshLookups command, CancellationToken ct) => Task.CompletedTask;
}

// --- idempotency -------------------------------------------------------------------------

[Idempotent]
public sealed record PlaceOrder(string Customer, decimal Total) : ICommand<string>;

public sealed class PlaceOrderHandler(CallLog log) : ICommandHandler<PlaceOrder, string>
{
    public async Task<string> Handle(PlaceOrder command, CancellationToken ct)
    {
        log.Add("order " + command.Customer);
        await Task.Delay(150, ct);
        return $"order-{log.Entries.Count(e => e.StartsWith("order ", StringComparison.Ordinal))}";
    }
}

[Idempotent]
public sealed record Charge(string IdempotencyKey, decimal Amount) : ICommand<Response<string>>, IIdempotencyKey;

public sealed class ChargeHandler(CallLog log) : ICommandHandler<Charge, Response<string>>
{
    public Task<Response<string>> Handle(Charge command, CancellationToken ct)
    {
        log.Add("charge " + command.Amount);
        return Task.FromResult<Response<string>>(command.Amount <= 0
            ? Response.BadRequest("Amount must be positive")
            : $"charged {command.Amount}");
    }
}
