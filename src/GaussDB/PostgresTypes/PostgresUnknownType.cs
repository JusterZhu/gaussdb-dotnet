namespace HuaweiCloud.GaussDB.PostgresTypes;

/// <summary>
/// Represents a PostgreSQL data type that isn't known to GaussDB and cannot be handled.
/// </summary>
public sealed class UnknownBackendType : PostgresType
{
    internal static readonly PostgresType Instance = new UnknownBackendType();

    /// <summary>
    /// Constructs a the unknown backend type.
    /// </summary>
    UnknownBackendType() : base("", "<unknown>", 0) { }
}
