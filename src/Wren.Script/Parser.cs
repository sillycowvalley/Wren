namespace Wren.Script;

/// <summary>
/// Reads Wren BASIC into a syntax tree. Errors carry the line they were found on.
/// Statements end at the end of a line or at ':'.
/// </summary>
public sealed class Parser
{
    private readonly List<Token> tokens;
    private int pos;

    private Parser(List<Token> tokens) => this.tokens = tokens;

    public static ProgramSyntax Parse(string source, Board board = Board.OledThing) =>
        new Parser(Lexer.Read(source)) { board = board }.ParseProgram();

    private Board board = Board.OledThing;

    private Token Current => tokens[pos];
    private Token Next => tokens[Math.Min(pos + 1, tokens.Count - 1)];

    private Token Take() => tokens[pos++];

    private ScriptException Error(string message) => new(Current.Line, message);

    private bool Accept(string word)
    {
        if (!Current.Is(word)) return false;
        pos++;
        return true;
    }

    private bool AcceptSymbol(string symbol)
    {
        if (!Current.IsSymbol(symbol)) return false;
        pos++;
        return true;
    }

    private void Expect(string word)
    {
        if (!Accept(word)) throw Error($"Expected {word.ToUpperInvariant()}, found {Current}");
    }

    private void ExpectSymbol(string symbol)
    {
        if (!AcceptSymbol(symbol)) throw Error($"Expected '{symbol}', found {Current}");
    }

    private string ExpectName(string what)
    {
        if (Current.Kind != TokenKind.Name) throw Error($"Expected {what}, found {Current}");
        string name = Take().Text;
        if (Names.IsReserved(name)) throw new ScriptException(tokens[pos - 1].Line, $"'{name}' is a reserved word");
        return name;
    }

    private void EndOfStatement()
    {
        if (Current.Kind == TokenKind.EndOfLine) { pos++; return; }
        if (Current.Kind == TokenKind.EndOfFile) return;
        throw Error($"Expected the end of the line, found {Current}");
    }

    private void SkipBlankLines()
    {
        while (Current.Kind == TokenKind.EndOfLine) pos++;
    }

    // ---- Top level ----

    private ProgramSyntax ParseProgram()
    {
        var program = new ProgramSyntax();
        while (true)
        {
            SkipBlankLines();
            if (Current.Kind == TokenKind.EndOfFile) return program;
            int line = Current.Line;

            if (Accept("DIM")) { program.Globals.Add(ParseDim(line)); EndOfStatement(); }
            else if (Accept("CONST"))
            {
                string name = ExpectName("a constant name");
                ExpectSymbol("=");
                program.Constants.Add(new ConstDecl(line, name, ParseExpression()));
                EndOfStatement();
            }
            else if (Accept("SUB")) program.Routines.Add(ParseRoutine(line, false));
            else if (Accept("FUNCTION")) program.Routines.Add(ParseRoutine(line, true));
            else if (Accept("ON")) program.Handlers.Add(ParseHandler(line));
            else throw Error($"Expected DIM, CONST, SUB, FUNCTION or ON, found {Current}. Statements belong inside a SUB or an ON handler");
        }
    }

    private DimStmt ParseDim(int line)
    {
        var items = new List<DimItem>();
        do
        {
            int itemLine = Current.Line;
            string name = ExpectName("a variable name");
            Expr? size = null;
            bool bytes = false;
            if (AcceptSymbol("("))
            {
                size = ParseExpression();
                ExpectSymbol(")");
                if (Accept("AS"))
                {
                    if (Accept("BYTE")) bytes = true;
                    else if (!Accept("INTEGER")) throw Error("Expected BYTE or INTEGER after AS");
                }
            }
            items.Add(new DimItem(itemLine, name, size, bytes));
        }
        while (AcceptSymbol(","));
        return new DimStmt(line, items);
    }

    private Routine ParseRoutine(int line, bool isFunction)
    {
        string name = ExpectName(isFunction ? "a FUNCTION name" : "a SUB name");
        var parameters = new List<string>();
        if (AcceptSymbol("("))
        {
            if (!Current.IsSymbol(")"))
            {
                do parameters.Add(ExpectName("a parameter name"));
                while (AcceptSymbol(","));
            }
            ExpectSymbol(")");
        }
        EndOfStatement();
        string end = isFunction ? "FUNCTION" : "SUB";
        var body = ParseBlock(() => Current.Is("END") && Next.Is(end));
        int endLine = Current.Line;
        Expect("END");
        Expect(end);
        EndOfStatement();
        return new Routine(line, name, parameters, isFunction, body, endLine);
    }

    private Handler ParseHandler(int line)
    {
        string ev = Current.Text.ToUpperInvariant();
        if (Current.Kind != TokenKind.Name) throw Error($"Expected an event after ON, found {Current}");
        pos++;
        int pin = -1, edges = 1, qualifier = -1;
        bool qualifierHigh = false;
        Expr? interval = null;

        switch (ev)
        {
            case "START":
            case "CHANGE":
            case "PINS":
                break;
            case "LATCH":
                pin = ParsePin();
                if (Accept("FALLING")) edges = 1;
                else if (Accept("RISING")) edges = 2;
                else if (Accept("BOTH")) edges = 3;
                if (Accept("WHILE"))
                {
                    qualifier = ParsePin();
                    if (qualifier == pin) throw Error("The WHILE pin must be a different pin");
                    if (Accept("HIGH")) qualifierHigh = true;
                    else if (!Accept("LOW")) throw Error("Expected LOW or HIGH after the WHILE pin");
                }
                break;
            case "PRESS":
            case "RELEASE":
                pin = ParsePin();
                break;
            case "EVERY":
                interval = ParseExpression();
                Accept("MS");
                break;
            default:
                throw new ScriptException(line, $"Unknown event '{ev}'. Events: START, CHANGE, PINS, LATCH, PRESS, RELEASE, EVERY");
        }
        EndOfStatement();
        var body = ParseBlock(() => Current.Is("END") && Next.Is("ON"));
        int endLine = Current.Line;
        Expect("END");
        Expect("ON");
        EndOfStatement();
        return new Handler(line, ev, pin, edges, qualifier, qualifierHigh, interval, body, endLine);
    }

    private int ParsePin()
    {
        if (Current.Kind == TokenKind.Name && Names.PinsFor(board).TryGetValue(Current.Text.ToUpperInvariant(), out int pin))
        {
            pos++;
            return pin;
        }
        if (Current.Kind == TokenKind.Name && Names.NotOn(Current.Text, board) is string why) throw Error(why);
        throw Error(board == Board.LedThing
            ? $"Expected a pin (M0, M1 or M2), found {Current}"
            : $"Expected a pin (PA1, PA2, PA3, PD2 or PD3), found {Current}");
    }

    // ---- Statements ----

    private List<Stmt> ParseBlock(Func<bool> atEnd)
    {
        var body = new List<Stmt>();
        while (true)
        {
            SkipBlankLines();
            if (Current.Kind == TokenKind.EndOfFile) throw Error("The file ends inside a block: an END, NEXT or WEND is missing");
            if (atEnd()) return body;
            body.Add(ParseStatement());
        }
    }

    private Stmt ParseStatement()
    {
        Stmt stmt = ParseSimpleOrBlock(out bool needsEnd);
        if (needsEnd) EndOfStatement();
        return stmt;
    }

    /// <summary>Parses one statement; needsEnd tells the caller to expect the end of the line.</summary>
    private Stmt ParseSimpleOrBlock(out bool needsEnd)
    {
        int line = Current.Line;
        needsEnd = true;

        if (Accept("DIM")) return ParseDim(line);
        if (Accept("IF")) return ParseIf(line, out needsEnd);
        if (Accept("WHILE"))
        {
            Expr condition = ParseExpression();
            EndOfStatement();
            var body = ParseBlock(() => Current.Is("WEND"));
            Expect("WEND");
            return new WhileStmt(line, condition, body);
        }
        if (Accept("FOR"))
        {
            string variable = ExpectName("the FOR variable");
            ExpectSymbol("=");
            Expr from = ParseExpression();
            Expect("TO");
            Expr to = ParseExpression();
            Expr? step = Accept("STEP") ? ParseExpression() : null;
            EndOfStatement();
            var body = ParseBlock(() => Current.Is("NEXT"));
            Expect("NEXT");
            if (Current.Kind == TokenKind.Name)
            {
                string after = Take().Text;
                if (!after.Equals(variable, StringComparison.OrdinalIgnoreCase))
                    throw new ScriptException(line, $"NEXT {after} does not match FOR {variable}");
            }
            return new ForStmt(line, variable, from, to, step, body);
        }
        if (Accept("EXIT"))
        {
            string what = Current.Text.ToUpperInvariant();
            if (!(Accept("FOR") || Accept("WHILE") || Accept("SUB") || Accept("FUNCTION") || Accept("ON")))
                throw Error("Expected FOR, WHILE, SUB, FUNCTION or ON after EXIT");
            return new ExitStmt(line, what);
        }
        if (Accept("RETURN"))
        {
            Expr? value = Current.Kind is TokenKind.EndOfLine or TokenKind.EndOfFile || Current.Is("ELSE") ? null : ParseExpression();
            return new ReturnStmt(line, value);
        }
        if (Accept("CALL"))
        {
            string name = ExpectName("a SUB name");
            var args = new List<Expr>();
            if (AcceptSymbol("("))
            {
                if (!Current.IsSymbol(")")) args = ParseArguments();
                ExpectSymbol(")");
            }
            return new CallStmt(line, name, args);
        }
        Accept("LET");

        if (Current.Is("END") || Current.Is("NEXT") || Current.Is("WEND") || Current.Is("ELSE") || Current.Is("ELSEIF"))
        {
            string found = Current.Is("END") ? $"END {Next.Text.ToUpperInvariant()}" : Current.Text.ToUpperInvariant();
            throw Error($"{found} here, but the block it is in is not closed: an END IF, NEXT, WEND or END is missing above");
        }
        if (Current.Kind != TokenKind.Name) throw Error($"Expected a statement, found {Current}");
        string word = Take().Text;

        if (Current.IsSymbol("="))
        {
            pos++;
            return new AssignStmt(line, word, null, ParseExpression());
        }

        // name(index) = value is an array element; anything else is a call.
        if (Current.IsSymbol("(") && !Names.Statements.ContainsKey(word.ToUpperInvariant()))
        {
            int save = pos;
            pos++;
            var inside = Current.IsSymbol(")") ? new List<Expr>() : ParseArguments();
            ExpectSymbol(")");
            if (Current.IsSymbol("="))
            {
                pos++;
                if (inside.Count != 1) throw new ScriptException(line, $"An array element takes one index: {word}(i)");
                return new AssignStmt(line, word, inside[0], ParseExpression());
            }
            if (Current.Kind is TokenKind.EndOfLine or TokenKind.EndOfFile || Current.Is("ELSE"))
                return new CallStmt(line, word, inside);
            pos = save; // name (a), b: arguments without brackets
        }

        var arguments = new List<Expr>();
        if (!(Current.Kind is TokenKind.EndOfLine or TokenKind.EndOfFile) && !Current.Is("ELSE"))
            arguments = ParseArguments();
        return new CallStmt(line, word, arguments);
    }

    private Stmt ParseIf(int line, out bool needsEnd)
    {
        Expr condition = ParseExpression();
        Expect("THEN");

        if (Current.Kind != TokenKind.EndOfLine)
        {
            // IF c THEN statement [ELSE statement] on one line
            Stmt then = ParseSimpleOrBlock(out _);
            List<Stmt>? otherwise = null;
            if (Accept("ELSE")) otherwise = new List<Stmt> { ParseSimpleOrBlock(out _) };
            needsEnd = true;
            return new IfStmt(line, new List<(Expr, List<Stmt>)> { (condition, new List<Stmt> { then }) }, otherwise);
        }

        EndOfStatement();
        var branches = new List<(Expr, List<Stmt>)>();
        var elseIfLines = new List<int>();
        List<Stmt>? elseBody = null;
        bool AtBranchEnd() => Current.Is("ELSEIF") || Current.Is("ELSE") || (Current.Is("END") && Next.Is("IF"));
        branches.Add((condition, ParseBlock(AtBranchEnd)));
        while (true)
        {
            int branchLine = Current.Line;
            if (Accept("ELSEIF"))
            {
                elseIfLines.Add(branchLine);
                Expr c = ParseExpression();
                Expect("THEN");
                EndOfStatement();
                branches.Add((c, ParseBlock(AtBranchEnd)));
            }
            else if (Accept("ELSE"))
            {
                EndOfStatement();
                elseBody = ParseBlock(() => Current.Is("END") && Next.Is("IF"));
            }
            else break;
        }
        Expect("END");
        Expect("IF");
        needsEnd = true;
        return new IfStmt(line, branches, elseBody, elseIfLines);
    }

    private List<Expr> ParseArguments()
    {
        var args = new List<Expr> { ParseArgument() };
        while (AcceptSymbol(",")) args.Add(ParseArgument());
        return args;
    }

    private Expr ParseArgument()
    {
        if (Current.Kind == TokenKind.String)
        {
            var t = Take();
            return new StringExpr(t.Line, t.Text);
        }
        return ParseExpression();
    }

    // ---- Expressions, lowest precedence first ----

    public Expr ParseExpression() => ParseOr();

    private Expr ParseOr()
    {
        Expr left = ParseAnd();
        while (true)
        {
            int line = Current.Line;
            if (Accept("OR")) left = new BinaryExpr(line, "OR", left, ParseAnd());
            else if (Accept("XOR")) left = new BinaryExpr(line, "XOR", left, ParseAnd());
            else return left;
        }
    }

    private Expr ParseAnd()
    {
        Expr left = ParseNot();
        while (true)
        {
            int line = Current.Line;
            if (Accept("AND")) left = new BinaryExpr(line, "AND", left, ParseNot());
            else return left;
        }
    }

    private Expr ParseNot()
    {
        int line = Current.Line;
        if (Accept("NOT")) return new UnaryExpr(line, "NOT", ParseNot());
        return ParseComparison();
    }

    private Expr ParseComparison()
    {
        Expr left = ParseShift();
        while (Current.Kind == TokenKind.Symbol && Current.Text is "=" or "<>" or "<" or "<=" or ">" or ">=")
        {
            var op = Take();
            left = new BinaryExpr(op.Line, op.Text, left, ParseShift());
        }
        return left;
    }

    private Expr ParseShift()
    {
        Expr left = ParseAdd();
        while (true)
        {
            int line = Current.Line;
            if (Accept("SHL")) left = new BinaryExpr(line, "SHL", left, ParseAdd());
            else if (Accept("SHR")) left = new BinaryExpr(line, "SHR", left, ParseAdd());
            else return left;
        }
    }

    private Expr ParseAdd()
    {
        Expr left = ParseMultiply();
        while (Current.IsSymbol("+") || Current.IsSymbol("-"))
        {
            var op = Take();
            left = new BinaryExpr(op.Line, op.Text, left, ParseMultiply());
        }
        return left;
    }

    private Expr ParseMultiply()
    {
        Expr left = ParseUnary();
        while (true)
        {
            int line = Current.Line;
            if (AcceptSymbol("*")) left = new BinaryExpr(line, "*", left, ParseUnary());
            else if (AcceptSymbol("/")) left = new BinaryExpr(line, "/", left, ParseUnary());
            else if (Accept("MOD")) left = new BinaryExpr(line, "MOD", left, ParseUnary());
            else return left;
        }
    }

    private Expr ParseUnary()
    {
        int line = Current.Line;
        if (AcceptSymbol("-")) return new UnaryExpr(line, "-", ParseUnary());
        if (AcceptSymbol("+")) return ParseUnary();
        return ParsePrimary();
    }

    private Expr ParsePrimary()
    {
        var t = Current;
        switch (t.Kind)
        {
            case TokenKind.Number:
                pos++;
                return new NumberExpr(t.Line, t.Number);
            case TokenKind.Symbol when t.Text == "(":
            {
                pos++;
                Expr inner = ParseExpression();
                ExpectSymbol(")");
                return inner;
            }
            case TokenKind.Name:
            {
                pos++;
                if (Current.IsSymbol("("))
                {
                    pos++;
                    var args = Current.IsSymbol(")") ? new List<Expr>() : ParseArguments();
                    ExpectSymbol(")");
                    return new CallExpr(t.Line, t.Text, args);
                }
                return new NameExpr(t.Line, t.Text);
            }
            case TokenKind.String:
                throw Error("A string can only be used in TEXT");
            default:
                throw Error($"Expected a value, found {t}");
        }
    }
}
