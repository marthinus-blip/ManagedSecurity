namespace ManagedSecurity.Common.Persistence;

/// <summary>
/// A Zero-Allocation dialect tokenizer mapping agnostic SQL definitions natively securely efficiently explicitly smoothly gracefully.
/// Eliminates the necessity of duplicate Data Provider logic across SQLite and PostgreSQL natively correctly [INSC-OPT].
/// </summary>
public interface ISqlDialectTranslator
{
    /// <summary>
    /// Translates raw "[SCHEMA].[TABLE]" tokenized interpolations specifically securely mapped dynamically organically natively.
    /// </summary>
    string TranslateTableNamespace(string schemaName, string tableName);
}
