using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace ElegantSeries.Flow.Navigation;

/// <summary>
/// <see cref="NavigationService"/> — target recognition: deciding which
/// existing instance, if any, an incoming forward navigation is addressed
/// to when several instances of the same page type may exist.
/// </summary>
public sealed partial class NavigationService
{
    /// <summary>The verdict of classifying one existing entry against an incoming navigation.</summary>
    private enum RecognitionVerdict
    {
        /// <summary>The entry is the navigation's target.</summary>
        Recognized,

        /// <summary>The entry is not the target.</summary>
        Rejected,

        /// <summary>
        /// The entry was rejected by parameter comparison, and the incoming
        /// parameter's type provides no value equality, so parameter-based
        /// recognition can never succeed for it. Eligible for the one-time
        /// diagnostic (see <see cref="ReportUnmatchableParameterOnce"/>).
        /// </summary>
        RejectedByUnmatchableParameter,
    }

    /// <summary>Value-equality verdicts by parameter type; type metadata is stable for the process.</summary>
    private static readonly ConcurrentDictionary<Type, bool> s_valueEqualityByType = new();

    /// <summary>
    /// Parameter types for which the unmatchable-parameter diagnostic has
    /// already been reported by this service instance. Guarded by
    /// <see cref="_stateLock"/>.
    /// </summary>
    private readonly HashSet<Type> _reportedUnmatchableParameterTypes = [];

    /// <summary>
    /// Classifies one candidate entry against an incoming navigation
    /// parameter. A ViewModel implementing
    /// <see cref="INavigationTargetAware"/> has the final say for its own
    /// entry; any other candidate is compared by the parameter it was last
    /// activated with. Exceptions thrown by the vote or by a parameter's
    /// <see cref="object.Equals(object)"/> are collected into
    /// <paramref name="classificationErrors"/> (the caller reports them once
    /// no locks are held) and the candidate counts as not recognized.
    /// This method invokes user code and must not be called while holding
    /// <see cref="_stateLock"/>.
    /// </summary>
    private static RecognitionVerdict ClassifyCandidate(
        NavigationEntry candidate, object? parameter, List<Exception> classificationErrors)
    {
        if (candidate.ViewModel is INavigationTargetAware aware)
        {
            try
            {
                return aware.IsNavigationTarget(parameter)
                    ? RecognitionVerdict.Recognized
                    : RecognitionVerdict.Rejected;
            }
            catch (Exception ex)
            {
                classificationErrors.Add(ex);
                return RecognitionVerdict.Rejected;
            }
        }

        bool equal;
        try
        {
            equal = object.Equals(candidate.Parameter, parameter);
        }
        catch (Exception ex)
        {
            classificationErrors.Add(ex);
            return RecognitionVerdict.Rejected;
        }

        if (equal)
        {
            return RecognitionVerdict.Recognized;
        }

        return parameter is not null && !HasValueEquality(parameter.GetType())
            ? RecognitionVerdict.RejectedByUnmatchableParameter
            : RecognitionVerdict.Rejected;
    }

    /// <summary>
    /// Finds the entry an incoming push navigation is addressed to within a
    /// stack snapshot (top first), or <see langword="null"/> when no entry
    /// qualifies and the caller must push the resolved instance. An exact
    /// instance match is identity and always wins; the remaining candidates
    /// are the same ones the former type fallback considered —
    /// <see cref="NavigationRetention.KeepAlive"/> entries whose ViewModel
    /// has the resolved instance's runtime type — and each must be
    /// recognized through <see cref="ClassifyCandidate"/>. Invokes user
    /// code; the caller must not hold <see cref="_stateLock"/>.
    /// </summary>
    private static NavigationEntry? ClassifyExistingEntry(
        List<NavigationEntry> snapshotTopFirst,
        INavigationViewModel targetVm,
        object? parameter,
        List<Exception> classificationErrors,
        ref bool unmatchableRejection)
    {
        foreach (var entry in snapshotTopFirst)
        {
            if (ReferenceEquals(entry.ViewModel, targetVm))
            {
                return entry;
            }
        }

        var targetType = targetVm.GetType();
        foreach (var entry in snapshotTopFirst)
        {
            if (entry.Retention != NavigationRetention.KeepAlive
                || entry.ViewModel.GetType() != targetType)
            {
                continue;
            }

            var verdict = ClassifyCandidate(entry, parameter, classificationErrors);
            if (verdict == RecognitionVerdict.Recognized)
            {
                return entry;
            }

            if (verdict == RecognitionVerdict.RejectedByUnmatchableParameter)
            {
                unmatchableRejection = true;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="type"/> provides value equality usable for
    /// parameter recognition through
    /// <see cref="object.Equals(object, object)"/>: a value type, or a
    /// reference type overriding <see cref="object.Equals(object)"/>
    /// (records synthesize one; <see cref="string"/> is covered by the same
    /// branch). Implementing <see cref="IEquatable{T}"/> alone does not
    /// count: without an <see cref="object.Equals(object)"/> override it
    /// never participates in <see cref="object.Equals(object, object)"/>.
    /// Used only to decide whether the unmatchable-parameter diagnostic
    /// applies, so an approximate answer under trimming is acceptable.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "The queried types are the runtime types of live parameter objects the " +
            "application itself created and passed to navigation, so their metadata is rooted by " +
            "the application's own usage. The answer feeds only an advisory diagnostic; under " +
            "aggressive trimming a wrong answer degrades that diagnostic, never recognition itself.")]
    private static bool HasValueEquality(Type type) =>
        s_valueEqualityByType.GetOrAdd(type, static t =>
            t.IsValueType
            || t.GetMethod(nameof(object.Equals), [typeof(object)])?.DeclaringType != typeof(object));

    /// <summary>
    /// Reports — once per parameter type per service instance — that an
    /// incoming parameter cannot take part in parameter-based recognition
    /// because its type provides no value equality, so existing pages were
    /// passed over and a new instance is being created instead. Without
    /// this report the degradation is silent: the navigation succeeds, but
    /// no existing instance is ever recognized for such parameters.
    /// </summary>
    private void ReportUnmatchableParameterOnce(Type? parameterType, Type viewModelType)
    {
        if (parameterType is null)
        {
            return;
        }

        bool shouldReport;
        using (_stateLock.EnterScope())
        {
            shouldReport = _reportedUnmatchableParameterTypes.Add(parameterType);
        }

        if (!shouldReport)
        {
            return;
        }

        ReportSubscriberError(new InvalidOperationException(
            $"Navigation parameter of type '{parameterType.FullName}' does not provide value equality, " +
            $"so existing '{viewModelType.FullName}' pages cannot be recognized by parameter. When no " +
            "instance claims such a navigation, a new instance is created instead of reusing one. " +
            "To enable recognition, pass a value-equal parameter (a record, a value type, or a type " +
            "overriding Equals), or implement INavigationTargetAware on the ViewModel. " +
            "This is reported once per parameter type."));
    }
}
