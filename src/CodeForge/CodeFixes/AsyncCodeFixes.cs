using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;

namespace CodeForge
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SyncOverAsyncCodeFix)), Shared]
    public sealed class SyncOverAsyncCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("CF0002");

        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var node = root?.FindNode(context.Diagnostics[0].Location.SourceSpan, getInnermostNodeForTie: true);
            if (node == null)
            {
                return;
            }

            var replacement = ComputeReplacement(node);
            if (replacement == null || !IsInAsyncContext(node))
            {
                return;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Await instead of blocking",
                    ct => Apply(context.Document, replacement.Value.Target, replacement.Value.Replacement, ct),
                    nameof(SyncOverAsyncCodeFix)),
                context.Diagnostics);
        }

        private static (SyntaxNode Target, SyntaxNode Replacement)? ComputeReplacement(SyntaxNode node)
        {
            var member = node.AncestorsAndSelf().OfType<MemberAccessExpressionSyntax>().FirstOrDefault(m => m.Name.Identifier.Text == "Result");
            if (member != null && node.Span.Contains(member.Span) || node is MemberAccessExpressionSyntax { Name.Identifier.Text: "Result" })
            {
                var access = node as MemberAccessExpressionSyntax ?? member!;
                var awaitExpression = SyntaxFactory.AwaitExpression(access.Expression.WithoutTrivia()).WithTriviaFrom(access);
                return (access, WrapIfNeeded(access, awaitExpression));
            }

            if (node is InvocationExpressionSyntax invocation && invocation.Expression is MemberAccessExpressionSyntax call)
            {
                if (call.Name.Identifier.Text == "Wait" && invocation.ArgumentList.Arguments.Count == 0)
                {
                    var awaitExpression = SyntaxFactory.AwaitExpression(call.Expression.WithoutTrivia()).WithTriviaFrom(invocation);
                    return (invocation, WrapIfNeeded(invocation, awaitExpression));
                }

                if (call.Name.Identifier.Text == "GetResult" && call.Expression is InvocationExpressionSyntax getAwaiter
                    && getAwaiter.Expression is MemberAccessExpressionSyntax getAwaiterAccess && getAwaiterAccess.Name.Identifier.Text == "GetAwaiter")
                {
                    var awaitExpression = SyntaxFactory.AwaitExpression(getAwaiterAccess.Expression.WithoutTrivia()).WithTriviaFrom(invocation);
                    return (invocation, WrapIfNeeded(invocation, awaitExpression));
                }
            }

            return null;
        }

        private static SyntaxNode WrapIfNeeded(SyntaxNode original, AwaitExpressionSyntax awaitExpression)
        {
            var standalone = original.Parent is EqualsValueClauseSyntax
                || original.Parent is ReturnStatementSyntax
                || original.Parent is ArgumentSyntax
                || original.Parent is ExpressionStatementSyntax
                || original.Parent is ArrowExpressionClauseSyntax
                || original.Parent is AssignmentExpressionSyntax assignment && assignment.Right == original;
            return standalone ? (SyntaxNode)awaitExpression : SyntaxFactory.ParenthesizedExpression(awaitExpression);
        }

        private static bool IsInAsyncContext(SyntaxNode node)
        {
            foreach (var ancestor in node.Ancestors())
            {
                switch (ancestor)
                {
                    case MethodDeclarationSyntax method:
                        return method.Modifiers.Any(SyntaxKind.AsyncKeyword);
                    case LocalFunctionStatementSyntax local:
                        return local.Modifiers.Any(SyntaxKind.AsyncKeyword);
                    case AnonymousFunctionExpressionSyntax lambda:
                        return lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
                }
            }

            return false;
        }

        private static async Task<Document> Apply(Document document, SyntaxNode target, SyntaxNode replacement, CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            return document.WithSyntaxRoot(root!.ReplaceNode(target, replacement));
        }
    }

    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AsyncVoidCodeFix)), Shared]
    public sealed class AsyncVoidCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("CF0005");

        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var method = root?.FindNode(context.Diagnostics[0].Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (method == null)
            {
                return;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Return Task instead of void",
                    ct => ChangeReturnType(context.Document, method, ct),
                    nameof(AsyncVoidCodeFix)),
                context.Diagnostics);
        }

        private static async Task<Document> ChangeReturnType(Document document, MethodDeclarationSyntax method, CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            var task = SyntaxFactory.ParseTypeName("global::System.Threading.Tasks.Task").WithTriviaFrom(method.ReturnType).WithAdditionalAnnotations(Simplifier.Annotation);
            return document.WithSyntaxRoot(root!.ReplaceNode(method, method.WithReturnType(task)));
        }
    }

    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingSleepCodeFix)), Shared]
    public sealed class BlockingSleepCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("CF0006");

        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var invocation = root?.FindNode(context.Diagnostics[0].Location.SourceSpan).AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
            if (invocation == null)
            {
                return;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Use await Task.Delay",
                    ct => Apply(context.Document, invocation, ct),
                    nameof(BlockingSleepCodeFix)),
                context.Diagnostics);
        }

        private static async Task<Document> Apply(Document document, InvocationExpressionSyntax invocation, CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            var delay = SyntaxFactory.InvocationExpression(
                SyntaxFactory.ParseExpression("global::System.Threading.Tasks.Task.Delay").WithAdditionalAnnotations(Simplifier.Annotation),
                invocation.ArgumentList);
            var awaited = SyntaxFactory.AwaitExpression(delay).WithTriviaFrom(invocation).WithAdditionalAnnotations(Simplifier.Annotation);
            return document.WithSyntaxRoot(root!.ReplaceNode(invocation, awaited));
        }
    }
}
