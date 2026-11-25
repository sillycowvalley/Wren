namespace Wren.Script;

// The syntax tree the parser builds and the code generator walks.

public abstract record Node(int Line);

// ---- Expressions ----

public abstract record Expr(int Line) : Node(Line);

public sealed record NumberExpr(int Line, long Value) : Expr(Line);

public sealed record StringExpr(int Line, string Value) : Expr(Line);

/// <summary>A name on its own: variable, constant, or a function with no arguments.</summary>
public sealed record NameExpr(int Line, string Name) : Expr(Line);

/// <summary>name(args): an array element or a function call.</summary>
public sealed record CallExpr(int Line, string Name, List<Expr> Args) : Expr(Line);

public sealed record UnaryExpr(int Line, string Op, Expr Operand) : Expr(Line);

public sealed record BinaryExpr(int Line, string Op, Expr Left, Expr Right) : Expr(Line);

// ---- Statements ----

public abstract record Stmt(int Line) : Node(Line);

public sealed record DimStmt(int Line, List<DimItem> Items) : Stmt(Line);

public sealed record DimItem(int Line, string Name, Expr? Size, bool Bytes);

public sealed record AssignStmt(int Line, string Name, Expr? Index, Expr Value) : Stmt(Line);

/// <summary>A built-in statement or a SUB call.</summary>
public sealed record CallStmt(int Line, string Name, List<Expr> Args) : Stmt(Line);

/// <summary>An IF: its branches in order (IF, then each ELSEIF, whose lines are ElseIfLines), and the ELSE.</summary>
public sealed record IfStmt(int Line, List<(Expr Condition, List<Stmt> Body)> Branches, List<Stmt>? Else, List<int>? ElseIfLines = null) : Stmt(Line);

public sealed record WhileStmt(int Line, Expr Condition, List<Stmt> Body) : Stmt(Line);

public sealed record ForStmt(int Line, string Variable, Expr From, Expr To, Expr? Step, List<Stmt> Body) : Stmt(Line);

public sealed record ExitStmt(int Line, string What) : Stmt(Line);

public sealed record ReturnStmt(int Line, Expr? Value) : Stmt(Line);

// ---- Top level ----

public sealed record ConstDecl(int Line, string Name, Expr Value);

public sealed record Routine(int Line, string Name, List<string> Parameters, bool IsFunction, List<Stmt> Body, int EndLine = 0);

public sealed record Handler(int Line, string Event, int Pin, int Edges, int QualifierPin, bool QualifierHigh, Expr? Interval, List<Stmt> Body, int EndLine = 0);

public sealed class ProgramSyntax
{
    public List<DimStmt> Globals { get; } = new();
    public List<ConstDecl> Constants { get; } = new();
    public List<Routine> Routines { get; } = new();
    public List<Handler> Handlers { get; } = new();
}
