using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ManagedSecurity.Analyzers
{
#pragma warning disable MSG001 // [ARTISTIC_LICENSE]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class MagicValueAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "MSG001";
        private const string Title = "Magic Value Detected";
        private const string MessageFormat = "Magic string/numeric literal '{0}' is prohibited by governance. Use 'nameof()', standard consts, or abstract it via configuration.";
        private const string Category = "Governance";

        private const string TargetAttributeName = "AllowMagicValues";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // Register an action to run on Strings and Numeric Literals
            context.RegisterSyntaxNodeAction(AnalyzeLiteral, SyntaxKind.StringLiteralExpression, SyntaxKind.NumericLiteralExpression, SyntaxKind.InterpolatedStringExpression);
        }

        private void AnalyzeLiteral(SyntaxNodeAnalysisContext context)
        {
            var node = context.Node;

            // Null or empty strings are generally safe exceptions in default behaviors
            if (node is LiteralExpressionSyntax literal)
            {
                if (literal.Token.ValueText.Length == 0) return;
                
                // Exempt explicit numeric limits like 0, 1, or -1 usually needed for iterations/defaults
                if (literal.Token.Value is int iVal && (iVal == 0 || iVal == 1 || iVal == -1)) return;
            }

            // Let's traverse up the Syntax Tree to find if it belongs to a Method/Constructor
            var methodBlock = node.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
            
            // Only enforce inside method bodies (excluding field initializers at the Class scope to allow 'const string' declarations!)
            if (methodBlock == null)
                return;

            // Check if method is decorated with [AllowMagicValues]
            if (methodBlock.AttributeLists.Any(attrList => attrList.Attributes.Any(attr => attr.Name.ToString().Contains(TargetAttributeName))))
                return;

            // Also check if ANY enclosing class/struct/record has [AllowMagicValues]
            if (node.Ancestors().OfType<TypeDeclarationSyntax>().Any(t => 
                t.AttributeLists.Any(attrList => 
                    attrList.Attributes.Any(a => a.Name.ToString().Contains(TargetAttributeName)))))
                return;

            var diagnostic = Diagnostic.Create(Rule, node.GetLocation(), node.ToString());
            context.ReportDiagnostic(diagnostic);
        }
    }
}
