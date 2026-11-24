namespace Wren.Script;

public enum VariableKind { Integer, IntegerArray, ByteArray }

/// <summary>A global variable: where it lives in the kernel's VARS area.</summary>
public sealed record VariableInfo(string Name, VariableKind Kind, int Offset, int Length);

/// <summary>A SUB, FUNCTION or handler: its code range and its parameters and locals by index.</summary>
public sealed record RoutineInfo(string Name, int Start, int End, IReadOnlyList<string> Locals);

public sealed record ScriptError(int Line, string Message)
{
    public override string ToString() => $"Line {Line}: {Message}";
}

/// <summary>A compiled program: the image to upload, and what the debugger needs.</summary>
public sealed class CompiledProgram
{
    public byte[] Image { get; init; } = Array.Empty<byte>();
    public List<ScriptError> Errors { get; } = new();
    public bool Success => Errors.Count == 0;

    /// <summary>Statement start addresses and their source lines, in address order.</summary>
    public List<(int Address, int Line)> Lines { get; init; } = new();
    public List<VariableInfo> Globals { get; init; } = new();
    public List<RoutineInfo> Routines { get; init; } = new();
    public int DataBytes { get; init; }

    /// <summary>64-byte flash blocks the program needs.</summary>
    public int Blocks => (Image.Length + 63) / 64;

    /// <summary>The source line of the statement containing address, or 0.</summary>
    public int LineAt(int address)
    {
        int line = 0;
        foreach (var (a, l) in Lines)
        {
            if (a > address) break;
            line = l;
        }
        return line;
    }

    public RoutineInfo? RoutineAt(int address) => Routines.FirstOrDefault(r => address >= r.Start && address < r.End);
}

/// <summary>Compiles Wren BASIC to bytecode. See docs/Design.md and docs/Bytecode.md.</summary>
public sealed class Compiler
{
    /// <summary>Flash blocks in the program store, $9440-$9FFF. The kernel's STORE must agree.</summary>
    public const int StoreBlocks = (0xA000 - 0x9440) / 64;
    public const int VariableArea = 256;

    private sealed class RoutineSymbol
    {
        public required Routine Syntax { get; init; }
        public int Label { get; init; }
    }

    private sealed class Scope
    {
        public Dictionary<string, int> Locals { get; } = new();
        public List<string> Names { get; } = new();
        public int Parameters { get; init; }
        public string Kind { get; init; } = "ON"; // SUB, FUNCTION or ON
        public int Add(string name)
        {
            int index = Names.Count;
            Names.Add(name);
            Locals[name.ToUpperInvariant()] = index;
            return index;
        }
    }

    private readonly CodeBuilder code = new();
    private readonly Dictionary<string, long> constants = new();
    private readonly Dictionary<string, VariableInfo> globals = new();
    private readonly Dictionary<string, RoutineSymbol> routines = new();
    private readonly Stack<(string Kind, int ExitLabel)> loops = new();
    private Scope scope = new();
    private int hiddenCount;

    public static CompiledProgram Compile(string source)
    {
        try
        {
            var syntax = Parser.Parse(source);
            return new Compiler().Generate(syntax);
        }
        catch (ScriptException ex)
        {
            var failed = new CompiledProgram();
            failed.Errors.Add(new ScriptError(ex.Line, ex.Message));
            return failed;
        }
    }

    private static string Key(string name) => name.ToUpperInvariant();

    private CompiledProgram Generate(ProgramSyntax program)
    {
        foreach (var c in program.Constants)
        {
            CheckNewName(c.Name, c.Line);
            constants[Key(c.Name)] = Fold(c.Value) ?? throw new ScriptException(c.Line, $"CONST {c.Name} must be a constant value");
        }

        LayOutGlobals(program);

        foreach (var r in program.Routines)
        {
            CheckNewName(r.Name, r.Line);
            routines[Key(r.Name)] = new RoutineSymbol { Syntax = r, Label = code.NewLabel() };
        }

        // Handler table
        var handlerLabels = new List<(Handler Handler, int Event, int Label)>();
        int everyCount = 0;
        var seen = new HashSet<int>();
        foreach (var h in program.Handlers)
        {
            int ev = h.Event switch
            {
                "START" => Op.EventStart,
                "CHANGE" => Op.EventChange,
                "PINS" => Op.EventPins,
                "LATCH" => Op.EventLatch + h.Pin,
                "PRESS" => Op.EventPress + h.Pin,
                "RELEASE" => Op.EventRelease + h.Pin,
                _ => Op.EventEvery + everyCount++,
            };
            if (everyCount > 4) throw new ScriptException(h.Line, "At most four ON EVERY handlers");
            if (!seen.Add(ev)) throw new ScriptException(h.Line, $"There is already an ON {h.Event} handler{(h.Pin >= 0 ? " for this pin" : "")}");
            handlerLabels.Add((h, ev, code.NewLabel()));
        }

        int dataBytes = globals.Values.Sum(v => v.Kind switch
        {
            VariableKind.Integer => 2,
            VariableKind.IntegerArray => v.Length * 2,
            _ => v.Length,
        });

        code.Emit(1, (byte)dataBytes, (byte)handlerLabels.Count);
        foreach (var (h, ev, label) in handlerLabels)
        {
            code.Emit((byte)ev);
            code.AddressOf(label);
            if (h.Event == "LATCH")
            {
                int settings = h.Edges;
                if (h.QualifierPin >= 0) settings |= 0x04 | (h.QualifierHigh ? 0x08 : 0) | (h.QualifierPin << 4);
                if (IsSoloLatch(h, program.Handlers)) settings |= SoloLatch;
                code.Emit((byte)settings);
            }
            else if (h.Event == "EVERY")
            {
                long interval = Fold(h.Interval!) ?? throw new ScriptException(h.Line, "The EVERY interval must be a constant");
                if (interval is < 1 or > 65535) throw new ScriptException(h.Line, "The EVERY interval must be 1 to 65535 ms");
                code.Emit((byte)(interval >> 8), (byte)interval);
            }
        }

        // Code
        var routineLabels = new List<(string Name, int Start, int End, List<string> Locals)>();
        foreach (var (h, _, label) in handlerLabels)
        {
            int end = code.NewLabel();
            code.Mark(label);
            var names = GenerateBody("ON", 0, h.Body, Array.Empty<string>(), h.Line);
            code.Mark(end);
            string title = h.Event switch
            {
                "LATCH" or "PRESS" or "RELEASE" => $"ON {h.Event} {Names.Pins.First(p => p.Value == h.Pin).Key}",
                _ => $"ON {h.Event}",
            };
            routineLabels.Add((title, label, end, names));
        }
        foreach (var r in routines.Values)
        {
            int end = code.NewLabel();
            code.Mark(r.Label);
            var names = GenerateBody(r.Syntax.IsFunction ? "FUNCTION" : "SUB", r.Syntax.Parameters.Count, r.Syntax.Body, r.Syntax.Parameters, r.Syntax.Line);
            code.Mark(end);
            routineLabels.Add((r.Syntax.Name, r.Label, end, names));
        }

        var layout = code.Build();
        var result = new CompiledProgram
        {
            Image = layout.Code,
            Lines = layout.Lines,
            Globals = globals.Values.OrderBy(g => g.Offset).ToList(),
            Routines = routineLabels.Select(r => new RoutineInfo(r.Name, layout.LabelAddresses[r.Start], layout.LabelAddresses[r.End], r.Locals)).ToList(),
            DataBytes = dataBytes,
        };

        if (result.Blocks > StoreBlocks)
            result.Errors.Add(new ScriptError(0, $"The program is {layout.Code.Length} bytes; the store holds {StoreBlocks * 64}"));
        else if (dataBytes + result.Blocks > VariableArea)
            result.Errors.Add(new ScriptError(0,
                $"The variables need {dataBytes} bytes, but only {VariableArea - result.Blocks} are free beside this program's {result.Blocks} blocks"));
        return result;
    }

    private void CheckNewName(string name, int line)
    {
        string key = Key(name);
        if (constants.ContainsKey(key) || globals.ContainsKey(key) || routines.ContainsKey(key))
            throw new ScriptException(line, $"'{name}' is already defined");
    }

    private void LayOutGlobals(ProgramSyntax program)
    {
        var items = program.Globals.SelectMany(d => d.Items).ToList();
        int offset = 0;
        // Integers first, so the first 16 get one-byte loads and stores.
        foreach (var item in items.Where(i => i.Size == null))
        {
            CheckNewName(item.Name, item.Line);
            globals[Key(item.Name)] = new VariableInfo(item.Name, VariableKind.Integer, offset, 1);
            offset += 2;
        }
        foreach (var item in items.Where(i => i.Size != null && !i.Bytes).Concat(items.Where(i => i.Size != null && i.Bytes)))
        {
            CheckNewName(item.Name, item.Line);
            long size = Fold(item.Size!) ?? throw new ScriptException(item.Line, "An array size must be a constant");
            int max = item.Bytes ? 255 : 127;
            if (size < 1 || size > max) throw new ScriptException(item.Line, $"Array size must be 1 to {max}");
            var kind = item.Bytes ? VariableKind.ByteArray : VariableKind.IntegerArray;
            int bytes = item.Bytes ? (int)size : (int)size * 2;
            if (offset + bytes > VariableArea) throw new ScriptException(item.Line, "The variables do not fit in RAM");
            globals[Key(item.Name)] = new VariableInfo(item.Name, kind, offset, (int)size);
            offset += bytes;
        }
        if (offset > VariableArea) throw new ScriptException(items.LastOrDefault()?.Line ?? 0, "The variables do not fit in RAM");
    }

    // ---- Bodies ----

    private List<string> GenerateBody(string kind, int parameters, List<Stmt> body, IReadOnlyList<string> parameterNames, int line)
    {
        scope = new Scope { Parameters = parameters, Kind = kind };
        foreach (string p in parameterNames)
        {
            if (scope.Locals.ContainsKey(Key(p))) throw new ScriptException(line, $"Parameter '{p}' appears twice");
            scope.Add(p);
        }
        DeclareLocals(body);
        int locals = scope.Names.Count - parameters;
        if (kind != "ON" || locals > 0) code.Emit(Op.Enter, (byte)parameters, (byte)locals);

        foreach (var s in body) Statement(s);

        if (kind == "FUNCTION")
        {
            PushConstant(0);
            code.Emit(Op.ReturnValue);
        }
        else code.Emit(Op.Return);
        return scope.Names.ToList();
    }

    /// <summary>Finds DIMs and FOR loops in a body so ENTER can reserve their locals.</summary>
    private void DeclareLocals(List<Stmt> body)
    {
        foreach (var s in body)
        {
            switch (s)
            {
                case DimStmt d:
                    foreach (var item in d.Items)
                    {
                        if (item.Size != null) throw new ScriptException(item.Line, "Arrays must be declared outside SUBs and handlers");
                        if (Names.IsReserved(item.Name)) throw new ScriptException(item.Line, $"'{item.Name}' is a reserved word");
                        if (scope.Locals.ContainsKey(Key(item.Name))) throw new ScriptException(item.Line, $"'{item.Name}' is already defined here");
                        scope.Add(item.Name);
                    }
                    break;
                case IfStmt i:
                    foreach (var b in i.Branches) DeclareLocals(b.Body);
                    if (i.Else != null) DeclareLocals(i.Else);
                    break;
                case WhileStmt w:
                    DeclareLocals(w.Body);
                    break;
                case ForStmt f:
                    if (Fold(f.To) == null) scope.Add($"(limit {++hiddenCount})");
                    DeclareLocals(f.Body);
                    break;
            }
        }
    }

    // ---- Statements ----

    private void Statement(Stmt s)
    {
        if (s is not DimStmt) code.MarkLine(s.Line);
        switch (s)
        {
            case DimStmt:
                break;
            case AssignStmt a:
                Assign(a);
                break;
            case CallStmt c:
                CallStatement(c);
                break;
            case IfStmt i:
                If(i);
                break;
            case WhileStmt w:
            {
                int top = code.NewLabel(), exit = code.NewLabel();
                code.Mark(top);
                Condition(w.Condition, exit);
                loops.Push(("WHILE", exit));
                foreach (var b in w.Body) Statement(b);
                loops.Pop();
                code.Jump(Op.Jump, top);
                code.Mark(exit);
                break;
            }
            case ForStmt f:
                For(f);
                break;
            case ExitStmt e:
                Exit(e);
                break;
            case ReturnStmt r:
                Return(r);
                break;
        }
    }

    private void Assign(AssignStmt a)
    {
        string key = Key(a.Name);
        if (a.Index != null)
        {
            if (!globals.TryGetValue(key, out var array) || array.Kind == VariableKind.Integer)
                throw new ScriptException(a.Line, $"'{a.Name}' is not an array");
            Expression(a.Index);
            Expression(a.Value);
            EmitArray(array, store: true);
            return;
        }
        Expression(a.Value);
        Store(a.Name, a.Line);
    }

    private void Store(string name, int line)
    {
        string key = Key(name);
        if (scope.Locals.TryGetValue(key, out int local))
        {
            code.Emit(Op.StoreLocal, (byte)local);
            return;
        }
        if (globals.TryGetValue(key, out var g))
        {
            if (g.Kind != VariableKind.Integer) throw new ScriptException(line, $"'{name}' is an array: use {name}(index) = value");
            int index = g.Offset / 2;
            if (index < 16) code.Emit((byte)(Op.StoreGlobal + index));
            else code.Emit(Op.StoreGlobalByte, (byte)index);
            return;
        }
        if (constants.ContainsKey(key) || Names.IsReserved(name)) throw new ScriptException(line, $"'{name}' cannot be assigned to");
        throw new ScriptException(line, $"'{name}' is not declared: add DIM {name}");
    }

    private void EmitArray(VariableInfo array, bool store)
    {
        byte op = (array.Kind, store) switch
        {
            (VariableKind.IntegerArray, false) => Op.LoadArrayWord,
            (VariableKind.IntegerArray, true) => Op.StoreArrayWord,
            (_, false) => Op.LoadArrayByte,
            _ => Op.StoreArrayByte,
        };
        code.Emit(op, (byte)array.Offset, (byte)array.Length);
    }

    private void CallStatement(CallStmt c)
    {
        string key = Key(c.Name);
        if (Names.Statements.TryGetValue(key, out var builtin))
        {
            if (key == "TEXT") { Text(c, builtin); return; }
            BuiltinArguments(builtin, c.Args, c.Line);
            code.Emit(builtin.Opcode);
            return;
        }
        if (routines.TryGetValue(key, out var routine))
        {
            CallRoutine(routine, c.Args, c.Line);
            if (routine.Syntax.IsFunction) code.Emit(Op.Drop);
            return;
        }
        if (Names.Functions.ContainsKey(key)) throw new ScriptException(c.Line, $"{key} gives a value; use it in an expression");
        throw new ScriptException(c.Line, $"Unknown statement or SUB '{c.Name}'");
    }

    private void BuiltinArguments(Builtin b, List<Expr> args, int line)
    {
        if (args.Count < b.MinArgs || args.Count > b.MaxArgs)
        {
            string count = b.MinArgs == b.MaxArgs ? $"{b.MinArgs}" : $"{b.MinArgs} to {b.MaxArgs}";
            throw new ScriptException(line, $"{b.Name} takes {count} argument{(b.MaxArgs == 1 ? "" : "s")}");
        }
        foreach (var a in args) Expression(a);
        for (int i = args.Count; i < b.MaxArgs; i++) PushConstant(b.Defaults[i - b.MinArgs]);
    }

    private void Text(CallStmt c, Builtin b)
    {
        if (c.Args.Count is < 3 or > 4) throw new ScriptException(c.Line, "TEXT takes x, y, \"text\" and an optional font");
        if (c.Args[2] is not StringExpr str) throw new ScriptException(c.Line, "TEXT's third argument must be a string in quotes");
        if (str.Value.Length > 255) throw new ScriptException(c.Line, "The text is too long");
        Expression(c.Args[0]);
        Expression(c.Args[1]);
        if (c.Args.Count == 4) Expression(c.Args[3]);
        else PushConstant(b.Defaults[0]);
        var bytes = new List<byte> { b.Opcode, (byte)str.Value.Length };
        bytes.AddRange(str.Value.Select(ch => ch is >= ' ' and <= '~' ? (byte)ch : (byte)'?'));
        code.Emit(bytes.ToArray());
    }

    private void CallRoutine(RoutineSymbol routine, List<Expr> args, int line)
    {
        int expected = routine.Syntax.Parameters.Count;
        if (args.Count != expected)
            throw new ScriptException(line, $"{routine.Syntax.Name} takes {expected} argument{(expected == 1 ? "" : "s")}");
        foreach (var a in args) Expression(a);
        code.Call(routine.Label);
    }

    private void If(IfStmt i)
    {
        int end = code.NewLabel();
        foreach (var (condition, body) in i.Branches)
        {
            int next = code.NewLabel();
            Condition(condition, next);
            foreach (var s in body) Statement(s);
            code.Jump(Op.Jump, end);
            code.Mark(next);
        }
        if (i.Else != null)
        {
            foreach (var s in i.Else) Statement(s);
        }
        code.Mark(end);
    }

    /// <summary>Evaluates a condition and jumps to whenFalse if it is 0.</summary>
    private void Condition(Expr condition, int whenFalse)
    {
        if (Fold(condition) is long constant)
        {
            if (constant == 0) code.Jump(Op.Jump, whenFalse);
            return;
        }
        if (condition is UnaryExpr { Op: "NOT" } not)
        {
            Expression(not.Operand);
            code.Jump(Op.JumpNotZero, whenFalse);
            return;
        }
        Expression(condition);
        code.Jump(Op.JumpZero, whenFalse);
    }

    private void For(ForStmt f)
    {
        long step = 1;
        if (f.Step != null)
        {
            step = Fold(f.Step) ?? throw new ScriptException(f.Line, "STEP must be a constant");
            if (step == 0) throw new ScriptException(f.Line, "STEP cannot be 0");
        }
        Expression(f.From);
        Store(f.Variable, f.Line);

        long? limit = Fold(f.To);
        int limitSlot = -1;
        if (limit == null)
        {
            limitSlot = scope.Names.FindIndex(n => n.StartsWith("(limit") && !usedHidden.Contains(n));
            usedHidden.Add(scope.Names[limitSlot]);
            Expression(f.To);
            code.Emit(Op.StoreLocal, (byte)limitSlot);
        }

        int top = code.NewLabel(), exit = code.NewLabel();
        code.Mark(top);
        Expression(new NameExpr(f.Line, f.Variable));
        if (limit is long l) PushConstant(l);
        else code.Emit(Op.LoadLocal, (byte)limitSlot);
        code.Emit(step > 0 ? Op.Gt : Op.Lt);
        code.Jump(Op.JumpNotZero, exit);

        loops.Push(("FOR", exit));
        foreach (var s in f.Body) Statement(s);
        loops.Pop();

        Expression(new NameExpr(f.Line, f.Variable));
        PushConstant(step);
        code.Emit(Op.Add);
        Store(f.Variable, f.Line);
        code.Jump(Op.Jump, top);
        code.Mark(exit);
    }

    private readonly HashSet<string> usedHidden = new();

    private void Exit(ExitStmt e)
    {
        switch (e.What)
        {
            case "FOR":
            case "WHILE":
                foreach (var (kind, label) in loops)
                {
                    if (kind == e.What)
                    {
                        code.Jump(Op.Jump, label);
                        return;
                    }
                }
                throw new ScriptException(e.Line, $"EXIT {e.What} is not inside a {e.What} loop");
            default:
                if (e.What != scope.Kind) throw new ScriptException(e.Line, $"EXIT {e.What} is not inside a {e.What}");
                if (scope.Kind == "FUNCTION")
                {
                    PushConstant(0);
                    code.Emit(Op.ReturnValue);
                }
                else code.Emit(Op.Return);
                return;
        }
    }

    private void Return(ReturnStmt r)
    {
        if (scope.Kind == "FUNCTION")
        {
            if (r.Value == null) throw new ScriptException(r.Line, "RETURN in a FUNCTION needs a value");
            Expression(r.Value);
            code.Emit(Op.ReturnValue);
            return;
        }
        if (r.Value != null) throw new ScriptException(r.Line, "Only a FUNCTION can RETURN a value");
        code.Emit(Op.Return);
    }

    // ---- Expressions ----

    private void Expression(Expr e)
    {
        if (Fold(e) is long constant)
        {
            PushConstant(constant);
            return;
        }
        switch (e)
        {
            case NameExpr n:
                Name(n);
                break;
            case CallExpr c:
                Call(c);
                break;
            case UnaryExpr u:
                Expression(u.Operand);
                code.Emit(u.Op == "NOT" ? Op.Not : Op.Neg);
                break;
            case BinaryExpr b:
                Expression(b.Left);
                Expression(b.Right);
                code.Emit(BinaryOp(b.Op));
                break;
            case StringExpr s:
                throw new ScriptException(s.Line, "A string can only be used in TEXT");
            default:
                throw new ScriptException(e.Line, "Cannot compile this expression");
        }
    }

    private void Name(NameExpr n)
    {
        string key = Key(n.Name);
        if (scope.Locals.TryGetValue(key, out int local))
        {
            code.Emit(Op.LoadLocal, (byte)local);
            return;
        }
        if (globals.TryGetValue(key, out var g))
        {
            if (g.Kind != VariableKind.Integer) throw new ScriptException(n.Line, $"'{n.Name}' is an array: use {n.Name}(index)");
            int index = g.Offset / 2;
            if (index < 16) code.Emit((byte)(Op.LoadGlobal + index));
            else code.Emit(Op.LoadGlobalByte, (byte)index);
            return;
        }
        if (Names.Functions.TryGetValue(key, out var f))
        {
            if (f.MinArgs > 0) throw new ScriptException(n.Line, $"{f.Name} needs arguments: {f.Name}(...)");
            code.Emit(f.Opcode);
            return;
        }
        if (routines.TryGetValue(key, out var r))
        {
            if (!r.Syntax.IsFunction) throw new ScriptException(n.Line, $"SUB {r.Syntax.Name} does not give a value");
            CallRoutine(r, new List<Expr>(), n.Line);
            return;
        }
        throw new ScriptException(n.Line, $"'{n.Name}' is not declared: add DIM {n.Name}");
    }

    private void Call(CallExpr c)
    {
        string key = Key(c.Name);
        if (globals.TryGetValue(key, out var g) && g.Kind != VariableKind.Integer)
        {
            if (c.Args.Count != 1) throw new ScriptException(c.Line, $"An array element takes one index: {c.Name}(i)");
            Expression(c.Args[0]);
            EmitArray(g, store: false);
            return;
        }
        if (Names.Functions.TryGetValue(key, out var f))
        {
            BuiltinArguments(f, c.Args, c.Line);
            code.Emit(f.Opcode);
            return;
        }
        if (routines.TryGetValue(key, out var r))
        {
            if (!r.Syntax.IsFunction) throw new ScriptException(c.Line, $"SUB {r.Syntax.Name} does not give a value");
            CallRoutine(r, c.Args, c.Line);
            return;
        }
        if (Names.Statements.ContainsKey(key)) throw new ScriptException(c.Line, $"{key} is a statement, not a function");
        throw new ScriptException(c.Line, $"Unknown function or array '{c.Name}'");
    }

    private static byte BinaryOp(string op) => op switch
    {
        "+" => Op.Add,
        "-" => Op.Sub,
        "*" => Op.Mul,
        "/" => Op.Div,
        "MOD" => Op.Mod,
        "AND" => Op.And,
        "OR" => Op.Or,
        "XOR" => Op.Xor,
        "SHL" => Op.Shl,
        "SHR" => Op.Shr,
        "=" => Op.Eq,
        "<>" => Op.Ne,
        "<" => Op.Lt,
        "<=" => Op.Le,
        ">" => Op.Gt,
        _ => Op.Ge,
    };

    private void PushConstant(long value)
    {
        short v = Wrap(value);
        if (v is >= 0 and <= 15) code.Emit((byte)(Op.PushSmall + v));
        else if (v is >= 16 and <= 255) code.Emit(Op.PushByte, (byte)v);
        else if (v is >= -256 and <= -1) code.Emit(Op.PushNegative, (byte)(v + 256));
        else code.Emit(Op.PushWord, (byte)(v >> 8), (byte)v);
    }

    private static short Wrap(long value) => unchecked((short)value);

    /// <summary>The value of a constant expression, or null when it depends on run time.</summary>
    private long? Fold(Expr e)
    {
        switch (e)
        {
            case NumberExpr n:
                if (n.Value > 65535) throw new ScriptException(n.Line, $"{n.Value} is too large: numbers are 16-bit");
                return Wrap(n.Value);
            case NameExpr name:
            {
                string key = Key(name.Name);
                if (scope.Locals.ContainsKey(key) || globals.ContainsKey(key)) return null;
                if (constants.TryGetValue(key, out long c)) return c;
                if (Names.Constants.TryGetValue(key, out long k)) return k;
                return null;
            }
            case UnaryExpr u:
            {
                if (Fold(u.Operand) is not long v) return null;
                return u.Op == "NOT" ? (v == 0 ? -1 : 0) : Wrap(-v);
            }
            case BinaryExpr b:
            {
                if (Fold(b.Left) is not long l || Fold(b.Right) is not long r) return null;
                short a = Wrap(l), c = Wrap(r);
                return b.Op switch
                {
                    "+" => Wrap(a + c),
                    "-" => Wrap(a - c),
                    "*" => Wrap(a * c),
                    "/" => c == 0 ? throw new ScriptException(b.Line, "Division by zero") : Wrap(a / c),
                    "MOD" => c == 0 ? throw new ScriptException(b.Line, "Division by zero") : Wrap(a % c),
                    "AND" => a & c,
                    "OR" => a | c,
                    "XOR" => a ^ c,
                    "SHL" => Wrap(a << (c & 15)),
                    "SHR" => Wrap((ushort)a >> (c & 15)),
                    "=" => a == c ? -1 : 0,
                    "<>" => a != c ? -1 : 0,
                    "<" => a < c ? -1 : 0,
                    "<=" => a <= c ? -1 : 0,
                    ">" => a > c ? -1 : 0,
                    _ => a >= c ? -1 : 0,
                };
            }
            default:
                return null;
        }
    }

    /// <summary>LATCH settings bit 7: port D's interrupt is this LATCH's edge (docs/Bytecode.md).</summary>
    public const int SoloLatch = 0x80;

    /// <summary>
    /// A LATCH the kernel can take from port D's interrupt alone, without reading the pins
    /// back, so a pulse of any length is caught: on PD2 or PD3, the only LATCH on port D,
    /// one edge, no qualifier.
    /// </summary>
    private static bool IsSoloLatch(Handler h, IEnumerable<Handler> handlers) =>
        h.Event == "LATCH" && h.Pin is PinPd2 or PinPd3 && h.Edges is 1 or 2 && h.QualifierPin < 0
        && handlers.Count(o => o.Event == "LATCH" && o.Pin is PinPd2 or PinPd3) == 1;

    private const int PinPd2 = 3, PinPd3 = 4;
}
