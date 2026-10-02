namespace ElegantSeries.Flow.Avalonia.Tests;

public sealed class AvaloniaDispatcherTests
{
    [Fact]
    public void Constructor_NullDispatcher_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new AvaloniaDispatcher(null!));
    }

    [Fact]
    public void ParameterlessConstructor_WithoutRunningApplication_FailsFast()
    {
        // Dispatcher.UIThread only exists inside a running Avalonia application.
        // Headless, the parameterless constructor must fail fast with a clear
        // error instead of a NullReferenceException; inside a real app it succeeds.
        var ex = Record.Exception(() => new AvaloniaDispatcher());
        Assert.True(
            ex is null || ex is InvalidOperationException,
            $"Expected success or InvalidOperationException, got {ex?.GetType().Name}.");
    }
}
