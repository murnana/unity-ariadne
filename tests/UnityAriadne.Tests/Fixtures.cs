using Xunit;

namespace UnityAriadne.Tests;

/// <summary>
/// Locates the external fixture directory. The fixtures are third-party Unity content and are
/// not distributed with this repository: point <c>ARIADNE_FIXTURES</c> at a directory holding
/// <c>SampleProject/</c>, <c>Addressables/</c> and <c>unity-select-dependencies.txt</c>
/// (see docs/development/fixtures.md). Tests marked <see cref="FixtureFactAttribute"/> skip
/// when it is not set.
/// </summary>
public static class Fixtures
{
    public const string EnvironmentVariable = "ARIADNE_FIXTURES";

    public static string? Root { get; } = FindRoot();

    public static bool IsAvailable => Root is not null;

    public static string SampleProject => Path.Combine(RequireRoot(), "SampleProject");

    /// <summary>Real Unity 6000.0 + Addressables 2.3.16 output — group assets, settings and an
    /// AssetReference smoke scene, laid out relative to Assets/.</summary>
    public static string Addressables => Path.Combine(RequireRoot(), "Addressables");

    public static string UnitySelectDependencies => Path.Combine(RequireRoot(), "unity-select-dependencies.txt");

    /// <summary>True when the fixture project has been opened in Unity locally (never true in CI).</summary>
    public static bool HasPackageCache =>
        IsAvailable && Directory.Exists(Path.Combine(SampleProject, "Library", "PackageCache"));

    private static string RequireRoot() =>
        Root ?? throw new InvalidOperationException(EnvironmentVariable + " is not set; mark the test [FixtureFact].");

    private static string? FindRoot()
    {
        var path = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return !string.IsNullOrEmpty(path) && Directory.Exists(Path.Combine(path, "SampleProject"))
            ? Path.GetFullPath(path)
            : null;
    }
}

/// <summary>A fact that needs the external fixtures; skipped when <c>ARIADNE_FIXTURES</c> is not set.</summary>
public sealed class FixtureFactAttribute : FactAttribute
{
    public FixtureFactAttribute()
    {
        if (!Fixtures.IsAvailable)
        {
            Skip = Fixtures.EnvironmentVariable + " is not set";
        }
    }
}

/// <summary>A theory that needs the external fixtures; skipped when <c>ARIADNE_FIXTURES</c> is not set.</summary>
public sealed class FixtureTheoryAttribute : TheoryAttribute
{
    public FixtureTheoryAttribute()
    {
        if (!Fixtures.IsAvailable)
        {
            Skip = Fixtures.EnvironmentVariable + " is not set";
        }
    }
}
