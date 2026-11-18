using System.Runtime.ExceptionServices;

namespace ElegantSeries.Flow.WPF.Tests.Windows;

/// <summary>
/// Runs test code on a dedicated STA thread, as required for creating WPF objects.
/// xUnit executes tests on MTA thread-pool threads, so any test that instantiates
/// WPF types must go through this helper.
/// </summary>
internal static class StaHelper
{
    private static readonly TimeSpan ThreadTimeout = TimeSpan.FromSeconds(60);

    public static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        // Background, so a wedged test cannot hold the test host open;
        // the bounded join below fails that one test instead.
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(ThreadTimeout))
        {
            throw new TimeoutException(
                $"The STA test thread did not finish within {ThreadTimeout.TotalSeconds:F0} seconds.");
        }

        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    public static T Run<T>(Func<T> func)
    {
        T? result = default;
        Run(() => { result = func(); });
        return result!;
    }
}
