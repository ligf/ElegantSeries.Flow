using System.Windows;
using ElegantSeries.Flow.Core.Navigation;

namespace ElegantSeries.Flow.WPF;

/// <summary>
/// Default <see cref="IViewLocator"/> implementation for WPF.
/// </summary>
/// <remarks>
/// A thin adapter over <c>ViewRegistry&lt;FrameworkElement&gt;</c>; all
/// registration and lookup logic lives there so it stays unit-testable
/// without a WPF runtime.
/// </remarks>
public sealed class ViewLocator : IViewLocator
{
    private readonly ViewRegistry<FrameworkElement> _registry = new();

    /// <inheritdoc />
    public void Register<TView, TViewModel>()
        where TView : FrameworkElement, new()
        where TViewModel : INavigationViewModel
        => _registry.Register<TViewModel>(_ => new TView());

    /// <inheritdoc />
    public void Register<TViewModel>(Func<TViewModel, FrameworkElement> viewFactory)
        where TViewModel : INavigationViewModel
        => _registry.Register(viewFactory);

    /// <inheritdoc />
    public FrameworkElement CreateView(INavigationViewModel viewModel)
        => _registry.CreateView(viewModel);

    /// <inheritdoc />
    public bool IsRegistered<TViewModel>()
        where TViewModel : INavigationViewModel
        => _registry.IsRegistered<TViewModel>();
}
