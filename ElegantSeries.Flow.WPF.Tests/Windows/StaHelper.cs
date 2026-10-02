using System.Runtime.ExceptionServices;

namespace ElegantSeries.Flow.WPF.Tests.Windows;

/// <summary>
/// Runs test code on a dedicated STA thread, as required for creating WPF objects.
/// xUnit executes tests on MTA thread-pool threads, so any test that instantiates
/// WPF types must go through this helper.
/// </summary>
internal static class StaHelper
{
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
        thread.Start();
        thread.Join();

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
