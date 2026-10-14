namespace DotCourier.Internal;

/// <summary>
/// Flags an exception whose stack trace was already logged, so an unexpected failure produces
/// one error entry even when it is seen by both the logging behavior and <c>Dispatch</c>.
/// </summary>
internal static class ExceptionLogMarker
{
    private const string Key = "DotCourier.Logged";

    public static void Mark(Exception exception)
    {
        try
        {
            exception.Data[Key] = true;
        }
        catch (Exception)
        {
            // Some exception types expose a read-only Data dictionary; worst case is a duplicate log.
        }
    }

    public static bool IsMarked(Exception exception)
    {
        try
        {
            return exception.Data[Key] is true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
