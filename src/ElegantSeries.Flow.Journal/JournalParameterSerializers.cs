namespace ElegantSeries.Flow.Journal;

/// <summary>
/// One registered parameter serializer pair, type-erased for lookup by
/// parameter type or by assembly-qualified type name.
/// </summary>
internal sealed class JournalParameterSerializer
{
    public required Type ParameterType { get; init; }
    public required Func<object, string> Serialize { get; init; }
    public required Func<string, object?> Deserialize { get; init; }
}

/// <summary>
/// The parameter serializers registered through <see cref="JournalBuilder"/>.
/// A parameter type with no registered serializer is journaled with a null
/// snapshot and restores as a null parameter; snapshots that carry a
/// serialized parameter for an unregistered type cannot be materialized.
/// </summary>
internal sealed class JournalParameterSerializers
{
    private readonly Dictionary<Type, JournalParameterSerializer> _byType = new();
    private readonly Dictionary<string, JournalParameterSerializer> _byName = new(StringComparer.Ordinal);

    public void Add<TParameter>(Func<TParameter, string> serialize, Func<string, TParameter> deserialize)
    {
        var entry = new JournalParameterSerializer
        {
            ParameterType = typeof(TParameter),
            Serialize = value => serialize((TParameter)value),
            Deserialize = text => deserialize(text),
        };
        _byType[typeof(TParameter)] = entry;
        _byName[typeof(TParameter).AssemblyQualifiedName!] = entry;
    }

    /// <summary>
    /// Serializes a parameter with its registered serializer, if any.
    /// Returns false when no serializer is registered for the type.
    /// Serializer exceptions propagate to the caller.
    /// </summary>
    public bool TrySerialize(object parameter, out string? snapshot)
    {
        if (_byType.TryGetValue(parameter.GetType(), out var entry))
        {
            snapshot = entry.Serialize(parameter);
            return true;
        }
        snapshot = null;
        return false;
    }

    /// <summary>
    /// Materializes a serialized parameter for the type named by its
    /// assembly-qualified name. Returns false when no deserializer is
    /// registered under that name. Deserializer exceptions propagate to
    /// the caller.
    /// </summary>
    public bool TryDeserialize(string parameterTypeName, string snapshot, out object? parameter)
    {
        if (_byName.TryGetValue(parameterTypeName, out var entry))
        {
            parameter = entry.Deserialize(snapshot);
            return true;
        }
        parameter = null;
        return false;
    }
}
