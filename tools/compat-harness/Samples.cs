using MessagePack;

namespace CompatHarness;

[MessagePackObject]
public class IntKeyed
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public string Name { get; set; }
    [Key(2)] public double Score { get; set; }
    [Key(3)] public bool Active { get; set; }
    // Key 4 is deliberately skipped: the formatter must write nil in the gap.
    [Key(5)] public long Big { get; set; }
    [IgnoreMember] public string Ignored { get; set; }
}

[MessagePackObject]
public class StringKeyed
{
    [Key("id")] public int Id { get; set; }
    [Key("name")] public string Name { get; set; }
    [Key("tags")] public List<string> Tags { get; set; }
    [Key("a_last_but_alphabetically_first")] public int Ordering { get; set; }
}

[MessagePackObject(keyAsPropertyName: true)]
public class PropertyNameKeyed
{
    public int Zeta { get; set; }
    public string Alpha { get; set; }
    public int[] Middle { get; set; }
}

[MessagePackObject]
public struct PointStruct
{
    [Key(0)] public int X { get; set; }
    [Key(1)] public int Y { get; set; }
    [Key(2)] public float Z { get; set; }
}

[MessagePackObject]
public class Immutable
{
    [SerializationConstructor]
    public Immutable(int a, string b)
    {
        A = a;
        B = b;
    }

    [Key(0)] public int A { get; }
    [Key(1)] public string B { get; }
}

public enum Color
{
    Red,
    Green = 5,
    Blue = 200,
}

public enum Small : byte
{
    A = 1,
    B = 255,
}

[Flags]
public enum Perm : long
{
    None = 0,
    Read = 1,
    Write = 2,
    Huge = 1L << 40,
}

[Union(0, typeof(Circle))]
[Union(1, typeof(Rect))]
public interface IShape
{
}

[MessagePackObject]
public class Circle : IShape
{
    [Key(0)] public double R { get; set; }
}

[MessagePackObject]
public class Rect : IShape
{
    [Key(0)] public int W { get; set; }
    [Key(1)] public int H { get; set; }
}

[MessagePackObject]
public class Drawing
{
    [Key(0)] public List<IShape> Shapes { get; set; }
    [Key(1)] public Dictionary<string, IShape> Named { get; set; }
}

[MessagePackObject]
public class Nested
{
    [Key(0)] public IntKeyed Inner { get; set; }
    [Key(1)] public PointStruct? MaybePoint { get; set; }
    [Key(2)] public Dictionary<int, List<string>> Groups { get; set; }
    [Key(3)] public int?[] Nullables { get; set; }
    [Key(4)] public Color Color { get; set; }
    [Key(5)] public DateTime When { get; set; }
}

public class Poco
{
    public int Id { get; set; }
    public string Name { get; set; }
    public List<int> Values { get; set; }
    public Dictionary<string, double> Map { get; set; }
    public Color Color { get; set; }
    public DateTime When { get; set; }
    public PocoChild Child { get; set; }
}

public class PocoChild
{
    public Guid Key { get; set; }
    public decimal Amount { get; set; }
}
