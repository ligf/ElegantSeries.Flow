using ElegantSeries.Flow.Core.Navigation;
using ElegantSeries.Flow.Core.Locating;
using Avalonia.Controls;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace ElegantSeries.Flow.Avalonia.Locating;

/// <summary>
/// Default <see cref="IViewLocator"/> implementation for Avalonia.
/// </summary>
/// <remarks>
/// A thin adapter over <c>ViewRegistry&lt;Control&gt;</c> (in
/// <c>ElegantSeries.Flow.Core</c>); all registration and lookup logic lives
/// there so it stays unit-testable without an Avalonia runtime.
/// Zero reflection: registration stores a compiled delegate per ViewModel type
/// and lookup uses <see cref="object.GetType"/> only. Safe for Native AOT and
/// trimming.
/// </remarks>
public sealed class ViewLocator : IViewLocator
{
    private readonly ViewRegistry<Control> _registry = new();
    // Held for the application's lifetime (the locator is registered as a singleton).
    // Intentional: ViewModel registrations only happen during startup configuration,
    // before the service provider is built.
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
        where TView : Control, new()
        where TViewModel : INavigationViewModel
        => _registry.Register<TViewModel>(_ => new TView());

    /// <inheritdoc />
    public void RegisterTransient<TView, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>()
        where TView : Control, new()
        where TViewModel : class, INavigationViewModel
    {
        EnsureServices(nameof(RegisterTransient));
        Register<TView, TViewModel>();
        _services!.AddTransient<TViewModel>();
    }

    /// <inheritdoc />
    public void RegisterSingleton<TView, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>()
        where TView : Control, new()
        where TViewModel : class, INavigationViewModel
    {
        EnsureServices(nameof(RegisterSingleton));
        Register<TView, TViewModel>();
        _services!.AddSingleton<TViewModel>();
    }

    /// <inheritdoc />
    public void Register<TViewModel>(Func<TViewModel, Control> viewFactory)
        where TViewModel : INavigationViewModel
        => _registry.Register(viewFactory);

    /// <inheritdoc />
    public Control CreateView(INavigationViewModel viewModel)
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
