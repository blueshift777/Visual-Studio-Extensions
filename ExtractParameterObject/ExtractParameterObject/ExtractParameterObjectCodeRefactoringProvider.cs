using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ExtractParameterObject
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(ExtractParameterObjectCodeRefactoringProvider)), Shared]
    internal class ExtractParameterObjectCodeRefactoringProvider : CodeRefactoringProvider
    {
        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            // TODO: Replace the following code with your own analysis, generating a CodeAction for each refactoring to offer

            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            // Find the node at the selection.
            var node = root.FindNode(context.Span);

            // Only offer a refactoring if the selected node is a type declaration node.
            var methodDecl = node as MethodDeclarationSyntax;
            if (methodDecl == null)
            {
                return;
            }

            // For any type declaration node, create a code action to reverse the identifier text.
            var action =
                CodeAction.Create(
                    "Extract Parameter Object",
                    cancellationToken =>
                        ExtractParameterObjectAsync(context.Document, methodDecl, cancellationToken));

            // Register this code action.
            context.RegisterRefactoring(action);
        }

        private async Task<Solution> ExtractParameterObjectAsync(Document document, MethodDeclarationSyntax methodDeclarationSyntax, CancellationToken cancellationToken)
        {
            var parameterList = methodDeclarationSyntax.ParameterList;

            var currentProject = document.Project;

            string className = "ParameterObject";
            string parameterObjectContents = GetParameterObjectContents(className, parameterList, currentProject.AssemblyName);

            string classFileName = $"{className}.cs";

            var sourceText = SourceText.From(parameterObjectContents);

            var newDocument = currentProject.AddDocument(classFileName, sourceText, document.Folders);
            newDocument = await RemoveUnusedImportDirectivesAsync(newDocument, cancellationToken);

            return newDocument.Project.Solution;
        }

        private async Task UpdateOriginalMethodAsync(Document document, MethodDeclarationSyntax methodDeclarationSyntax, CancellationToken cancellationToken)
        {
            SyntaxNode documentRootNode = await document.GetSyntaxRootAsync(cancellationToken);
            var members = documentRootNode.DescendantNodes().OfType<MethodDeclarationSyntax>();
            var matchingMember = members.First(m => m.Identifier.Equals(methodDeclarationSyntax.Identifier));
        }


        private string GetParameterObjectContents(string className, ParameterListSyntax parameterList, string assemblyName)
        {
            MemberDeclarationSyntax[] aoProperties = GetProperties(parameterList);
            ConstructorDeclarationSyntax oConstructorDeclaration = GetConstructor(className);

            var comp = SyntaxFactory.CompilationUnit()
                .AddMembers(
                    SyntaxFactory.NamespaceDeclaration(SyntaxFactory.IdentifierName(assemblyName))
                            .AddMembers(
                            SyntaxFactory.ClassDeclaration(className)
                                .AddMembers(aoProperties)
                                .AddMembers(oConstructorDeclaration)))
                .NormalizeWhitespace();

            return comp.ToFullString();
        }

        private static ConstructorDeclarationSyntax GetConstructor(string className)
        {
            return
                SyntaxFactory.ConstructorDeclaration(className)
                        .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                        .WithBody(SyntaxFactory.Block());
        }

        private static MemberDeclarationSyntax[] GetProperties(ParameterListSyntax parameterList)
        {
            return parameterList.Parameters
                    .Select(oParameter =>
                        SyntaxFactory.PropertyDeclaration(oParameter.Type, oParameter.Identifier)
                                            .AddAccessorListAccessors(
                                            SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                                            SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                                                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)))
                                        .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                    .ToArray();
        }


        private static async Task<Document> RemoveUnusedImportDirectivesAsync(Document document, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            root = RemoveUnusedImportDirectives(semanticModel, root, cancellationToken);
            document = document.WithSyntaxRoot(root);
            return document;
        }

        private static SyntaxNode RemoveUnusedImportDirectives(SemanticModel semanticModel, SyntaxNode root, CancellationToken cancellationToken)
        {
            var oldUsings = root.DescendantNodesAndSelf().Where(s => s is UsingDirectiveSyntax);
            var unusedUsings = GetUnusedImportDirectives(semanticModel, cancellationToken);
            var leadingTrivia = root.GetLeadingTrivia();

            root = root.RemoveNodes(oldUsings, SyntaxRemoveOptions.KeepNoTrivia);
            var newUsings = SyntaxFactory.List(oldUsings.Except(unusedUsings));

            root = ((CompilationUnitSyntax)root)
                .WithUsings(newUsings)
                .WithLeadingTrivia(leadingTrivia);

            return root;
        }

        private static HashSet<SyntaxNode> GetUnusedImportDirectives(SemanticModel model, CancellationToken cancellationToken)
        {
            var unusedImportDirectives = new HashSet<SyntaxNode>();
            var root = model.SyntaxTree.GetRoot(cancellationToken);
            foreach (var diagnostic in model.GetDiagnostics(null, cancellationToken).Where(d => d.Id == "CS8019" || d.Id == "CS0105"))
            {
                var usingDirectiveSyntax = root.FindNode(diagnostic.Location.SourceSpan, false, false) as UsingDirectiveSyntax;
                if (usingDirectiveSyntax != null)
                {
                    unusedImportDirectives.Add(usingDirectiveSyntax);
                }
            }

            return unusedImportDirectives;
        }
    }
}
