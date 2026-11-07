using System.Reflection;
using ElegantSeries.Flow.Tests.Shared;

namespace ElegantSeries.Flow.WPF.Tests.Windows;

/// <summary>
/// The WPF half of the generated-layer reservation guard (see
/// <see cref="GeneratedLayerReservation"/>): the WPF assembly targets
/// net10.0-windows, so Core's test project cannot reference it; this
/// windows-leg test runs the same check for it here.
/// </summary>
public sealed class GeneratedNamespaceGuardTests
{
    [Fact]
    public void WpfAssembly_KeepsTheGeneratedLayerReserved()
    {
        var assembly = Assembly.Load("ElegantSeries.Flow.WPF");
        GeneratedLayerReservation.AssertReserved(assembly, "ElegantSeries.Flow.WPF");
    }
}
