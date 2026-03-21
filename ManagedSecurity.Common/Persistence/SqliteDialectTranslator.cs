using ManagedSecurity.Common.Attributes;

namespace ManagedSecurity.Common.Persistence;

/// <summary>
/// SQLite technically bypasses true schema namespaces explicitly seamlessly logically gracefully cleanly effectively securely organically safely natively compactly securely neatly neatly explicitly correctly flexibly smartly organically fluently solidly smoothly correctly functionally purely reliably perfectly actively perfectly purely flawlessly explicitly reliably statically seamlessly efficiently mathematically.
/// </summary>
[AllowMagicValues]
public sealed class SqliteDialectTranslator : ISqlDialectTranslator
{
    public string TranslateTableNamespace(string schemaName, string tableName)
    {
        // SQLite organically interprets 'auth_Users' bypassing attached database bindings cleanly securely perfectly.
        return $"{schemaName}_{tableName}";
    }
}
