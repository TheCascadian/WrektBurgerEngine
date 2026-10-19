using System.Text;

namespace Burger.Core.Script;

/// <summary>
/// Parser for the Paradox/Clausewitz-style text format used by every BurgerEngine data file:
/// <code>
/// # comment
/// key = value
/// key = "quoted value"
/// key = { child = 1 other = { 1 2 3 } }
/// gold &gt;= 100
/// color = rgb { 255 0 0 }
/// @my_constant = 10        # file-local constant, use as  value = @my_constant
/// </code>
/// Parse errors never throw; they are written to <see cref="Log"/> with file and line, and parsing continues.
/// </summary>
public static class ScriptParser
{
    private enum Tok { Word, String, LBrace, RBrace, Op, Eof }

    private struct Token
    {
        public Tok Type;
        public string Text;
        public Op Op;
        public int Line;
        public int Start, End;
    }

    private static readonly HashSet<string> TypedBlockPrefixes = new(StringComparer.OrdinalIgnoreCase) { "rgb", "hsv", "hsv360", "list" };

    public static Node ParseFile(string path, string? displayName = null)
    {
        string text;
        try
        {
            text = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Err($"Could not read file: {ex.Message}", displayName ?? path);
            return Node.Root(displayName ?? path);
        }
        return Parse(text, displayName ?? path);
    }

    [ThreadStatic] private static List<(string Message, string Where)>? _sink;

    private static void Err(string msg, string where)
    {
        if (_sink != null) _sink.Add((msg, where));
        else Log.Error(msg, where);
    }

    /// <summary>Parses without writing to the log; syntax errors are returned in <paramref name="errors"/> ("file:line").</summary>
    public static Node ParseQuiet(string text, string fileName, List<(string Message, string Where)>? errors = null)
    {
        var old = _sink;
        _sink = errors ?? new List<(string, string)>();
        try { return Parse(text, fileName); }
        finally { _sink = old; }
    }

    /// <summary>
    /// Hard limits for text that does not come from a trusted file (a save sent by a multiplayer host). The cost that matters is
    /// memory, so it is limited directly: the bytes the parse allocates on this thread are measured as it goes. Nesting is limited
    /// because a stack overflow cannot be caught; string length because one string is one allocation.
    /// </summary>
    public sealed record ParseLimits(int MaxDepth, int MaxStringLength, long MaxAllocBytes)
    {
        /// <summary>No limit but nesting: a depth beyond this would overflow the stack, which no handler can catch.</summary>
        public static readonly ParseLimits Trusted = new(256, int.MaxValue, long.MaxValue);
        /// <summary>Set from measured late-game saves (docs/multiplayer-security.md).</summary>
        // Presence vectors in large historical saves are single quoted values; World500BCE measures up to ~18 KB.
        // Keep the bound finite while allowing legitimate late-game saves to pass multiplayer resync validation.
        public static ParseLimits Save = new(40, 65536, 2L << 30);
    }

    [ThreadStatic] private static ParseLimits? _limits;
    [ThreadStatic] private static long _allocLimit;
    [ThreadStatic] private static int _sinceCheck;
    /// <summary>Statistics of the last text parsed on this thread (for measuring saves against the limits).</summary>
    [ThreadStatic] public static int LastTokenCount, LastNodeCount;
    [ThreadStatic] public static long LastAllocBytes;
    private static ParseLimits Limits => _limits ?? ParseLimits.Trusted;

    /// <summary>Parses untrusted text. Throws <see cref="InvalidDataException"/> when a limit is exceeded or a syntax error is found.</summary>
    public static Node ParseStrict(string text, string fileName, ParseLimits limits)
    {
        var old = _limits; var oldSink = _sink; var errors = new List<(string Message, string Where)>();
        _limits = limits; _sink = errors;
        try
        {
            var root = Parse(text, fileName);
            if (errors.Count > 0) throw new InvalidDataException("syntax error: " + errors[0].Message);
            return root;
        }
        finally { _limits = old; _sink = oldSink; }
    }

    public static Node Parse(string text, string fileName)
    {
        long alloc0 = GC.GetAllocatedBytesForCurrentThread();
        _allocLimit = Limits.MaxAllocBytes == long.MaxValue ? long.MaxValue : alloc0 + Limits.MaxAllocBytes;
        _sinceCheck = 0; LastNodeCount = 0;
        var lex = new Lexer(text, fileName, Limits.MaxStringLength, Limits.MaxAllocBytes != long.MaxValue);
        var root = Node.Root(fileName);
        var constants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ParseStatements(lex, root, fileName, constants, isRoot: true);
        LastTokenCount = lex.Count;
        LastAllocBytes = GC.GetAllocatedBytesForCurrentThread() - alloc0;
        return root;
    }

    /// <summary>Called for every token and entry: stops the parse once it has allocated more than the limit.</summary>
    private static void Spend()
    {
        if (++_sinceCheck < 4096) return;
        _sinceCheck = 0;
        if (GC.GetAllocatedBytesForCurrentThread() > _allocLimit) throw new InvalidDataException("the save needs too much memory to read");
    }

    /// <summary>Reads tokens one at a time from the text (no token list: memory stays that of the tree being built). Two tokens of look-ahead.</summary>
    private sealed class Lexer
    {
        private readonly string _s, _file;
        private readonly int _maxStr;
        private int _i, _line = 1;
        private readonly Token[] _buf = new Token[3];
        private int _head, _n;
        public int Count;
        /// <summary>End offset of the last token taken.</summary>
        public int LastEnd;
        // repeated keys and values share one string; bounded so a hostile text cannot grow the pool itself without limit
        private readonly Dictionary<string, string>? _pool;
        private readonly Dictionary<ReadOnlyMemory<char>, string> _wordPool;
        private const int PoolMax = 1 << 16, PoolLen = 48;

        public Lexer(string s, string file, int maxStr, bool untrusted)
        {
            _s = s; _file = file; _maxStr = maxStr;
            if (s.Length > 0 && s[0] == '\uFEFF') _i = 1; // BOM
            _wordPool = new Dictionary<ReadOnlyMemory<char>, string>(MemoryComparer.Instance);
            _pool = untrusted ? new Dictionary<string, string>(StringComparer.Ordinal) : null;
        }

        public Token Peek(int k = 0)
        {
            while (_n <= k) { _buf[(_head + _n) % 3] = Read(); _n++; }
            return _buf[(_head + k) % 3];
        }

        public Token Take()
        {
            var t = Peek();
            _head = (_head + 1) % 3; _n--;
            LastEnd = t.End;
            return t;
        }

        private string Word(int start, int len)
        {
            if (len > PoolLen) return _s.Substring(start, len);
            var key = _s.AsMemory(start, len);
            if (_wordPool.TryGetValue(key, out var w)) return w;
            w = _s.Substring(start, len);
            if (_wordPool.Count < PoolMax) _wordPool[w.AsMemory()] = w;
            return w;
        }

        private string Quoted(string text)
        {
            if (_pool == null || text.Length > PoolLen) return text;
            if (_pool.TryGetValue(text, out var p)) return p;
            if (_pool.Count < PoolMax) _pool[text] = text;
            return text;
        }

        private Token Read()
        {
            var s = _s;
            int i = _i;
            Spend();
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\n') { _line++; i++; continue; }
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '#')
                {
                    while (i < s.Length && s[i] != '\n') i++;
                    continue;
                }
                Count++;
                if (c == '{') { _i = i + 1; return new Token { Type = Tok.LBrace, Text = "{", Line = _line, Start = i, End = i + 1 }; }
                if (c == '}') { _i = i + 1; return new Token { Type = Tok.RBrace, Text = "}", Line = _line, Start = i, End = i + 1 }; }
                if (c == '=' || c == '<' || c == '>' || ((c == '!' || c == '?') && i + 1 < s.Length && s[i + 1] == '='))
                {
                    int opStart = i;
                    Op op;
                    if (c == '=')
                    {
                        op = Op.Eq;
                        i++;
                        if (i < s.Length && s[i] == '=') i++; // allow ==
                    }
                    else if (c == '<')
                    {
                        if (i + 1 < s.Length && s[i + 1] == '=') { op = Op.Le; i += 2; } else { op = Op.Lt; i++; }
                    }
                    else if (c == '>')
                    {
                        if (i + 1 < s.Length && s[i + 1] == '=') { op = Op.Ge; i += 2; } else { op = Op.Gt; i++; }
                    }
                    else if (c == '!') { op = Op.Ne; i += 2; }
                    else { op = Op.SafeEq; i += 2; }
                    _i = i;
                    return new Token { Type = Tok.Op, Op = op, Text = Node.OpToString(op), Line = _line, Start = opStart, End = i };
                }
                if (c == '"')
                {
                    int startLine = _line, strStart = i;
                    var sb = new StringBuilder();
                    i++;
                    bool closed = false;
                    while (i < s.Length)
                    {
                        char d = s[i];
                        if (d == '\\' && i + 1 < s.Length)
                        {
                            char e = s[i + 1];
                            sb.Append(e switch { 'n' => '\n', 't' => '\t', _ => e });
                            i += 2;
                            continue;
                        }
                        if (d == '"') { closed = true; i++; break; }
                        if (d == '\n') _line++;
                        if (sb.Length > _maxStr) throw new InvalidDataException("string too long");
                        sb.Append(d);
                        i++;
                    }
                    if (!closed) Err("Unterminated quoted string", $"{_file}:{startLine}");
                    _i = i;
                    return new Token { Type = Tok.String, Text = Quoted(sb.ToString()), Line = startLine, Start = strStart, End = i };
                }
                // bare word
                int start = i;
                while (i < s.Length)
                {
                    char d = s[i];
                    if (i - start > _maxStr) throw new InvalidDataException("word too long");
                    if (char.IsWhiteSpace(d) || d == '{' || d == '}' || d == '=' || d == '<' || d == '>' || d == '#' || d == '"') break;
                    if ((d == '!' || d == '?') && i + 1 < s.Length && s[i + 1] == '=') break;
                    i++;
                }
                if (i == start) { i++; Count--; continue; } // should not happen; skip the char
                _i = i;
                return new Token { Type = Tok.Word, Text = Word(start, i - start), Line = _line, Start = start, End = i };
            }
            _i = i;
            return new Token { Type = Tok.Eof, Text = "", Line = _line, Start = s.Length, End = s.Length };
        }
    }

    private sealed class MemoryComparer : IEqualityComparer<ReadOnlyMemory<char>>
    {
        public static readonly MemoryComparer Instance = new();
        public bool Equals(ReadOnlyMemory<char> a, ReadOnlyMemory<char> b) => a.Span.SequenceEqual(b.Span);
        public int GetHashCode(ReadOnlyMemory<char> m) => string.GetHashCode(m.Span);
    }

    [ThreadStatic] private static int _depth;

    private static void ParseStatements(Lexer t, Node parent, string file, Dictionary<string, string> constants, bool isRoot)
    {
        if (++_depth > Limits.MaxDepth) { _depth = 0; throw new InvalidDataException("blocks nested too deeply"); }
        try { ParseStatementsCore(t, parent, file, constants, isRoot); }
        finally { if (_depth > 0) _depth--; }
    }

    private static void ParseStatementsCore(Lexer t, Node parent, string file, Dictionary<string, string> constants, bool isRoot)
    {
        while (true)
        {
            var tok = t.Peek();
            if (tok.Type == Tok.Eof)
            {
                if (!isRoot) Err($"Missing closing '}}' for block '{parent.Key}' opened at line {parent.Line}", $"{file}:{tok.Line}");
                return;
            }
            if (tok.Type == Tok.RBrace)
            {
                t.Take();
                if (isRoot)
                {
                    Err("Unexpected '}' (too many closing braces)", $"{file}:{tok.Line}");
                    continue;
                }
                return;
            }
            if (tok.Type == Tok.LBrace)
            {
                // anonymous block, e.g. inside a list of blocks
                t.Take();
                var anon = new Node { Key = "", Op = Op.None, Children = new List<Node>(), File = file, Line = tok.Line, SpanStart = tok.Start };
                ParseStatements(t, anon, file, constants, isRoot: false);
                anon.SpanEnd = t.LastEnd;
                Added();
                parent.Children!.Add(anon);
                continue;
            }
            if (tok.Type == Tok.Op)
            {
                Err($"Unexpected operator '{tok.Text}' (missing key?)", $"{file}:{tok.Line}");
                t.Take();
                // skip the value too
                if (t.Peek().Type == Tok.Word || t.Peek().Type == Tok.String) t.Take();
                else if (t.Peek().Type == Tok.LBrace) { t.Take(); SkipBlock(t); }
                continue;
            }

            // word or string: key
            t.Take();
            var node = new Node { Key = tok.Text, KeyQuoted = tok.Type == Tok.String, File = file, Line = tok.Line, SpanStart = tok.Start };
            if (t.Peek().Type == Tok.Op)
            {
                node.Op = t.Take().Op;
                var v = t.Peek();
                if (v.Type == Tok.LBrace)
                {
                    t.Take();
                    node.Children = new List<Node>();
                    ParseStatements(t, node, file, constants, isRoot: false);
                }
                else if (v.Type == Tok.Word || v.Type == Tok.String)
                {
                    t.Take();
                    if (v.Type == Tok.Word && t.Peek().Type == Tok.LBrace && TypedBlockPrefixes.Contains(v.Text))
                    {
                        t.Take();
                        node.BlockType = v.Text.ToLowerInvariant();
                        node.Children = new List<Node>();
                        ParseStatements(t, node, file, constants, isRoot: false);
                    }
                    else
                    {
                        string val = v.Text;
                        if (v.Type == Tok.Word && val.StartsWith('@') && val.Length > 1)
                        {
                            if (constants.TryGetValue(val, out var cv)) val = cv;
                            else Err($"Unknown constant '{val}'", $"{file}:{v.Line}");
                        }
                        node.Value = val;
                        node.ValueQuoted = v.Type == Tok.String;
                    }
                }
                else
                {
                    Err($"Missing value after '{node.Key} {Node.OpToString(node.Op)}'", $"{file}:{tok.Line}");
                    node.Value = "";
                }

                if (node.Key.StartsWith('@') && node.Value != null && isRoot)
                {
                    constants[node.Key] = node.Value;
                    continue; // constants are not part of the data
                }
            }
            else
            {
                node.Op = Op.None; // list item
                if (!node.KeyQuoted && node.Key.StartsWith('@') && constants.TryGetValue(node.Key, out var cv)) node.Key = cv;
            }
            node.SpanEnd = t.LastEnd;
            Added();
            parent.Children!.Add(node);
        }
    }

    private static void Added() { LastNodeCount++; Spend(); }

    private static void SkipBlock(Lexer t)
    {
        int depth = 1;
        while (depth > 0 && t.Peek().Type != Tok.Eof)
        {
            var k = t.Take().Type;
            if (k == Tok.LBrace) depth++;
            else if (k == Tok.RBrace) depth--;
        }
    }
}
