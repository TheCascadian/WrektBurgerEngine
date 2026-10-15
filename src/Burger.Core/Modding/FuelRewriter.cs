using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Burger.Core.Modding;

/// <summary>
/// Inserts <see cref="Fuel.Tick"/> into sandboxed mod source before it is compiled: at the start of every function
/// body (expression bodies become blocks), every loop body, every catch and finally block and after every label.
/// No newline is added, so line numbers in later messages stay those of the mod's own file.
/// </summary>
internal sealed class FuelRewriter : CSharpSyntaxRewriter
{
    /// <summary>Changes this when the inserted code changes (part of the compiled-script cache key).</summary>
    public const string Version = "fuel-v1";

    private readonly SemanticModel _model;
    private FuelRewriter(SemanticModel model) { _model = model; }

    public static SyntaxTree Rewrite(SyntaxTree tree, SemanticModel model)
    {
        var root = new FuelRewriter(model).Visit(tree.GetRoot());
        return tree.WithRootAndOptions(root, tree.Options);
    }

    private static StatementSyntax TickStatement() =>
        SyntaxFactory.ParseStatement("global::Burger.Core.Modding.Fuel.Tick();").WithTrailingTrivia(SyntaxFactory.Space);

    private static BlockSyntax Ticked(BlockSyntax b) => b.WithStatements(b.Statements.Insert(0, TickStatement()));

    private static BlockSyntax Ticked(StatementSyntax s) =>
        s is BlockSyntax b ? Ticked(b) : SyntaxFactory.Block(TickStatement(), s.WithoutLeadingTrivia()).WithLeadingTrivia(s.GetLeadingTrivia());

    /// <summary>A block that ticks and then evaluates the expression (as a statement when nothing is returned).</summary>
    private static BlockSyntax Body(ExpressionSyntax e, bool returnsValue)
    {
        StatementSyntax s = e is ThrowExpressionSyntax t ? SyntaxFactory.ThrowStatement(t.Expression)
            : returnsValue ? SyntaxFactory.ReturnStatement(e.WithLeadingTrivia(SyntaxFactory.Space))
            : SyntaxFactory.ExpressionStatement(e);
        return SyntaxFactory.Block(TickStatement(), s).WithLeadingTrivia(SyntaxFactory.Space);
    }

    private static bool ReturnsValue(IMethodSymbol? m)
    {
        if (m == null) return true;
        if (m.ReturnsVoid) return false;
        // an async method returning Task or ValueTask (not generic) returns nothing in its body
        return !(m.IsAsync && m.ReturnType is INamedTypeSymbol { IsGenericType: false } rt && (rt.Name == "Task" || rt.Name == "ValueTask"));
    }

    // ---------------------------------------------------------------- function bodies

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        bool value = ReturnsValue(_model.GetDeclaredSymbol(node));
        var n = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
        if (n.Body != null) return n.WithBody(Ticked(n.Body));
        if (n.ExpressionBody != null) return n.WithExpressionBody(null).WithSemicolonToken(default).WithBody(Body(n.ExpressionBody.Expression, value));
        return n;
    }

    public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        bool value = ReturnsValue(_model.GetDeclaredSymbol(node) as IMethodSymbol);
        var n = (LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node)!;
        if (n.Body != null) return n.WithBody(Ticked(n.Body));
        if (n.ExpressionBody != null) return n.WithExpressionBody(null).WithSemicolonToken(default).WithBody(Body(n.ExpressionBody.Expression, value));
        return n;
    }

    public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        var n = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node)!;
        if (n.Body != null) return n.WithBody(Ticked(n.Body));
        if (n.ExpressionBody != null) return n.WithExpressionBody(null).WithSemicolonToken(default).WithBody(Body(n.ExpressionBody.Expression, false));
        return n;
    }

    public override SyntaxNode? VisitDestructorDeclaration(DestructorDeclarationSyntax node)
    {
        var n = (DestructorDeclarationSyntax)base.VisitDestructorDeclaration(node)!;
        if (n.Body != null) return n.WithBody(Ticked(n.Body));
        if (n.ExpressionBody != null) return n.WithExpressionBody(null).WithSemicolonToken(default).WithBody(Body(n.ExpressionBody.Expression, false));
        return n;
    }

    public override SyntaxNode? VisitOperatorDeclaration(OperatorDeclarationSyntax node)
    {
        var n = (OperatorDeclarationSyntax)base.VisitOperatorDeclaration(node)!;
        if (n.Body != null) return n.WithBody(Ticked(n.Body));
        if (n.ExpressionBody != null) return n.WithExpressionBody(null).WithSemicolonToken(default).WithBody(Body(n.ExpressionBody.Expression, true));
        return n;
    }

    public override SyntaxNode? VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
    {
        var n = (ConversionOperatorDeclarationSyntax)base.VisitConversionOperatorDeclaration(node)!;
        if (n.Body != null) return n.WithBody(Ticked(n.Body));
        if (n.ExpressionBody != null) return n.WithExpressionBody(null).WithSemicolonToken(default).WithBody(Body(n.ExpressionBody.Expression, true));
        return n;
    }

    public override SyntaxNode? VisitAccessorDeclaration(AccessorDeclarationSyntax node)
    {
        bool value = node.IsKind(SyntaxKind.GetAccessorDeclaration);
        var n = (AccessorDeclarationSyntax)base.VisitAccessorDeclaration(node)!;
        if (n.Body != null) return n.WithBody(Ticked(n.Body));
        if (n.ExpressionBody != null) return n.WithExpressionBody(null).WithSemicolonToken(default).WithBody(Body(n.ExpressionBody.Expression, value));
        return n; // auto-property accessor: no code
    }

    public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        var n = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node)!;
        if (n.ExpressionBody == null) return n;
        var get = SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, Body(n.ExpressionBody.Expression, true));
        return n.WithExpressionBody(null).WithSemicolonToken(default).WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(get)));
    }

    public override SyntaxNode? VisitIndexerDeclaration(IndexerDeclarationSyntax node)
    {
        var n = (IndexerDeclarationSyntax)base.VisitIndexerDeclaration(node)!;
        if (n.ExpressionBody == null) return n;
        var get = SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, Body(n.ExpressionBody.Expression, true));
        return n.WithExpressionBody(null).WithSemicolonToken(default).WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(get)));
    }

    public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
    {
        bool value = ReturnsValue(_model.GetSymbolInfo(node).Symbol as IMethodSymbol);
        var n = (ParenthesizedLambdaExpressionSyntax)base.VisitParenthesizedLambdaExpression(node)!;
        if (n.Block != null) return n.WithBlock(Ticked(n.Block));
        if (n.ExpressionBody != null) return n.WithExpressionBody(null).WithBlock(Body(n.ExpressionBody, value));
        return n;
    }

    public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
    {
        bool value = ReturnsValue(_model.GetSymbolInfo(node).Symbol as IMethodSymbol);
        var n = (SimpleLambdaExpressionSyntax)base.VisitSimpleLambdaExpression(node)!;
        if (n.Block != null) return n.WithBlock(Ticked(n.Block));
        if (n.ExpressionBody != null) return n.WithExpressionBody(null).WithBlock(Body(n.ExpressionBody, value));
        return n;
    }

    public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
    {
        var n = (AnonymousMethodExpressionSyntax)base.VisitAnonymousMethodExpression(node)!;
        return n.WithBlock(Ticked(n.Block));
    }

    // ---------------------------------------------------------------- loops, labels, handlers

    public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
    { var n = (WhileStatementSyntax)base.VisitWhileStatement(node)!; return n.WithStatement(Ticked(n.Statement)); }

    public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
    { var n = (DoStatementSyntax)base.VisitDoStatement(node)!; return n.WithStatement(Ticked(n.Statement)); }

    public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
    { var n = (ForStatementSyntax)base.VisitForStatement(node)!; return n.WithStatement(Ticked(n.Statement)); }

    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
    { var n = (ForEachStatementSyntax)base.VisitForEachStatement(node)!; return n.WithStatement(Ticked(n.Statement)); }

    public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
    { var n = (ForEachVariableStatementSyntax)base.VisitForEachVariableStatement(node)!; return n.WithStatement(Ticked(n.Statement)); }

    public override SyntaxNode? VisitLabeledStatement(LabeledStatementSyntax node)
    { var n = (LabeledStatementSyntax)base.VisitLabeledStatement(node)!; return n.WithStatement(Ticked(n.Statement)); }

    public override SyntaxNode? VisitCatchClause(CatchClauseSyntax node)
    { var n = (CatchClauseSyntax)base.VisitCatchClause(node)!; return n.WithBlock(Ticked(n.Block)); }

    public override SyntaxNode? VisitFinallyClause(FinallyClauseSyntax node)
    { var n = (FinallyClauseSyntax)base.VisitFinallyClause(node)!; return n.WithBlock(Ticked(n.Block)); }
}
