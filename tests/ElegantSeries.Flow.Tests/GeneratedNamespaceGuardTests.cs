using System.Reflection;
using ElegantSeries.Flow.Tests.Shared;

namespace ElegantSeries.Flow.Tests;

/// <summary>
/// Enforces the generated-layer reservation (see
/// <see cref="GeneratedLayerReservation"/>) for every library assembly
/// this project can inspect, and keeps the guard's assembly list in sync
/// with the solution. Assemblies are read through a
/// <see cref="MetadataLoadContext"/> so the check is metadata-only: it
/// never executes library code and works for every target framework.
/// The WPF assembly is covered by the matching guard in the WPF test
/// project's windows leg — a net10.0 project cannot reference its
/// net10.0-windows project.
/// </summary>
public sealed class GeneratedNamespaceGuardTests
{
    [Fact]
    public void LibraryAssemblies_KeepTheGeneratedLayerReserved()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var assemblyPaths = Directory.GetFiles(baseDirectory, "*.dll");
        var resolver = new PathAssemblyResolver(
            assemblyPaths.Concat(GetTrustedPlatformAssemblyPaths()));
        using var context = new MetadataLoadContext(resolver);

        foreach (var assemblyName in GeneratedLayerReservation.AllLibraryAssemblies)
        {
            if (assemblyName == "ElegantSeries.Flow.WPF")
            {
                // Covered by the WPF test project's windows leg instead.
                continue;
            }

            var path = Path.Combine(baseDirectory, assemblyName + ".dll");
            Assert.True(
                File.Exists(path),
                $"Library assembly '{assemblyName}' was not found in the test output. "
                + "If a library project was renamed, update AllLibraryAssemblies in "
                + "tests/Shared/GeneratedLayerReservation.cs and this project's "
                + "references with it.");

            var assembly = context.LoadFromAssemblyPath(path);
            GeneratedLayerReservation.AssertReserved(assembly, assemblyName);
        }
    }

    [Fact]
    public void GuardList_CoversEverySrcProject()
    {
        var solution = FindSolutionFile();
        var srcProjects = File.ReadAllText(solution)
            .Split('"')
            .Where(part => part.StartsWith("src/", StringComparison.Ordinal) &&
                           part.EndsWith(".csproj", StringComparison.Ordinal))
            .Select(part => Path.GetFileNameWithoutExtension(part))
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            srcProjects.SetEquals(GeneratedLayerReservation.AllLibraryAssemblies),
            "The library projects under src/ in ElegantSeries.Flow.slnx no longer "
            + "match AllLibraryAssemblies in tests/Shared/GeneratedLayerReservation.cs. "
            + "For a new library project: add its assembly name to that list and a "
            + "ProjectReference to it in ElegantSeries.Flow.Tests.csproj (or, for a "
            + "target framework this project cannot reference, a guard in that "
            + "project's own test project like the WPF one), so the generated-layer "
            + "reservation is checked for it too.");
    }

    private static IEnumerable<string> GetTrustedPlatformAssemblyPaths()
    {
        var trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        return trusted is null
            ? []
            : trusted.Split(Path.PathSeparator).Where(path => path.Length > 0);
    }

    private static string FindSolutionFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "ElegantSeries.Flow.slnx");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "ElegantSeries.Flow.slnx was not found above the test output directory; "
            + "the guard-list completeness check needs the repository layout.");
    }
}
