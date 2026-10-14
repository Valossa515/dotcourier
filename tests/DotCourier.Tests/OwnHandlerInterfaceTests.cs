namespace DotCourier.Tests;

// --- an application's own handler interface (no CancellationToken, method named Execute) ---
public interface IAppHandler<TInput, TOutput> : IRequestHandler<TInput, Response<TOutput>>
    where TInput : IRequest<Response<TOutput>>
{
    Task<Response<TOutput>> Execute(TInput request);

    Task<Response<TOutput>> IRequestHandler<TInput, Response<TOutput>>.Handle(TInput request, CancellationToken cancellationToken)
        => Execute(request);
}

// --- the recommended variant that passes the token through ---
public interface IAppHandlerWithToken<TInput, TOutput> : IRequestHandler<TInput, Response<TOutput>>
    where TInput : IRequest<Response<TOutput>>
{
    Task<Response<TOutput>> Execute(TInput request, CancellationToken cancellationToken);

    Task<Response<TOutput>> IRequestHandler<TInput, Response<TOutput>>.Handle(TInput request, CancellationToken cancellationToken)
        => Execute(request, cancellationToken);
}

public sealed record FindTicket(int Id) : IQuery<Response<string>>;

public sealed class FindTicketHandler(CallLog log) : IAppHandler<FindTicket, string>
{
    public Task<Response<string>> Execute(FindTicket request)
    {
        log.Add("execute " + request.Id);
        return Task.FromResult<Response<string>>(request.Id switch
        {
            0 => throw new InvalidOperationException("Server=prod;Password=secret"),
            1 => Response.NotFound("Ticket not found", code: "TCK-404"),
            _ => "ticket " + request.Id,
        });
    }
}

public sealed record WaitForTicket(int DelayMs) : IQuery<Response<string>>;

public sealed class WaitForTicketHandler : IAppHandlerWithToken<WaitForTicket, string>
{
    public async Task<Response<string>> Execute(WaitForTicket request, CancellationToken cancellationToken)
    {
        await Task.Delay(request.DelayMs, cancellationToken);
        return "done";
    }
}

public class OwnHandlerInterfaceTests
{
    [Fact]
    public async Task Handlers_implementing_only_the_app_interface_are_discovered_and_called()
    {
        var (services, log) = TestHost.Build();

        var found = await services.Courier().Dispatch(new FindTicket(7));
        var missing = await services.Courier().Dispatch(new FindTicket(1));

        Assert.Equal("ticket 7", found.Data);
        Assert.Equal(404, missing.StatusCode);
        Assert.Equal("TCK-404", missing.ErrorCode);
        Assert.Equal(["execute 7", "execute 1"], log.Entries);
    }

    [Fact]
    public async Task They_get_the_pipeline_like_any_handler()
    {
        var (services, log) = TestHost.Build(cfg => cfg.AddOpenBehavior(typeof(OuterBehavior<,>)));

        var failed = await services.Courier().Dispatch(new FindTicket(0));

        Assert.Equal(500, failed.StatusCode);
        Assert.Equal("An internal error occurred", failed.Error);
        Assert.Equal(["outer>", "execute 0"], log.Entries);
    }

    [Fact]
    public async Task The_token_variant_receives_the_callers_cancellation()
    {
        var (services, _) = TestHost.Build(cfg => cfg.EnableTimeouts = false);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => services.Courier().Dispatch(new WaitForTicket(5_000), cts.Token));
    }
}
