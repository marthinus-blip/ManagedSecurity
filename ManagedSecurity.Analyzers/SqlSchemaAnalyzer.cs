using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ManagedSecurity.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class SqlSchemaAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "MSG002";
        private const string Title = "Unbounded SQL Reference Detected";
        private const string MessageFormat = "SQL Table reference '{0}' lacks schema boundaries. All SQL objects MUST be referenced utilizing explicit domain constants (e.g. '{{SchemaNameQl}}.{{TableNameQl}}') or physical schema borders natively.";
        private const string Category = "Governance";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        // Natively scans for raw single-word table names succeeding explicit SQL execution statements organically cleanly.
        // It strictly demands architectural bounds ('.', '_', or Interpolation holes '}') functionally mathematically seamlessly [ESC-OPT].
        // Utilizes atomic grouping '?>' to permanently prevent backtracking on IF NOT EXISTS clauses.
        private static readonly Regex SqlUnboundRegex = new Regex(
            @"(?i)\b(?>(?:TABLE(?:\s+IF\s+NOT\s+EXISTS)?|INTO|UPDATE|FROM|JOIN))\s+(?!IF\b|SET\b|SKIP\b|UnlockedJob\b)([a-zA-Z0-9]+)\s*(?:[\(\n\r;, ]|$)",
            RegexOptions.Compiled);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(AnalyzeStringNodes, SyntaxKind.StringLiteralExpression, SyntaxKind.InterpolatedStringText);
        }

        private void AnalyzeStringNodes(SyntaxNodeAnalysisContext context)
        {
            var node = context.Node;
            string textToAnalyze = string.Empty;

            if (node is LiteralExpressionSyntax literal)
            {
                textToAnalyze = literal.Token.ValueText;
            }
            else if (node is InterpolatedStringTextSyntax interpolatedText)
            {
                textToAnalyze = interpolatedText.TextToken.ValueText;
            }

            if (string.IsNullOrWhiteSpace(textToAnalyze))
                return;

            // Physical optimization: Only run complex geometric regexes if the chunk organically resembles SQL instructions natively [INSC-OPT].
            if (!Regex.IsMatch(textToAnalyze, @"(?i)\b(?:SELECT|UPDATE|INSERT|DELETE|CREATE|ALTER|WITH)\b"))
                return;

            var match = SqlUnboundRegex.Match(textToAnalyze);
            if (match.Success)
            {
                // Capture Group 1 precisely isolates the illegitimate magic table name natively cleanly!
                var illegalTableName = match.Groups[1].Value;

                var diagnostic = Diagnostic.Create(Rule, node.GetLocation(), illegalTableName);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }
}
