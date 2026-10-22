using FluentValidation;
using Microsoft.Extensions.Logging;
using DotCourierValidationError = DotCourier.ValidationError;

namespace DotCourier.Tests.FluentValidation;

public abstract record PageSettings(int PageNumber, int PageSize);

public sealed record GetPagedProducts(int PageNumber, int PageSize) : PageSettings(PageNumber, PageSize), IQuery<string>;

public sealed record GetPagedOrders(int PageNumber, int PageSize) : PageSettings(PageNumber, PageSize), IQuery<string>;

/// <summary>One validator for every paged request, closed by the container per request.</summary>
public class PageSettingsValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : PageSettings
{
    public PageSettingsValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0).WithErrorCode("PAGE-01");
    }
}

/// <summary>A subclass adding rules: the base's rules must not run twice.</summary>
public sealed class GetPagedOrdersValidator : PageSettingsValidator<GetPagedOrders>
{
    public GetPagedOrdersValidator()
    {
        RuleFor(x => x.PageSize).LessThanOrEqualTo(50).WithErrorCode("PAGE-02");
    }
}

public sealed record Batch<T>(IReadOnlyList<T> Items);

/// <summary>Validates <c>Batch&lt;T&gt;</c>, not <c>T</c>: <c>IValidator&lt;X&gt;</c> cannot be mapped to it.</summary>
public sealed class BatchValidator<T> : AbstractValidator<Batch<T>>
{
    public BatchValidator()
    {
        RuleFor(x => x.Items).NotEmpty();
    }
}

public sealed class GetPagedProductsHandler(CallLog log) : IQueryHandler<GetPagedProducts, string>
{
    public Task<string> Handle(GetPagedProducts query, CancellationToken ct)
    {
        log.Add("products");
        return Task.FromResult($"page {query.PageNumber}");
    }
}

public sealed class GetPagedOrdersHandler(CallLog log) : IQueryHandler<GetPagedOrders, string>
{
    public Task<string> Handle(GetPagedOrders query, CancellationToken ct)
    {
        log.Add("orders");
        return Task.FromResult($"page {query.PageNumber}");
    }
}

public class GenericValidatorTests
{
    [Fact]
    public async Task An_open_generic_validator_runs_for_every_request_matching_its_constraint()
    {
        var (services, log) = TestHost.Build(cfg => cfg.AddFluentValidation());

        var invalid = await services.Courier().Dispatch(new GetPagedProducts(0, 10));
        var valid = await services.Courier().Dispatch(new GetPagedProducts(1, 10));
        var unrelated = await services.Courier().Dispatch(new Signup("ana@example.com", 30));

        Assert.Equal(400, invalid.StatusCode);
        var error = Assert.Single(invalid.GetErrorDetails<IReadOnlyList<DotCourierValidationError>>()!);
        Assert.Equal(("PageNumber", "PAGE-01"), (error.Field, error.Code));
        Assert.True(valid.IsSuccess);
        Assert.True(unrelated.IsSuccess); // Signup does not satisfy the constraint, so it is skipped
        Assert.Equal(["products", "signed up"], log.Entries);
    }

    [Fact]
    public async Task A_subclass_of_a_generic_validator_does_not_repeat_its_rules()
    {
        var (services, log) = TestHost.Build(cfg => cfg.AddFluentValidation());

        var response = await services.Courier().Dispatch(new GetPagedOrders(0, 100));

        Assert.Equal(400, response.StatusCode);
        var errors = response.GetErrorDetails<IReadOnlyList<DotCourierValidationError>>()!;
        Assert.Equal(["PAGE-01", "PAGE-02"], errors.Select(e => e.Code).Order());
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task Generic_validators_that_cannot_be_registered_are_logged_once()
    {
        var logs = new CapturingLoggerProvider();
        var (services, _) = TestHost.Build(cfg => cfg.AddFluentValidation(), logs);

        await services.Courier().Dispatch(new GetPagedProducts(1, 10));
        await services.Courier().Dispatch(new GetPagedProducts(1, 10));

        var warning = Assert.Single(logs.Entries, e => e.Category == "DotCourier.FluentValidation");
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains(typeof(BatchValidator<>).FullName!, warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_is_logged_when_validators_are_registered_by_the_application()
    {
        var logs = new CapturingLoggerProvider();
        var (services, _) = TestHost.Build(cfg => cfg.AddFluentValidation(registerValidators: false), logs);

        var response = await services.Courier().Dispatch(new GetPagedProducts(0, 10));

        Assert.True(response.IsSuccess); // no validator registered
        Assert.DoesNotContain(logs.Entries, e => e.Category == "DotCourier.FluentValidation");
    }
}
