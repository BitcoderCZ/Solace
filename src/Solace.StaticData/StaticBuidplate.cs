namespace Solace.StaticData;

public readonly struct StaticBuidplate : IEquatable<StaticBuidplate>
{
    private readonly string _path;

    internal StaticBuidplate(string path)
    {
        _path = path;
    }

    public Guid Id => Guid.Parse(Path.GetFileNameWithoutExtension(_path));

    public Stream OpenRead()
        => File.OpenRead(_path);

    public bool Equals(StaticBuidplate other)
        => _path == other._path;

    public override bool Equals(object? obj)
        => obj is StaticBuidplate other && Equals(other);

    public override int GetHashCode()
        => _path.GetHashCode(StringComparison.Ordinal);

    public static bool operator ==(StaticBuidplate left, StaticBuidplate right)
        => left.Equals(right);

    public static bool operator !=(StaticBuidplate left, StaticBuidplate right)
        => !(left == right);
}
