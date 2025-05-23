using HuaweiCloud.GaussDB.Internal.Postgres;

namespace HuaweiCloud.GaussDB.PostgresTypes;

/// <summary>
/// Represents a PostgreSQL domain type.
/// </summary>
/// <remarks>
/// See https://www.postgresql.org/docs/current/static/sql-createdomain.html.
///
/// When PostgreSQL returns a RowDescription for a domain type, the type OID is the base type's
/// (so fetching a domain type over text returns a RowDescription for text).
/// However, when a composite type is returned, the type OID there is that of the domain,
/// so we provide "clean" support for domain types.
/// </remarks>
public class PostgresDomainType : PostgresType
{
    /// <summary>
    /// The PostgreSQL data type of the base type, i.e. the type this domain is based on.
    /// </summary>
    public PostgresType BaseType { get; }

    /// <summary>
    /// <b>True</b> if the domain has a NOT NULL constraint, otherwise <b>false</b>.
    /// </summary>
    public bool NotNull { get; }

    /// <summary>
    /// Constructs a representation of a PostgreSQL domain data type.
    /// </summary>
    protected internal PostgresDomainType(string ns, string name, uint oid, PostgresType baseType, bool notNull)
        : base(ns, name, oid)
    {
        BaseType = baseType;
        NotNull = notNull;
    }

    /// <summary>
    /// Constructs a representation of a PostgreSQL domain data type.
    /// </summary>
    internal PostgresDomainType(DataTypeName dataTypeName, Oid oid, PostgresType baseType, bool notNull)
        : base(dataTypeName, oid)
    {
        BaseType = baseType;
        NotNull = notNull;
    }

    internal override PostgresFacets GetFacets(int typeModifier)
        => BaseType.GetFacets(typeModifier);
}
