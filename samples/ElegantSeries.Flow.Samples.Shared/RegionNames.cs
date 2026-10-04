namespace ElegantSeries.Flow.Samples.Shared;

/// <summary>
/// Centralized region names for the samples. Regions are addressed by string
/// throughout the navigation API (the same as Prism's region names); keeping
/// the vocabulary in one place turns a typo from a silent runtime failure
/// into a compile-time error.
/// </summary>
public static class RegionNames
{
    public const string MainRegion = "MainRegion";
    public const string Sidebar = "Sidebar";
    public const string FailureRegion = "FailureRegion";
    public const string Q1 = "Q1";
    public const string Q2 = "Q2";
    public const string Q3 = "Q3";
    public const string Q4 = "Q4";
    public const string Q5 = "Q5";
    public const string Q6 = "Q6";
    public const string SwitchA = "SwitchA";
    public const string SwitchB = "SwitchB";
}
