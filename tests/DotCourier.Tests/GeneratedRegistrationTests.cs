using DotCourier.Generated;
using DotCourier.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace DotCourier.Tests;

/// <summary>
/// This assembly is built with DotCourier.SourceGenerator, so every test runs on the generated
/// registrations. These tests pin that the generated path is the one in use and that it registers
/// exactly what the reflection scan registers, in the same order.
/// </summary>
public class GeneratedRegistrationTests
{
    private static ServiceCollection Register(bool generated)
    {
        var services = new ServiceCollection();
        services.AddCourier(cfg =>
        {
            cfg.UseGeneratedRegistrations = generated;
            cfg.RegisterServicesFromAssemblyContaining<CallLog>();
        });
        return services;
    }

    private static string[] Describe(IServiceCollection services) =>
        services.Select(d => $"{d.ServiceType} -> {d.ImplementationType?.ToString() ?? (d.ImplementationFactory is null ? "instance" : "factory")} ({d.Lifetime})").ToArray();

    [Fact]
    public void This_assembly_has_generated_registrations()
    {
        Assert.NotNull(GeneratedCourierRegistry.Find(typeof(CallLog).Assembly));
        Assert.Null(GeneratedCourierRegistry.Find(typeof(ICourier).Assembly)); // built without the generator
    }

    [Fact]
    public void Generated_registrations_match_the_reflection_scan_in_content_and_order()
    {
        var generated = Register(generated: true);
        var scanned = Register(generated: false);

        Assert.Equal(Describe(scanned), Describe(generated));
        Assert.Equal(
            scanned.BuildServiceProvider().GetRequiredService<GenericRequestHandlerRegistry>().OpenHandlers,
            generated.BuildServiceProvider().GetRequiredService<GenericRequestHandlerRegistry>().OpenHandlers);
    }

    [Fact]
    public void Generated_registrations_create_the_dispatch_wrappers_up_front()
    {
        Register(generated: true);

        Assert.True(Courier.HasRequestWrapper(typeof(Ping)));
        Assert.True(Courier.HasStreamWrapper(typeof(Streaming.CountTo)));
        Assert.True(Courier.HasNotificationWrapper(typeof(ItemCreated)));
    }
}
