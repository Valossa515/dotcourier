using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotCourier.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DotCourier.Tests.AspNetCore;

public enum Color
{
    Blue,
}

public sealed record ProductDto(int Id, string Name, string? Note = null, Color Color = Color.Blue);

public class ResponseConverterTests
{
    private static readonly ProductDto Pen = new(1, "pen");

    private static Response<ProductDto> ValidationFailure() =>
        Response<ProductDto>.Failure(
            "The request has 2 invalid field(s).",
            400,
            new List<ValidationError> { new("Name", "Name is required", "PRD-NAME"), new("Price", "Must be positive") },
            "VAL-000");

    private static (IResponseConverter Converter, IServiceProvider Services) Build(Action<CourierResponseOptions>? configure = null, Action<CourierConfiguration>? courier = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new CallLog());
        services.AddControllers();
        services.AddProblemDetails();
        services.AddCourier(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CallLog>();
            cfg.AddResponseConverter(configure);
            courier?.Invoke(cfg);
        });
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IResponseConverter>(), provider);
    }

    private static async Task<(int Status, string? ContentType, string Body)> Run(IResult result, IServiceProvider services)
    {
        var http = new DefaultHttpContext { RequestServices = services };
        using var body = new MemoryStream();
        http.Response.Body = body;
        await result.ExecuteAsync(http);
        return (http.Response.StatusCode, http.Response.ContentType, Encoding.UTF8.GetString(body.ToArray()));
    }

    private static async Task<(int Status, string? ContentType, string Body)> Run(IActionResult result, IServiceProvider services)
    {
        var http = new DefaultHttpContext { RequestServices = services };
        using var body = new MemoryStream();
        http.Response.Body = body;
        await result.ExecuteResultAsync(new ActionContext(http, new RouteData(), new ActionDescriptor()));
        return (http.Response.StatusCode, http.Response.ContentType, Encoding.UTF8.GetString(body.ToArray()));
    }

    [Fact]
    public async Task Envelope_is_the_default_and_matches_the_static_conversion()
    {
        var (converter, services) = Build();
        Response<ProductDto> created = Response.Created(Pen);

        var minimal = await Run(converter.ToResult(created), services);
        var mvc = await Run(converter.ToActionResult(created), services);

        Assert.Equal((await Run(created.ToResult(), services)).Body, minimal.Body);
        Assert.Equal(201, minimal.Status);
        Assert.Equal(201, mvc.Status);
        Assert.Contains("\"data\":{\"id\":1,\"name\":\"pen\"", minimal.Body, StringComparison.Ordinal);
        Assert.Contains("\"success\":true", mvc.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DataOrProblemDetails_sends_only_the_data_on_success()
    {
        var (converter, services) = Build(o => o.Format = ResponseBodyFormat.DataOrProblemDetails);

        var minimal = await Run(converter.ToResult(Response<ProductDto>.Success(Pen)), services);
        var mvc = await Run(converter.ToActionResult(Response<ProductDto>.Success(Pen)), services);
        var nothing = await Run(converter.ToResult(Response.Success()), services);

        Assert.Equal("""{"id":1,"name":"pen","note":null,"color":0}""", minimal.Body);
        Assert.Equal(minimal.Body, mvc.Body);
        Assert.Equal(204, nothing.Status); // a command without data
    }

    [Fact]
    public async Task DataOrProblemDetails_sends_RFC_7807_on_failure()
    {
        var (converter, services) = Build(o => o.Format = ResponseBodyFormat.DataOrProblemDetails);
        Response<ProductDto> conflict = Response.Conflict("SKU taken", code: "PRD-SKU-TAKEN");

        var minimal = await Run(converter.ToResult(conflict), services);
        var mvc = await Run(converter.ToActionResult(conflict), services);
        var invalid = await Run(converter.ToResult(ValidationFailure()), services);

        Assert.Equal(409, minimal.Status);
        Assert.StartsWith("application/problem+json", minimal.ContentType, StringComparison.Ordinal);
        using (var problem = JsonDocument.Parse(minimal.Body))
        {
            Assert.Equal("SKU taken", problem.RootElement.GetProperty("title").GetString());
            Assert.Equal(409, problem.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("PRD-SKU-TAKEN", problem.RootElement.GetProperty("code").GetString());
        }

        Assert.Equal(409, mvc.Status);
        Assert.Contains("\"code\":\"PRD-SKU-TAKEN\"", mvc.Body, StringComparison.Ordinal);

        Assert.Equal(400, invalid.Status);
        using var validation = JsonDocument.Parse(invalid.Body);
        Assert.Equal("Name is required", validation.RootElement.GetProperty("errors").GetProperty("Name")[0].GetString());
        Assert.Equal("Must be positive", validation.RootElement.GetProperty("errors").GetProperty("Price")[0].GetString());
        Assert.Equal("VAL-000", validation.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task DataOrErrorList_sends_one_item_per_error()
    {
        var (converter, services) = Build(o => o.Format = ResponseBodyFormat.DataOrErrorList);

        var invalid = await Run(converter.ToResult(ValidationFailure()), services);
        var conflict = await Run(converter.ToActionResult(Response<ProductDto>.Failure("SKU taken", 409, code: "PRD-SKU-TAKEN")), services);
        var ok = await Run(converter.ToResult(Response<ProductDto>.Success(Pen)), services);

        Assert.Equal(400, invalid.Status);
        Assert.Equal("""[{"code":"PRD-NAME","message":"Name is required","field":"Name"},{"code":"VAL-000","message":"Must be positive","field":"Price"}]""", invalid.Body);
        Assert.Equal(409, conflict.Status);
        Assert.Equal("""[{"code":"PRD-SKU-TAKEN","message":"SKU taken"}]""", conflict.Body);
        Assert.Equal("""{"id":1,"name":"pen","note":null,"color":0}""", ok.Body);
    }

    [Fact]
    public async Task Serializer_options_shape_the_JSON_of_both_flavours()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
        };
        var (converter, services) = Build(o =>
        {
            o.Format = ResponseBodyFormat.DataOrErrorList;
            o.SerializerOptions = json;
        });

        var minimal = await Run(converter.ToResult(Response<ProductDto>.Success(Pen)), services);
        var mvc = await Run(converter.ToActionResult(Response<ProductDto>.Success(Pen)), services);

        Assert.Equal("""{"id":1,"name":"pen","color":"Blue"}""", minimal.Body);
        Assert.Equal(minimal.Body, mvc.Body);
    }

    [Fact]
    public async Task A_missing_response_is_a_500_with_the_configured_message()
    {
        var (converter, services) = Build(
            o => o.Format = ResponseBodyFormat.DataOrErrorList,
            cfg => cfg.Messages.UnexpectedError = new("Ocorreu um erro inesperado", code: "000"));

        var result = await Run(converter.ToActionResult<ProductDto>(null), services);

        Assert.Equal(500, result.Status);
        Assert.Equal("""[{"code":"000","message":"Ocorreu um erro inesperado"}]""", result.Body);
    }

    [Fact]
    public async Task Files_are_sent_as_downloads_in_every_flavour()
    {
        var (converter, services) = Build(o => o.Format = ResponseBodyFormat.DataOrProblemDetails);
        var csv = Encoding.UTF8.GetBytes("id,name\n1,pen");
        Response<FileResponse> file = new FileResponse(csv, "text/csv", "products.csv");

        var minimal = await Run(converter.ToResult(file), services);
        var mvc = await Run(converter.ToActionResult(file), services);
        var fromStatic = await Run(file.ToResult(), services);
        var streamed = await Run(converter.ToResult(Response<FileResponse>.Success(new FileResponse(new MemoryStream(csv), "text/csv"))), services);

        foreach (var sent in new[] { minimal, mvc, fromStatic, streamed })
        {
            Assert.Equal(200, sent.Status);
            Assert.Equal("text/csv", sent.ContentType);
            Assert.Equal("id,name\n1,pen", sent.Body);
        }

        var download = Assert.IsType<FileContentResult>(converter.ToActionResult(file));
        Assert.Equal("products.csv", download.FileDownloadName);
    }

    [Fact]
    public void Results_a_handler_already_built_are_passed_through()
    {
        var (converter, _) = Build();
        var redirect = new RedirectResult("/elsewhere");

        Assert.Same(redirect, converter.ToActionResult(Response<IActionResult>.Success(redirect)));
        Assert.Same(redirect, Response<IActionResult>.Success(redirect).ToActionResult());
    }

    [Fact]
    public async Task Successes_without_body_and_204s_send_no_content()
    {
        var (converter, services) = Build(o => o.Format = ResponseBodyFormat.DataOrErrorList);

        Assert.Equal(204, (await Run(converter.ToResult(Response<ProductDto>.Success(Pen), includeBody: false), services)).Status);
        Assert.Equal(204, (await Run(converter.ToActionResult<string>(Response.NoContent()), services)).Status);
    }

    [Fact]
    public async Task Tasks_can_be_converted_directly()
    {
        var (converter, services) = Build(o => o.Format = ResponseBodyFormat.DataOrErrorList);
        var courier = services.GetRequiredService<ICourier>();

        var result = await Run(await converter.ToResult(courier.Dispatch(new Ping("hi"))), services);

        Assert.Equal("\"pong: hi\"", result.Body);
    }
}
