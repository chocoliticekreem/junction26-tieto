// Compatibility byte harness for MessagePack-CSharp.
//   CompatHarness                       -> {case: hex} for every sample, stdout JSON
//   CompatHarness --roundtrip <file>    -> {case: "ok" | "mismatch: ..." | "error: ..."} reading {case: hex} bytes
//   CompatHarness --api                 -> sorted public API lines of MessagePack + MessagePack.Annotations
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using CompatHarness;
using MessagePack;
using MessagePack.Resolvers;

var cases = Cases.All();
var json = new JsonSerializerOptions { WriteIndented = true };

if (args.Length >= 1 && args[0] == "--api")
{
    Console.WriteLine(JsonSerializer.Serialize(Api.Listing(), json));
    return 0;
}

if (args.Length >= 2 && args[0] == "--roundtrip")
{
    var fixtures = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(args[1]));
    var byName = cases.ToDictionary(c => c.Name);
    var results = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (var (name, hex) in fixtures)
    {
        if (!byName.TryGetValue(name, out var c))
        {
            results[name] = "error: case not defined in this harness";
            continue;
        }

        results[name] = RoundTrip(c, hex);
    }

    Console.WriteLine(JsonSerializer.Serialize(results, json));
    return 0;
}

var output = new SortedDictionary<string, string>(StringComparer.Ordinal);
foreach (var c in cases)
{
    try
    {
        output[c.Name] = Convert.ToHexString(MessagePackSerializer.Serialize(c.Type, c.Value, c.Options)).ToLowerInvariant();
    }
    catch (Exception e)
    {
        output[c.Name] = $"error: {e.GetType().Name}: {e.Message}";
    }
}

Console.WriteLine(JsonSerializer.Serialize(output, json));
return 0;

// Head reads the base bytes, then the read value is compared with the original sample by
// serializing both with head. This isolates reader changes from writer changes.
static string RoundTrip(Case c, string hex)
{
    if (hex.StartsWith("error:", StringComparison.Ordinal))
    {
        return "skipped: base could not serialize this case";
    }

    try
    {
        var bytes = Convert.FromHexString(hex);
        var read = MessagePackSerializer.Deserialize(c.Type, bytes, c.Options);
        var fromRead = MessagePackSerializer.Serialize(c.Type, read, c.Options);
        var fromOriginal = MessagePackSerializer.Serialize(c.Type, c.Value, c.Options);
        return fromRead.AsSpan().SequenceEqual(fromOriginal)
            ? "ok"
            : $"mismatch: read back as {Convert.ToHexString(fromRead).ToLowerInvariant()}";
    }
    catch (Exception e)
    {
        return $"error: {e.GetType().Name}: {e.Message}";
    }
}

public record Case(string Name, Type Type, object Value, MessagePackSerializerOptions Options);

public static class Cases
{
    static readonly MessagePackSerializerOptions Std = MessagePackSerializerOptions.Standard;
    static readonly MessagePackSerializerOptions Contractless = ContractlessStandardResolver.Options;
    static readonly MessagePackSerializerOptions Lz4 = Std.WithCompression(MessagePackCompression.Lz4BlockArray);
    static readonly MessagePackSerializerOptions OldSpec = Std.WithOldSpec();
    static readonly MessagePackSerializerOptions EnumAsString = Std.WithResolver(
        CompositeResolver.Create(DynamicEnumAsStringResolver.Instance, StandardResolver.Instance));

    public static List<Case> All()
    {
        var list = new List<Case>();
        void Add<T>(string name, T value, MessagePackSerializerOptions options = null) =>
            list.Add(new Case(name, typeof(T), value, options ?? Std));

        // Integers on every MessagePack encoding boundary (fixint, uint8/16/32/64, int8/16/32/64).
        foreach (var v in new[] { 0, 1, 127, 128, 255, 256, 65535, 65536, -1, -32, -33, -128, -129, -32768, -32769, int.MinValue, int.MaxValue })
        {
            Add($"int/{v}", v);
        }

        foreach (var v in new[] { long.MinValue, long.MaxValue, 4294967295L, 4294967296L, -2147483649L, -128L, -129L, -33L })
        {
            Add($"long/{v}", v);
        }

        // Each integer width has its own writer overload, so boundaries are repeated per type.
        foreach (var v in new short[] { short.MinValue, short.MaxValue, -32, -33, -128, -129, -300, 127, 128, 255, 256 })
        {
            Add($"short/{v}", v);
        }

        foreach (var v in new sbyte[] { sbyte.MinValue, sbyte.MaxValue, -32, -33, -100 })
        {
            Add($"sbyte/{v}", v);
        }

        Add("uint/max", uint.MaxValue);
        Add("uint/128", 128u);
        Add("ulong/max", ulong.MaxValue);
        Add("ulong/65536", 65536ul);
        Add("byte/200", (byte)200);
        Add("byte/max", byte.MaxValue);
        Add("ushort/60000", (ushort)60000);
        Add("ushort/max", ushort.MaxValue);
        Add("bool/true", true);
        Add("bool/false", false);
        Add("char/A", 'A');
        Add("char/euro", '€');
        Add("float/1.5", 1.5f);
        Add("float/nan", float.NaN);
        Add("float/-inf", float.NegativeInfinity);
        Add("double/pi", Math.PI);
        Add("double/-0", -0.0);
        Add("double/max", double.MaxValue);
        Add("decimal/1.5", 1.5m);
        Add("decimal/min", decimal.MinValue);
        Add("decimal/0.0001", 0.0001m);
        Add("bigint/2^100", BigInteger.Pow(2, 100));

        // Strings on fixstr / str8 / str16 boundaries, plus non-ASCII and null.
        Add("string/empty", string.Empty);
        Add("string/null", (string)null);
        Add("string/ascii", "hello world");
        Add("string/len31", new string('a', 31));
        Add("string/len32", new string('b', 32));
        Add("string/len256", new string('c', 256));
        Add("string/unicode", "Hyvää päivää å中😀");
        Add("string/oldspec-len40", new string('d', 40), OldSpec);

        Add("bytes/empty", Array.Empty<byte>());
        Add("bytes/small", new byte[] { 0, 1, 2, 254, 255 });
        Add("bytes/300", Enumerable.Range(0, 300).Select(i => (byte)i).ToArray());
        Add("bytes/oldspec", new byte[] { 9, 8, 7 }, OldSpec);

        Add("datetime/utc", new DateTime(2026, 11, 15, 10, 0, 0, 123, DateTimeKind.Utc));
        Add("datetime/unspecified", new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified));
        Add("datetime/min", DateTime.MinValue);
        Add("datetime/max-utc", DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));
        Add("datetime/pre-epoch-utc", new DateTime(1969, 7, 20, 20, 17, 40, DateTimeKind.Utc));
        Add("datetimeoffset/+02", new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.FromHours(2)));
        Add("timespan/90m", TimeSpan.FromMinutes(90));
        Add("guid/fixed", Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"));
        Add("nullable-int/null", (int?)null);
        Add("nullable-int/5", (int?)5);
        Add("nullable-guid/value", (Guid?)Guid.Parse("00000000-0000-0000-0000-000000000001"));
        Add("uri/https", new Uri("https://example.com/a?b=c"));
        Add("version/1.2.3.4", new Version(1, 2, 3, 4));

        Add("enum/color-blue", Color.Blue);
        Add("enum/small-b", Small.B);
        Add("enum/flags-long", Perm.Read | Perm.Huge);
        Add("enum/as-string-green", Color.Green, EnumAsString);

        Add("array/int-empty", Array.Empty<int>());
        Add("array/int", new[] { 1, -1, 300, int.MaxValue });
        Add("array/int-17", Enumerable.Range(0, 17).ToArray());
        Add("array/string", new[] { "a", null, "c" });
        Add("array/multidim", new int[,] { { 1, 2 }, { 3, 4 } });
        Add("list/double", new List<double> { 0.1, 2.5, -3 });
        Add("dict/string-int", new Dictionary<string, int> { ["one"] = 1, ["two"] = 2, ["three"] = 3 });
        Add("dict/int-string", new Dictionary<int, string> { [3] = "c", [1] = "a", [2] = null });
        Add("dict/16-entries", Enumerable.Range(0, 16).ToDictionary(i => i, i => i * i));
        Add("sorteddict/string-long", new SortedDictionary<string, long> { ["b"] = 2, ["a"] = 1 });
        Add("nested/list-dict-array", new List<Dictionary<string, int[]>>
        {
            new() { ["x"] = new[] { 1, 2 }, ["y"] = Array.Empty<int>() },
            new() { ["z"] = new[] { 3 } },
        });
        Add("hashset/int", new HashSet<int> { 3, 1, 2 });
        Add("tuple/int-string", Tuple.Create(1, "one"));
        Add("valuetuple/int-string-bool", (7, "seven", true));
        Add("kvp/string-int", new KeyValuePair<string, int>("k", 9));
        Add("object-array/mixed", new object[] { 1, "a", 2.5, true, null, new byte[] { 1 } });
        Add("dict/string-object", new Dictionary<string, object> { ["n"] = 1L, ["s"] = "x", ["l"] = new object[] { 1, 2 } });

        var intKeyed = new IntKeyed { Id = 42, Name = "answer", Score = 0.5, Active = true, Big = 1L << 40, Ignored = "never written" };
        Add("object/int-keyed", intKeyed);
        Add("object/int-keyed-null", (IntKeyed)null);
        Add("object/string-keyed", new StringKeyed { Id = 7, Name = "s", Tags = new List<string> { "t1", "t2" }, Ordering = 3 });
        Add("object/property-name-keyed", new PropertyNameKeyed { Zeta = 26, Alpha = "first", Middle = new[] { 13 } });
        Add("object/struct", new PointStruct { X = -1, Y = 1000, Z = 2.25f });
        Add("object/ctor", new Immutable(5, "five"));
        Add("object/nested", new Nested
        {
            Inner = intKeyed,
            MaybePoint = new PointStruct { X = 1, Y = 2, Z = 3 },
            Groups = new Dictionary<int, List<string>> { [1] = new() { "a" }, [2] = new() },
            Nullables = new int?[] { 1, null, 3 },
            Color = Color.Green,
            When = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        Add("union/circle", (IShape)new Circle { R = 1.25 });
        Add("union/rect", (IShape)new Rect { W = 3, H = 4 });
        Add("union/in-object", new Drawing
        {
            Shapes = new List<IShape> { new Circle { R = 2 }, new Rect { W = 1, H = 1 }, null },
            Named = new Dictionary<string, IShape> { ["c"] = new Circle { R = 9 } },
        });

        var poco = new Poco
        {
            Id = 1,
            Name = "poco",
            Values = new List<int> { 5, 6 },
            Map = new Dictionary<string, double> { ["pi"] = 3.14 },
            Color = Color.Red,
            When = new DateTime(2020, 2, 29, 12, 0, 0, DateTimeKind.Utc),
            Child = new PocoChild { Key = Guid.Parse("11111111-2222-3333-4444-555555555555"), Amount = 12.34m },
        };
        Add("contractless/poco", poco, Contractless);
        Add("contractless/dict-object", new Dictionary<string, object> { ["a"] = 1, ["b"] = "two" }, Contractless);

        Add("lz4/int-keyed", intKeyed, Lz4);
        Add("lz4/repetitive-string", new string('z', 2000), Lz4);
        Add("lz4/int-array", Enumerable.Range(0, 500).ToArray(), Lz4);

        return list;
    }
}

public static class Api
{
    const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static List<string> Listing()
    {
        var lines = new List<string>();
        foreach (var asm in new[] { typeof(MessagePackSerializer).Assembly, typeof(KeyAttribute).Assembly })
        {
            var asmName = asm.GetName().Name;
            foreach (var t in asm.GetExportedTypes())
            {
                var bases = new List<string>();
                if (t.BaseType != null && t.BaseType != typeof(object))
                {
                    bases.Add(t.BaseType.ToString());
                }

                bases.AddRange(t.GetInterfaces().Select(i => i.ToString()).OrderBy(s => s, StringComparer.Ordinal));
                lines.Add($"{asmName} type {Kind(t)} {t}{(bases.Count > 0 ? " : " + string.Join(", ", bases) : string.Empty)}");
                foreach (var m in t.GetMembers(Declared))
                {
                    if (!IsVisible(m) || (m is MethodBase mb && IsAccessor(mb)))
                    {
                        continue;
                    }

                    lines.Add($"{asmName} member {t}::{m.MemberType} {Describe(m)}");
                }
            }
        }

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    static string Kind(Type t) =>
        t.IsInterface ? "interface" : t.IsEnum ? "enum" : t.IsValueType ? "struct" : t.IsAbstract && t.IsSealed ? "static class" : t.IsAbstract ? "abstract class" : t.IsSealed ? "sealed class" : "class";

    // Property and event accessors are covered by their Property/Event line.
    static bool IsAccessor(MethodBase mb) =>
        mb.IsSpecialName && (mb.Name.StartsWith("get_") || mb.Name.StartsWith("set_") || mb.Name.StartsWith("add_") || mb.Name.StartsWith("remove_"));

    static bool IsVisible(MemberInfo m) => m switch
    {
        MethodBase mb => mb.IsPublic || mb.IsFamily || mb.IsFamilyOrAssembly,
        FieldInfo f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly,
        PropertyInfo p => (p.GetMethod != null && IsVisible(p.GetMethod)) || (p.SetMethod != null && IsVisible(p.SetMethod)),
        EventInfo e => e.AddMethod != null && IsVisible(e.AddMethod),
        Type nt => nt.IsNestedPublic || nt.IsNestedFamily || nt.IsNestedFamORAssem,
        _ => false,
    };

    static string Describe(MemberInfo m) => m switch
    {
        FieldInfo f when f.IsLiteral => $"{(f.IsStatic ? "static " : string.Empty)}{f} = {f.GetRawConstantValue()}",
        FieldInfo f => $"{(f.IsStatic ? "static " : string.Empty)}{(f.IsInitOnly ? "readonly " : string.Empty)}{f}",
        MethodBase mb => $"{(mb.IsStatic ? "static " : string.Empty)}{(mb.IsAbstract ? "abstract " : mb.IsVirtual && !mb.IsFinal ? "virtual " : string.Empty)}{(mb.IsFamily ? "protected " : string.Empty)}{mb}",
        PropertyInfo p => $"{p} {{{(p.GetMethod != null && IsVisible(p.GetMethod) ? " get;" : string.Empty)}{(p.SetMethod != null && IsVisible(p.SetMethod) ? " set;" : string.Empty)} }}",
        _ => m.ToString(),
    };
}
