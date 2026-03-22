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
    public class StandardOutAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "MSG003";
        private const string Title = "Standard Out Detected";
        private const string MessageFormat = "Direct use of Console.{0} is prohibited. Use SentinelLogger generated extensions for zero-allocation telemetry.";
        private const string Category = "Governance";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        }

        private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                if (memberAccess.Expression is IdentifierNameSyntax identifier && identifier.Identifier.Text == "Console")
                {
                    string methodName = memberAccess.Name.Identifier.Text;
                    if (methodName == "Write" || methodName == "WriteLine")
                    {
                        var diagnostic = Diagnostic.Create(Rule, memberAccess.GetLocation(), methodName);
                        context.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }
    }
}
