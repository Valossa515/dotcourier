using DotCourier.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCourier;

/// <summary>Adds EF Core transactions to a DotCourier pipeline.</summary>
public static class CourierEntityFrameworkCoreExtensions
{
    /// <summary>
    /// Runs every command (<see cref="ICommand{TResponse}"/>, plus requests marked
    /// <see cref="TransactionalAttribute"/>) in a <typeparamref name="TDbContext"/> transaction:
    /// committed when the handler succeeds, rolled back when it throws or returns an error
    /// <see cref="Response{T}"/>. Queries are not wrapped. Registers <see cref="ICourierTransaction"/>
    /// for after-commit side effects. Register the context itself with <c>AddDbContext</c> as usual.
    /// </summary>
    /// <remarks>Call it once: transactions cover one context type per application.</remarks>
    public static CourierConfiguration AddEntityFrameworkTransactions<TDbContext>(
        this CourierConfiguration configuration,
        Action<CourierTransactionOptions>? configure = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new CourierTransactionOptions();
        configure?.Invoke(options);

        return configuration
            .ConfigureServices(services =>
            {
                services.RemoveAll<TransactionSettings>();
                services.AddSingleton(new TransactionSettings(typeof(TDbContext), options));
                services.TryAddScoped<CourierTransactionScope>();
                services.TryAddScoped<ICourierTransaction>(sp => sp.GetRequiredService<CourierTransactionScope>());
            })
            .AddOpenBehavior(typeof(TransactionBehavior<,>), BehaviorOrder.Transaction);
    }
}
