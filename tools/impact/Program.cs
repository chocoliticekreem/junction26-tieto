// Copyright (c) Receipts. Licensed under the MIT license.
//
// Member-level change analysis for the "blast radius" check (notes/blast-radius.md).
//
//   Impact --base <tree> --head <tree> --changed <file with one repo-relative path per line> [--out <json>]
//
// 1. Every changed C# file under src/ (not *.g.cs) is parsed on base and head. Each method,
//    constructor, property, indexer and operator is keyed by containing type + name + parameter
//    types and classified new / modified / deleted (unchanged members are omitted).
// 2. For modified and deleted members, call sites are found in every .cs file under src/ and,
//    separately, tests/ of the HEAD tree. Matching is by NAME on syntax trees, not semantic: a call
//    is attributed to a member when the name (and argument count) fits and the receiver is not
//    visibly some unrelated type. Receivers that cannot be resolved syntactically still count, so
//    common names over-approximate; `ambiguous` and `unresolved_receivers` say how much.
// Output: one JSON object on stdout.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var sw = Stopwatch.StartNew();
var opts = ParseArgs(args);
if (!opts.TryGetValue("base", out var baseDir) || !opts.TryGetValue("head", out var headDir)
    || !opts.TryGetValue("changed", out var changedList))
{
    Console.Error.WriteLine("usage: Impact --base <tree> --head <tree> --changed <paths.txt>");
    return 2;
}

var changed = File.ReadAllLines(changedList).Select(l => l.Trim().Replace('\\', '/'))
    .Where(l => l.Length > 0).ToHashSet(StringComparer.Ordinal);
var analyzed = changed.Where(p => p.StartsWith("src/", StringComparison.Ordinal)
        && p.EndsWith(".cs", StringComparison.Ordinal) && !p.EndsWith(".g.cs", StringComparison.Ordinal))
    .OrderBy(p => p, StringComparer.Ordinal).ToList();

var members = new List<MemberResult>();
foreach (var path in analyzed)
{
    var baseDecls = Members.Collect(Parse(Path.Combine(baseDir, path)), path);
    var headDecls = Members.Collect(Parse(Path.Combine(headDir, path)), path);
    members.AddRange(Members.Diff(baseDecls, headDecls));
}

var srcFiles = Scan.ParseTree(headDir, "src", excludeResources: false);
var testFiles = Scan.ParseTree(headDir, "tests", excludeResources: true);
var index = new TypeIndex(srcFiles.Concat(testFiles).Select(f => f.Root));
var targets = members.Where(m => m.Kind != "new").ToList();
// A call inside a member this change adds or modifies is part of the change. A call from an
// unchanged member of a touched file is not: that member is old behaviour relying on the target.
var changedSpans = members.Where(m => m.Kind != "deleted").GroupBy(m => m.File)
    .ToDictionary(g => g.Key, g => g.Select(m => m.HeadSpan!).ToList());
bool InChange(string file, int line) => changedSpans.TryGetValue(file, out var spans)
    ? spans.Any(s => line >= s[0] && line <= s[1])
    : changed.Contains(file) && !analyzed.Contains(file);
Scan.FindCallers(srcFiles, targets, index, InChange, test: false);
Scan.FindCallers(testFiles, targets, index, InChange, test: true);
foreach (var m in targets)
{
    m.SameNameDeclarations = index.OtherTypesDeclaring(m.Member, m.TypeName);
    m.UnresolvedReceivers = m.Callers.Count(c => c.Receiver == "unresolved");
    m.Ambiguous = m.Undetectable || (m.SameNameDeclarations > 0 && m.UnresolvedReceivers > 0);
}

var output = new
{
    method = "name-based call sites (Roslyn syntax, not semantic)",
    analyzed_files = analyzed,
    files_scanned = new { src = srcFiles.Count, tests = testFiles.Count },
    members,
    elapsed_ms = sw.ElapsedMilliseconds,
};
var json = JsonSerializer.Serialize(output, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
});
if (opts.TryGetValue("out", out var outPath))
{
    File.WriteAllText(outPath, json);
    Console.WriteLine($"{members.Count} changed members ({targets.Count} modified or deleted), "
        + $"{srcFiles.Count} src + {testFiles.Count} test files scanned in {sw.ElapsedMilliseconds} ms");
}
else
{
    Console.WriteLine(json);
}

return 0;

static Dictionary<string, string> ParseArgs(string[] args)
{
    var d = new Dictionary<string, string>();
    for (int i = 0; i + 1 < args.Length; i += 2)
    {
        d[args[i].TrimStart('-')] = args[i + 1];
    }

    return d;
}

static CompilationUnitSyntax? Parse(string file) =>
    File.Exists(file) ? (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot() : null;

sealed class MemberDecl
{
    public required string File { get; init; }
    public required string TypePath { get; init; }
    public required string TypeName { get; init; }
    public required string Name { get; init; }
    public required string MemberKind { get; init; }
    public required string[] ParamTypes { get; init; }
    public int Required { get; init; }
    public int Total { get; init; }
    public bool HasParamsArray { get; init; }
    public bool IsExtension { get; init; }
    public required string Signature { get; init; }
    public required string Full { get; init; }
    public int Start { get; init; }
    public int End { get; init; }

    public string Key => $"{MemberKind}|{TypePath}.{Name}({string.Join(",", ParamTypes)})";

    public string Display => MemberKind == "property"
        ? $"{TypePath}.{Name}"
        : $"{TypePath}.{Name}({string.Join(", ", ParamTypes)})";
}

sealed class Caller
{
    public required string File { get; init; }
    public int Line { get; init; }
    public int StatementStart { get; init; }
    public required string Member { get; init; }
    public required int[] MemberSpan { get; init; }
    public bool InChange { get; init; }
    public required string Receiver { get; init; } // this | type | visible | none | unresolved
}

sealed class MemberResult
{
    public required string Name { get; init; }
    public required string File { get; init; }
    public required string TypeName { get; init; }
    public required string Member { get; init; }
    public required string MemberKind { get; init; }
    public required string Kind { get; init; } // new | modified | deleted
    public bool SignatureChanged { get; init; }
    public int[]? HeadSpan { get; init; }
    public int[]? BaseSpan { get; init; }
    public int ArgsMin { get; init; }
    public int ArgsMax { get; init; } // int.MaxValue for params arrays
    public bool IsExtension { get; init; }
    public bool Undetectable { get; init; } // operators and indexers: no named call site exists
    public List<Caller> Callers { get; } = new();
    public List<Caller> TestCallers { get; } = new();
    public int SameNameDeclarations { get; set; }
    public int UnresolvedReceivers { get; set; }
    public bool Ambiguous { get; set; }
}

static class Members
{
    public static List<MemberDecl> Collect(CompilationUnitSyntax? root, string file)
    {
        var list = new List<MemberDecl>();
        if (root is null)
        {
            return list;
        }

        foreach (var node in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
        {
            var d = Describe(node, file);
            if (d is not null)
            {
                list.Add(d);
            }
        }

        return list;
    }

    static MemberDecl? Describe(MemberDeclarationSyntax node, string file)
    {
        string name, kind;
        ParameterListSyntax? ps = null;
        BracketedParameterListSyntax? bps = null;
        SyntaxNode?[] bodies;
        switch (node)
        {
            case MethodDeclarationSyntax m:
                name = m.Identifier.Text; kind = "method"; ps = m.ParameterList;
                bodies = [m.Body, m.ExpressionBody];
                break;
            case ConstructorDeclarationSyntax c:
                name = c.Modifiers.Any(SyntaxKind.StaticKeyword) ? ".cctor" : ".ctor"; kind = "ctor";
                ps = c.ParameterList; bodies = [c.Body, c.ExpressionBody, c.Initializer];
                break;
            case PropertyDeclarationSyntax p:
                name = p.Identifier.Text; kind = "property";
                bodies = [p.AccessorList, p.ExpressionBody, p.Initializer];
                break;
            case IndexerDeclarationSyntax ix:
                name = "this[]"; kind = "indexer"; bps = ix.ParameterList;
                bodies = [ix.AccessorList, ix.ExpressionBody];
                break;
            case OperatorDeclarationSyntax o:
                name = "operator " + o.OperatorToken.Text; kind = "operator"; ps = o.ParameterList;
                bodies = [o.Body, o.ExpressionBody];
                break;
            case ConversionOperatorDeclarationSyntax co:
                name = $"{co.ImplicitOrExplicitKeyword.Text} operator {Compact(co.Type.ToString())}";
                kind = "operator"; ps = co.ParameterList; bodies = [co.Body, co.ExpressionBody];
                break;
            default:
                return null;
        }

        var types = node.Ancestors().OfType<BaseTypeDeclarationSyntax>().Reverse().ToList();
        if (types.Count == 0)
        {
            return null;
        }

        var parameters = (IEnumerable<ParameterSyntax>?)ps?.Parameters ?? bps?.Parameters ?? Enumerable.Empty<ParameterSyntax>();
        var plist = parameters.ToList();
        var excluded = bodies.Where(b => b is not null).Select(b => b!.Span)
            .Concat(node.AttributeLists.Select(a => a.Span)).ToList();
        var lines = node.GetLocation().GetLineSpan();
        return new MemberDecl
        {
            File = file,
            TypePath = string.Join(".", types.Select(TypeLabel)),
            TypeName = types[^1].Identifier.Text,
            Name = name,
            MemberKind = kind,
            ParamTypes = plist.Select(p => Compact(p.Type?.ToString() ?? "?")).ToArray(),
            Required = plist.Count(p => p.Default is null && !p.Modifiers.Any(SyntaxKind.ParamsKeyword)),
            Total = plist.Count,
            HasParamsArray = plist.Any(p => p.Modifiers.Any(SyntaxKind.ParamsKeyword)),
            IsExtension = plist.Count > 0 && plist[0].Modifiers.Any(SyntaxKind.ThisKeyword),
            Signature = Tokens(node, excluded),
            Full = Tokens(node, []),
            Start = lines.StartLinePosition.Line + 1,
            End = lines.EndLinePosition.Line + 1,
        };
    }

    static string TypeLabel(BaseTypeDeclarationSyntax t) =>
        t is TypeDeclarationSyntax { TypeParameterList: { } tp } ? $"{t.Identifier.Text}`{tp.Parameters.Count}" : t.Identifier.Text;

    public static string Compact(string s) => string.Concat(s.Where(ch => !char.IsWhiteSpace(ch)));

    static string Tokens(SyntaxNode node, List<Microsoft.CodeAnalysis.Text.TextSpan> excluded) =>
        string.Join(" ", node.DescendantTokens()
            .Where(t => !excluded.Any(s => s.Contains(t.Span)))
            .Select(t => t.Text));

    public static IEnumerable<MemberResult> Diff(List<MemberDecl> baseDecls, List<MemberDecl> headDecls)
    {
        var baseByKey = baseDecls.GroupBy(d => d.Key).ToDictionary(g => g.Key, g => g.First());
        var headByKey = headDecls.GroupBy(d => d.Key).ToDictionary(g => g.Key, g => g.First());
        var results = new List<MemberResult>();
        foreach (var (key, h) in headByKey)
        {
            if (baseByKey.TryGetValue(key, out var b) && b.Full != h.Full)
            {
                results.Add(Result(h, b, "modified", b.Signature != h.Signature));
            }
        }

        // A parameter-type change moves a member to a new key. When exactly one unmatched member of
        // the same type, name and kind exists on each side, it is one member whose signature changed.
        var baseOnly = baseByKey.Where(kv => !headByKey.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList();
        var headOnly = headByKey.Where(kv => !baseByKey.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList();
        foreach (var group in headOnly.GroupBy(h => (h.TypePath, h.Name, h.MemberKind)).ToList())
        {
            var olds = baseOnly.Where(b => (b.TypePath, b.Name, b.MemberKind) == group.Key).ToList();
            if (olds.Count == 1 && group.Count() == 1)
            {
                results.Add(Result(group.First(), olds[0], "modified", true));
                baseOnly.Remove(olds[0]);
                headOnly.Remove(group.First());
            }
        }

        results.AddRange(headOnly.Select(h => Result(h, null, "new", false)));
        results.AddRange(baseOnly.Select(b => Result(null, b, "deleted", false)));
        return results.OrderBy(r => r.File, StringComparer.Ordinal)
            .ThenBy(r => (r.HeadSpan ?? r.BaseSpan)![0]);
    }

    static MemberResult Result(MemberDecl? head, MemberDecl? baseDecl, string kind, bool sigChanged)
    {
        var d = head ?? baseDecl!;
        int min = d.Required, max = d.HasParamsArray ? int.MaxValue : d.Total;
        return new MemberResult
        {
            Name = d.Display,
            File = d.File,
            TypeName = d.TypeName,
            Member = d.Name,
            MemberKind = d.MemberKind,
            Kind = kind,
            SignatureChanged = sigChanged,
            HeadSpan = head is null ? null : [head.Start, head.End],
            BaseSpan = baseDecl is null ? null : [baseDecl.Start, baseDecl.End],
            ArgsMin = min,
            ArgsMax = max,
            IsExtension = d.IsExtension,
            Undetectable = d.MemberKind is "operator" or "indexer",
        };
    }
}

sealed record ParsedFile(string Path, CompilationUnitSyntax Root);

sealed class TypeIndex
{
    readonly Dictionary<string, HashSet<string>> bases = new(StringComparer.Ordinal);
    readonly Dictionary<string, HashSet<string>> declaringTypes = new(StringComparer.Ordinal);

    public TypeIndex(IEnumerable<CompilationUnitSyntax> roots)
    {
        foreach (var root in roots)
        {
            foreach (var t in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                var set = Get(bases, t.Identifier.Text);
                foreach (var b in t.BaseList?.Types ?? default)
                {
                    set.Add(Scan.RightName(b.Type));
                }
            }

            foreach (var m in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
            {
                var name = m switch
                {
                    MethodDeclarationSyntax x => x.Identifier.Text,
                    PropertyDeclarationSyntax x => x.Identifier.Text,
                    _ => null,
                };
                var owner = m.Parent as BaseTypeDeclarationSyntax;
                if (name is not null && owner is not null)
                {
                    Get(declaringTypes, name).Add(owner.Identifier.Text);
                }
            }
        }
    }

    static HashSet<string> Get(Dictionary<string, HashSet<string>> d, string key) =>
        d.TryGetValue(key, out var s) ? s : d[key] = new HashSet<string>(StringComparer.Ordinal);

    public bool IsType(string name) => bases.ContainsKey(name);

    public int OtherTypesDeclaring(string member, string type) =>
        declaringTypes.TryGetValue(member, out var s) ? s.Count(t => t != type) : 0;

    HashSet<string> Ancestry(string type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>([type]);
        while (stack.Count > 0 && seen.Count < 64)
        {
            var t = stack.Pop();
            if (!seen.Add(t))
            {
                continue;
            }

            if (bases.TryGetValue(t, out var bs))
            {
                foreach (var b in bs)
                {
                    stack.Push(b);
                }
            }
        }

        return seen;
    }

    /// <summary>A member of <paramref name="target"/> can be reached through a value of
    /// <paramref name="receiver"/> when one type derives from (or implements) the other.</summary>
    public bool Related(string receiver, string target) =>
        receiver == target || Ancestry(target).Contains(receiver) || Ancestry(receiver).Contains(target);
}

static class Scan
{
    public static List<ParsedFile> ParseTree(string root, string sub, bool excludeResources)
    {
        var dir = Path.Combine(root, sub);
        if (!Directory.Exists(dir))
        {
            return new();
        }

        var files = Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(rel => !rel.Split('/').Any(s => s is "bin" or "obj")
                && !(excludeResources && rel.Contains("/Resources/", StringComparison.Ordinal)))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        var parsed = new ConcurrentBag<ParsedFile>();
        Parallel.ForEach(files, rel =>
            parsed.Add(new ParsedFile(rel, (CompilationUnitSyntax)CSharpSyntaxTree
                .ParseText(File.ReadAllText(Path.Combine(root, rel))).GetRoot())));
        return parsed.OrderBy(p => p.Path, StringComparer.Ordinal).ToList();
    }

    public static string RightName(TypeSyntax t) => t switch
    {
        IdentifierNameSyntax i => i.Identifier.Text,
        GenericNameSyntax g => g.Identifier.Text,
        QualifiedNameSyntax q => RightName(q.Right),
        AliasQualifiedNameSyntax a => RightName(a.Name),
        NullableTypeSyntax n => RightName(n.ElementType),
        _ => t.ToString(),
    };

    public static void FindCallers(List<ParsedFile> files, List<MemberResult> targets, TypeIndex index,
                                   Func<string, int, bool> inChange, bool test)
    {
        var byName = targets.Where(t => t.MemberKind is "method" or "property")
            .GroupBy(t => t.Member).ToDictionary(g => g.Key, g => g.ToList());
        var ctors = targets.Where(t => t.MemberKind == "ctor" && t.Member == ".ctor")
            .GroupBy(t => t.TypeName).ToDictionary(g => g.Key, g => g.ToList());
        var found = new ConcurrentBag<(MemberResult Target, Caller Caller)>();
        Parallel.ForEach(files, f =>
        {
            foreach (var node in f.Root.DescendantNodes())
            {
                switch (node)
                {
                    case SimpleNameSyntax sn when byName.TryGetValue(sn.Identifier.Text, out var cands):
                        foreach (var hit in MatchName(sn, cands, index))
                        {
                            found.Add((hit.Target, MakeCaller(f.Path, sn, hit.Receiver, inChange)));
                        }

                        break;
                    case BaseObjectCreationExpressionSyntax oc:
                        var typeName = CreatedType(oc);
                        if (typeName is not null && ctors.TryGetValue(typeName, out var cs))
                        {
                            var n = oc.ArgumentList?.Arguments.Count ?? 0;
                            foreach (var t in cs.Where(t => n >= t.ArgsMin && n <= t.ArgsMax))
                            {
                                found.Add((t, MakeCaller(f.Path, oc, "visible", inChange)));
                            }
                        }

                        break;
                    case ConstructorInitializerSyntax ci:
                        var self = ci.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
                        if (self is null)
                        {
                            break;
                        }

                        var argc = ci.ArgumentList.Arguments.Count;
                        IEnumerable<string> owners = ci.ThisOrBaseKeyword.IsKind(SyntaxKind.ThisKeyword)
                            ? [self.Identifier.Text]
                            : (self.BaseList?.Types ?? default).Select(b => RightName(b.Type));
                        foreach (var owner in owners)
                        {
                            if (ctors.TryGetValue(owner, out var cs2))
                            {
                                foreach (var t in cs2.Where(t => argc >= t.ArgsMin && argc <= t.ArgsMax))
                                {
                                    found.Add((t, MakeCaller(f.Path, ci, "this", inChange)));
                                }
                            }
                        }

                        break;
                }
            }
        });
        foreach (var (target, caller) in found.OrderBy(x => x.Caller.File, StringComparer.Ordinal)
                     .ThenBy(x => x.Caller.Line))
        {
            (test ? target.TestCallers : target.Callers).Add(caller);
        }
    }

    static string? CreatedType(BaseObjectCreationExpressionSyntax oc) => oc switch
    {
        ObjectCreationExpressionSyntax o => RightName(o.Type),
        // `T x = new(...)`: the declared type is visible on the declaration.
        ImplicitObjectCreationExpressionSyntax { Parent: EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax vd } } }
            when !vd.Type.IsVar => RightName(vd.Type),
        _ => null,
    };

    static IEnumerable<(MemberResult Target, string Receiver)> MatchName(SimpleNameSyntax sn, List<MemberResult> cands, TypeIndex index)
    {
        if (sn.Parent is QualifiedNameSyntax or NameEqualsSyntax or NameColonSyntax
            || sn.Ancestors().Any(a => a is UsingDirectiveSyntax)
            || sn.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Any(ns => ns.Name.Span.Contains(sn.Span))
            || IsTypePosition(sn))
        {
            yield break;
        }

        ExpressionSyntax outer = sn;
        ExpressionSyntax? receiver = null;
        var conditional = false;
        if (sn.Parent is MemberAccessExpressionSyntax ma && ma.Name == sn)
        {
            receiver = ma.Expression;
            outer = ma;
        }
        else if (sn.Parent is MemberBindingExpressionSyntax mb)
        {
            conditional = true;
            outer = mb;
        }

        var invocation = outer.Parent as InvocationExpressionSyntax;
        var invoked = invocation is not null && invocation.Expression == outer;
        var asArgument = outer.Parent is ArgumentSyntax;
        var initializerTarget = ObjectInitializerType(sn);

        foreach (var t in cands)
        {
            if (t.MemberKind == "method")
            {
                if (!(invoked || asArgument))
                {
                    continue;
                }

                if (invoked)
                {
                    var n = invocation!.ArgumentList.Arguments.Count;
                    var extensionCall = t.IsExtension && receiver is not null && n + 1 >= t.ArgsMin && n + 1 <= t.ArgsMax;
                    if (!extensionCall && (n < t.ArgsMin || n > t.ArgsMax))
                    {
                        continue;
                    }
                }
            }
            else if (invoked)
            {
                continue;
            }

            string mode;
            if (initializerTarget is not null)
            {
                if (!index.Related(initializerTarget, t.TypeName))
                {
                    continue;
                }

                mode = "visible";
            }
            else if (conditional)
            {
                mode = "unresolved";
            }
            else if (receiver is null)
            {
                if (t.MemberKind == "property" && DeclaredLocally(sn) is not null)
                {
                    continue;
                }

                var enclosing = sn.Ancestors().OfType<BaseTypeDeclarationSyntax>().Select(x => x.Identifier.Text);
                if (enclosing.Any(e => index.Related(e, t.TypeName)))
                {
                    mode = "this";
                }
                else if (t.IsExtension || UsingStatic(sn, t.TypeName))
                {
                    mode = "type";
                }
                else
                {
                    continue;
                }
            }
            else
            {
                var (type, how) = Resolve(receiver, index);
                if (type is null)
                {
                    mode = "unresolved";
                }
                else if (type == t.TypeName || (t.IsExtension && how != "type"))
                {
                    mode = how;
                }
                else if (index.Related(type, t.TypeName))
                {
                    mode = "related";
                }
                else
                {
                    continue;
                }
            }

            yield return (t, mode);
        }
    }

    static bool IsTypePosition(SimpleNameSyntax sn) => sn.Parent switch
    {
        VariableDeclarationSyntax vd => vd.Type == sn,
        ParameterSyntax p => p.Type == sn,
        ObjectCreationExpressionSyntax oc => oc.Type == sn,
        TypeArgumentListSyntax or BaseTypeSyntax or TypeConstraintSyntax or ArrayTypeSyntax or NullableTypeSyntax => true,
        PropertyDeclarationSyntax pd => pd.Type == sn,
        MethodDeclarationSyntax md => md.ReturnType == sn,
        CastExpressionSyntax ce => ce.Type == sn,
        TypeOfExpressionSyntax or DefaultExpressionSyntax => true,
        AttributeSyntax => true,
        _ => false,
    };

    static string? ObjectInitializerType(SimpleNameSyntax sn) =>
        sn.Parent is AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax init } asg
        && asg.Left == sn && init.IsKind(SyntaxKind.ObjectInitializerExpression)
        && init.Parent is BaseObjectCreationExpressionSyntax oc
            ? CreatedType(oc) ?? "?"
            : null;

    static bool UsingStatic(SyntaxNode at, string type) =>
        at.SyntaxTree.GetCompilationUnitRoot().Usings
            .Any(u => u.StaticKeyword != default && RightName(u.Name!) == type);

    /// <summary>Declared type of a local, parameter, foreach or pattern variable named like
    /// <paramref name="id"/> in the enclosing members; "" when declared but the type is not visible
    /// (var without `new T`), null when not declared locally.</summary>
    static string? DeclaredLocally(SimpleNameSyntax id)
    {
        var name = id.Identifier.Text;
        foreach (var scope in id.Ancestors())
        {
            if (scope is BaseTypeDeclarationSyntax)
            {
                break;
            }

            foreach (var n in scope.ChildNodes().SelectMany(c => c.DescendantNodesAndSelf()))
            {
                switch (n)
                {
                    case ParameterSyntax p when p.Identifier.Text == name:
                        return p.Type is null ? "" : RightName(p.Type);
                    case VariableDeclaratorSyntax v when v.Identifier.Text == name && v.Parent is VariableDeclarationSyntax vd
                                                         && vd.Parent is not FieldDeclarationSyntax:
                        if (!vd.Type.IsVar)
                        {
                            return RightName(vd.Type);
                        }

                        return v.Initializer?.Value is ObjectCreationExpressionSyntax oc ? RightName(oc.Type) : "";
                    case ForEachStatementSyntax fe when fe.Identifier.Text == name:
                        return fe.Type.IsVar ? "" : RightName(fe.Type);
                    case DeclarationPatternSyntax { Designation: SingleVariableDesignationSyntax d } dp when d.Identifier.Text == name:
                        return RightName(dp.Type);
                }
            }

            if (scope is BaseMethodDeclarationSyntax or BasePropertyDeclarationSyntax)
            {
                break;
            }
        }

        return null;
    }

    static string? MemberTypeInEnclosingTypes(SimpleNameSyntax id)
    {
        var name = id.Identifier.Text;
        foreach (var t in id.Ancestors().OfType<TypeDeclarationSyntax>())
        {
            foreach (var m in t.Members)
            {
                switch (m)
                {
                    case FieldDeclarationSyntax f when f.Declaration.Variables.Any(v => v.Identifier.Text == name):
                        return RightName(f.Declaration.Type);
                    case PropertyDeclarationSyntax p when p.Identifier.Text == name:
                        return RightName(p.Type);
                }
            }
        }

        return null;
    }

    static (string? Type, string How) Resolve(ExpressionSyntax recv, TypeIndex index)
    {
        switch (recv)
        {
            case ThisExpressionSyntax or BaseExpressionSyntax:
                return (recv.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text, "this");
            case ParenthesizedExpressionSyntax p:
                return Resolve(p.Expression, index);
            case CastExpressionSyntax c:
                return (RightName(c.Type), "visible");
            case ObjectCreationExpressionSyntax oc:
                return (RightName(oc.Type), "visible");
            case IdentifierNameSyntax id:
                var local = DeclaredLocally(id);
                if (local is not null)
                {
                    return (local == "" ? null : local, "visible");
                }

                var member = MemberTypeInEnclosingTypes(id);
                if (member is not null)
                {
                    return (member, "visible");
                }

                return index.IsType(id.Identifier.Text) ? (id.Identifier.Text, "type") : (null, "unresolved");
            case MemberAccessExpressionSyntax { Name: IdentifierNameSyntax last } ma when index.IsType(last.Identifier.Text)
                                                                                     && ma.Parent is MemberAccessExpressionSyntax:
                return (last.Identifier.Text, "type"); // Namespace.Type.Member(...)
            default:
                return (null, "unresolved");
        }
    }

    static Caller MakeCaller(string file, SyntaxNode at, string receiver, Func<string, int, bool> inChange)
    {
        var line = at.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        SyntaxNode anchor = at.Ancestors().FirstOrDefault(a => a is StatementSyntax or ArrowExpressionClauseSyntax
            or EqualsValueClauseSyntax or ConstructorInitializerSyntax) ?? at;
        var stmt = anchor.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        return new Caller
        {
            File = file,
            Line = line,
            StatementStart = Math.Min(stmt, line),
            Member = EnclosingMember(at),
            MemberSpan = EnclosingSpan(at),
            InChange = inChange(file, line),
            Receiver = receiver,
        };
    }

    static int[] EnclosingSpan(SyntaxNode at)
    {
        SyntaxNode m = at.Ancestors().OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(a => a is not BaseTypeDeclarationSyntax and not BaseNamespaceDeclarationSyntax) ?? at;
        var span = m.GetLocation().GetLineSpan();
        return [span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1];
    }

    static string EnclosingMember(SyntaxNode at)
    {
        var m = at.Ancestors().OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(a => a is not BaseTypeDeclarationSyntax and not BaseNamespaceDeclarationSyntax);
        var type = at.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "?";
        var name = m switch
        {
            MethodDeclarationSyntax x => x.Identifier.Text,
            ConstructorDeclarationSyntax => ".ctor",
            PropertyDeclarationSyntax x => x.Identifier.Text,
            IndexerDeclarationSyntax => "this[]",
            FieldDeclarationSyntax x => string.Join(",", x.Declaration.Variables.Select(v => v.Identifier.Text)),
            OperatorDeclarationSyntax x => "operator " + x.OperatorToken.Text,
            EventDeclarationSyntax x => x.Identifier.Text,
            _ => "(type level)",
        };
        return $"{type}.{name}";
    }
}
