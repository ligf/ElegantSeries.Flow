# Changelog

All notable changes to ElegantSeries.Flow are recorded here, starting with
1.0.0-rc.3. While the library is in the release-candidate phase, a release may
contain breaking changes; every breaking change is listed with the migration
it requires.

## 1.0.0-rc.3

Changes since 1.0.0-rc.2.

### Breaking changes

- **`NavigationMode` was split into `NavigationOperation` and
  `NavigationRetention`.** The `NavigationMode` enum no longer exists. The
  `NavigateToAsync` methods now take an `operation` parameter and a
  `retention` parameter (defaults: `NavigationOperation.Push` and
  `NavigationRetention.Destroy`). Each old mode decomposes as follows:

  | `NavigationMode` | `operation` | `retention` |
  | --- | --- | --- |
  | `New` | `Push` | `Destroy` |
  | `KeepAlive` | `Push` | `KeepAlive` |
  | `Replace` | `Replace` | `Destroy` |
  | `ClearStack` | `Reset` | `Destroy` |

  Related renames: `INavigationService.GetCurrentMode` was replaced by
  `GetCurrentRetention`, and `NavigationGuardContext.Mode` was replaced by
  the pair `Operation` / `Retention` (both `null` for back navigation).

- **`[ViewFor(typeof(TViewModel))]` was removed; use
  `[ViewFor<TViewModel>]`.** The `ViewForAttribute(Type)` constructor and the
  `ViewModelType` property no longer exist. A View that names its ViewModel
  explicitly uses the new generic `ViewForAttribute<TViewModel>`; the
  parameterless `[ViewFor]` form, whose ViewModel is inferred from the View's
  `BaseView<TViewModel>` base class, is unchanged.

- **Generated view registrations moved to a per-assembly namespace.** In
  1.0.0-rc.2 the source generator emitted `ViewForRegistrations` into the
  flat `ElegantSeries.Flow.Generated` namespace. It now emits into
  `ElegantSeries.Flow.Generated.<sanitized assembly name>`, where
  sanitization keeps the assembly name's dots as segment separators and,
  within each segment, replaces every character that is not a letter, digit
  or `_` with `_` (a segment starting with a digit gets a `_` prefix, a
  segment equal to a reserved C# keyword gets a trailing `_`, and an empty
  segment becomes `_`). Update `using` directives and fully qualified calls
  accordingly: an assembly named `TunnelInspect.Preprocess`, for example,
  now generates into
  `ElegantSeries.Flow.Generated.TunnelInspect.Preprocess`. An assembly can
  replace the derived namespace in full with
  `[assembly: ViewForGeneratedNamespace("...")]`.
