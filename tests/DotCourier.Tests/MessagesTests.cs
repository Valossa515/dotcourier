using System.Globalization;
using DotCourier.Tests.FluentValidation;
using DotCourier.Tests.Resilience;

namespace DotCourier.Tests;

public class MessagesTests
{
    [Fact]
    public async Task Unexpected_error_message_and_code_are_configurable()
    {
        var (services, _) = TestHost.Build(cfg => cfg.Messages.UnexpectedError = new("Ocorreu um erro inesperado", code: "000"));

        var response = await services.Courier().Dispatch(new Boom(false));

        Assert.Equal(500, response.StatusCode);
        Assert.Equal("Ocorreu um erro inesperado", response.Error);
        Assert.Equal("000", response.ErrorCode);
        Assert.DoesNotContain("hunter2", response.Error);
    }

    [Fact]
    public async Task A_plain_string_is_enough_for_a_message_without_code()
    {
        var (services, _) = TestHost.Build(cfg => cfg.Messages.UnexpectedError = "Falhou");

        var response = await services.Courier().Dispatch(new Boom(false));

        Assert.Equal("Falhou", response.Error);
        Assert.Null(response.ErrorCode);
    }

    [Fact]
    public async Task Lazy_messages_follow_the_culture_of_each_request()
    {
        var (services, _) = TestHost.Build(cfg => cfg.Messages.UnexpectedError = CourierMessage.From(
            () => CultureInfo.CurrentUICulture.Name.StartsWith("pt", StringComparison.Ordinal) ? "Erro inesperado" : "Unexpected error",
            code: "000"));
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("pt-BR");
            var portuguese = await services.Courier().Dispatch(new Boom(false));

            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            var english = await services.Courier().Dispatch(new Boom(false));

            Assert.Equal("Erro inesperado", portuguese.Error);
            Assert.Equal("Unexpected error", english.Error);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public async Task Validation_summary_fills_placeholders_and_keeps_field_errors()
    {
        var (services, _) = TestHost.Build(cfg =>
            cfg.Messages.ValidationFailed = new("{count} erro(s) de validação em {request}", code: "VAL-000"));

        var response = await services.Courier().Dispatch(new Register(null, "not-an-email", 12));

        Assert.Equal(400, response.StatusCode);
        Assert.Equal("3 erro(s) de validação em Register", response.Error);
        Assert.Equal("VAL-000", response.ErrorCode);
        Assert.Equal(3, response.GetErrorDetails<IReadOnlyList<ValidationError>>()!.Count);
    }

    [Fact]
    public async Task Timeout_message_is_configurable()
    {
        var (services, _) = TestHost.Build(cfg => cfg.Messages.RequestTimeout = new("{request} excedeu {timeoutMs} ms", code: "TMO"));

        var response = await services.Courier().Dispatch(new Slow(5_000));

        Assert.Equal(504, response.StatusCode);
        Assert.Equal("Slow excedeu 100 ms", response.Error);
        Assert.Equal("TMO", response.ErrorCode);
    }

    [Fact]
    public async Task Handler_not_found_message_is_configurable()
    {
        var (services, _) = TestHost.Build(cfg => cfg.Messages.HandlerNotFound = new("Operação indisponível", code: "NO-HANDLER"));

        var response = await services.Courier().Dispatch(new NoHandler());

        Assert.Equal("Operação indisponível", response.Error);
        Assert.Equal("NO-HANDLER", response.ErrorCode);
    }

    [Fact]
    public async Task Resilience_rejections_use_the_configured_messages()
    {
        var (services, _) = TestHost.Build(cfg =>
        {
            cfg.Messages.CircuitOpen = new("Serviço indisponível no momento ({request})", code: "CB-OPEN");
            cfg.AddResilience(o => o.CircuitBreaker.MinimumThroughput = 2);
        });
        var courier = services.Courier();

        await courier.Dispatch(new AlwaysFails("a"));
        await courier.Dispatch(new AlwaysFails("a"));
        var rejected = await courier.Dispatch(new AlwaysFails("a"));

        Assert.Equal(503, rejected.StatusCode);
        Assert.Equal("Serviço indisponível no momento (AlwaysFails)", rejected.Error);
        Assert.Equal("CB-OPEN", rejected.ErrorCode);
    }

    [Fact]
    public async Task FluentValidation_exceptions_from_handlers_use_the_configured_summary()
    {
        var (services, _) = TestHost.Build(cfg =>
        {
            cfg.Messages.ValidationFailed = new("Dados inválidos", code: "VAL-000");
            cfg.AddFluentValidation();
        });

        var response = await services.Courier().Dispatch(new Rate(9));

        Assert.Equal(400, response.StatusCode);
        Assert.Equal("Dados inválidos", response.Error);
        Assert.Equal("VAL-000", response.ErrorCode);
    }

    [Fact]
    public async Task Messages_written_in_handlers_are_not_affected()
    {
        var (services, _) = TestHost.Build(cfg => cfg.Messages.UnexpectedError = new("Ocorreu um erro inesperado", code: "000"));

        var response = await services.Courier().Dispatch(new Ship(0));

        Assert.Equal("Order not found", response.Error);
        Assert.Equal("ORD-404", response.ErrorCode);
    }

    [Fact]
    public async Task Defaults_are_unchanged()
    {
        var (services, _) = TestHost.Build();

        var unexpected = await services.Courier().Dispatch(new Boom(false));
        var invalid = await services.Courier().Dispatch(new Register(null, "x", 1));

        Assert.Equal("An internal error occurred", unexpected.Error);
        Assert.Null(unexpected.ErrorCode);
        Assert.Equal("Validation failed with 3 error(s)", invalid.Error);
        Assert.Null(invalid.ErrorCode);
    }
}
