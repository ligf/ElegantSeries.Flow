using System.Windows;
using ElegantSeries.Flow.Core.Navigation;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.WPF.Locating;

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
    private readonly IServiceCollection? _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="ViewLocator"/> class.
    /// </summary>
    /// <remarks>
    /// A directly constructed locator has no access to a service collection,
    /// so <see cref="RegisterTransient{TView, TViewModel}"/> and
    /// <see cref="RegisterSingleton{TView, TViewModel}"/> throw
    /// <see cref="InvalidOperationException"/> on it. Prefer
    /// <c>AddFlowViews(configure)</c>, which wires the service collection
    /// automatically.
    /// </remarks>
    public ViewLocator()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ViewLocator"/> class with
    /// access to the service collection, enabling ViewModel lifetime
    /// registration. Used by <c>AddFlowViews(configure)</c>.
    /// </summary>
    /// <param name="services">The service collection ViewModel registrations are written to.</param>
    internal ViewLocator(IServiceCollection services)
    {
        _services = services;
    }

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
    public void RegisterTransient<TView, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>()
        where TView : FrameworkElement, new()
        where TViewModel : class, INavigationViewModel
    {
        EnsureServices(nameof(RegisterTransient));
        Register<TView, TViewModel>();
        _services!.AddTransient<TViewModel>();
    }

    /// <inheritdoc />
    public void RegisterSingleton<TView, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>()
        where TView : FrameworkElement, new()
        where TViewModel : class, INavigationViewModel
    {
        EnsureServices(nameof(RegisterSingleton));
        Register<TView, TViewModel>();
        _services!.AddSingleton<TViewModel>();
    }

    /// <inheritdoc />
    public FrameworkElement CreateView(INavigationViewModel viewModel)
        => _registry.CreateView(viewModel);

    /// <inheritdoc />
    public bool IsRegistered<TViewModel>()
        where TViewModel : INavigationViewModel
        => _registry.IsRegistered<TViewModel>();

    private void EnsureServices(string methodName)
    {
        if (_services is null)
        {
            throw new InvalidOperationException(
                $"{methodName} requires the locator to be created by AddFlowViews(configure), " +
                "which provides access to the service collection. A directly constructed ViewLocator " +
                "can only map views: use Register<TView, TViewModel>() and register the ViewModel " +
                "in dependency injection separately.");
        }
    }
}
