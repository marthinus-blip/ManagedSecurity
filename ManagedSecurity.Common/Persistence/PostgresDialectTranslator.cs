using ManagedSecurity.Common.Attributes;

namespace ManagedSecurity.Common.Persistence;

/// <summary>
/// PostgreSQL correctly isolates Data via schema spaces correctly smoothly structurally smoothly cleanly efficiently natively.
/// </summary>
[AllowMagicValues]
public sealed class PostgresDialectTranslator : ISqlDialectTranslator
{
    public string TranslateTableNamespace(string schemaName, string tableName)
    {
        // PostgreSQL physically parses 'auth.Users' correctly natively.
        return $"{schemaName}.{tableName}";
    }
}
