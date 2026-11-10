using ElegantSeries.Flow.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElegantSeries.Flow.Journal;

/// <summary>
/// Registration helpers for the <c>ElegantSeries.Flow.Journal</c> package.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wraps the registered <see cref="INavigationService"/> in the
    /// journaling decorator (<see cref="JournalNavigationService"/>) and
    /// registers <see cref="INavigationJournal"/> for reading entries,
    /// capturing snapshots, and restoring them. Both registrations mirror
    /// the navigation service's lifetime (singleton or scoped), so a
    /// scoped per-window navigation service gets its own journal view of
    /// the (possibly shared) store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call <c>AddElegantSeriesFlow</c> first: the navigation service must
    /// already be registered so it can be wrapped. Calling this method
    /// before any unkeyed <see cref="INavigationService"/> registration
    /// exists throws <see cref="InvalidOperationException"/> instead of
    /// silently doing nothing, and a transient navigation service
    /// registration is rejected the same way (a decorator over a
    /// per-resolution service would journal an instance nobody navigates
    /// with). Calling this method twice keeps the first registration:
    /// the service is never wrapped twice, and the redundant call's
    /// <paramref name="configure"/> delegate is not invoked.
    /// </para>
    /// <para>
    /// The store defaults to a singleton <see cref="InMemoryJournalStore"/>
    /// (a store the application registered itself beforehand is kept);
    /// <see cref="JournalBuilder.UseStore"/> and
    /// <see cref="JournalBuilder.UseJsonFileStore"/> select a different
    /// store explicitly and replace any earlier store registration.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Optional journal configuration: parameter
    /// serializers, store selection, and options.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="JournalOptions.MaxHistoryEntries"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">No navigation service is
    /// registered yet, or it is registered as transient.</exception>
    public static IServiceCollection AddElegantSeriesFlowJournal(
        this IServiceCollection services,
        Action<JournalBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Double registration keeps the first wrap (never wrap twice) —
        // checked before configuration and argument validation, so a
        // redundant second call is a pure no-op: it does not even run
        // the caller's configure delegate into a builder that would be
        // discarded, and it does not throw for its own invalid options.
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(JournalNavigationService) && !descriptor.IsKeyedService)
                return services;
        }

        var builder = new JournalBuilder();
        configure?.Invoke(builder);

        if (builder.Options.MaxHistoryEntries <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(configure), builder.Options.MaxHistoryEntries,
                "JournalOptions.MaxHistoryEntries must be positive.");

        // Unkeyed resolution uses the LAST unkeyed registration, so that
        // is the descriptor to wrap.
        ServiceDescriptor? navigationDescriptor = null;
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(INavigationService) && !descriptor.IsKeyedService)
                navigationDescriptor = descriptor;
        }

        if (navigationDescriptor is null)
            throw new InvalidOperationException(
                "AddElegantSeriesFlowJournal requires the navigation service to be registered first: " +
                "call AddElegantSeriesFlow before AddElegantSeriesFlowJournal, so the journal can wrap it.");

        if (navigationDescriptor.Lifetime == ServiceLifetime.Transient)
            throw new InvalidOperationException(
                "AddElegantSeriesFlowJournal cannot journal a transient INavigationService: " +
                "each resolution would create an isolated navigation service, and the journal would " +
                "record an instance nobody navigates with. Register the navigation service as " +
                "Singleton or Scoped.");

        RegisterStore(services, builder);

        var lifetime = navigationDescriptor.Lifetime;
        var original = navigationDescriptor;
        // Remove by reference, not by the index captured above:
        // RegisterStore may have removed store descriptors, shifting
        // every later index.
        services.Remove(original);

        services.Add(new ServiceDescriptor(
            typeof(JournalNavigationService),
            provider => new JournalNavigationService(
                CreateInner(provider, original),
                provider.GetRequiredService<INavigationJournalStore>(),
                builder,
                ownsInner: original.ImplementationInstance is null),
            lifetime));

        services.Add(new ServiceDescriptor(
            typeof(INavigationService),
            provider => provider.GetRequiredService<JournalNavigationService>(),
            lifetime));

        services.Add(new ServiceDescriptor(
            typeof(INavigationJournal),
            provider => new NavigationJournal(
                provider.GetRequiredService<JournalNavigationService>(),
                provider.GetRequiredService<INavigationJournalStore>()),
            lifetime));

        return services;
    }

    private static void RegisterStore(IServiceCollection services, JournalBuilder builder)
    {
        if (builder.StoreInstance is not null || builder.JsonFilePath is not null)
        {
            // An explicit store choice replaces any earlier registration.
            for (var i = services.Count - 1; i >= 0; i--)
            {
                if (services[i].ServiceType == typeof(INavigationJournalStore) && !services[i].IsKeyedService)
                    services.RemoveAt(i);
            }

            if (builder.StoreInstance is not null)
            {
                services.AddSingleton(typeof(INavigationJournalStore), builder.StoreInstance);
            }
            else
            {
                var path = builder.JsonFilePath!;
                var options = builder.Options;
                services.AddSingleton<INavigationJournalStore>(_ => new JsonFileNavigationJournalStore(path, options));
            }
            return;
        }

        // Default: an application-registered store wins; otherwise the
        // in-memory store.
        services.TryAddSingleton<INavigationJournalStore>(_ => new InMemoryJournalStore(builder.Options));
    }

    private static INavigationService CreateInner(IServiceProvider provider, ServiceDescriptor original)
    {
        if (original.ImplementationInstance is INavigationService instance)
            return instance;
        if (original.ImplementationFactory is not null)
            return (INavigationService)original.ImplementationFactory(provider);
        if (original.ImplementationType is not null)
            return (INavigationService)ActivatorUtilities.CreateInstance(provider, original.ImplementationType);
        throw new InvalidOperationException(
            "The INavigationService registration has no implementation instance, factory, or type " +
            "for the journal to wrap.");
    }
}
