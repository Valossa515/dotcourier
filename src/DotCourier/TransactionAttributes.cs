namespace DotCourier;

/// <summary>
/// Runs the annotated request inside a database transaction when a transaction package is enabled
/// (e.g. <c>DotCourier.EntityFrameworkCore</c>). Commands are transactional by default; use this
/// for other request types, such as a plain <see cref="IRequest{TResponse}"/> that writes.
/// </summary>
/// <remarks>Lives in the core package so request contracts can be annotated without a database dependency.</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true)]
public sealed class TransactionalAttribute : Attribute;

/// <summary>
/// Keeps the annotated command out of the automatic transaction, e.g. a command that only calls an
/// external service and should not hold a database transaction open while it waits.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true)]
public sealed class NotTransactionalAttribute : Attribute;
