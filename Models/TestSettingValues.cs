namespace DBConnectionTester.Models;

public sealed record NetworkPort
{
    public const int Minimum = 1;
    public const int Maximum = 65_535;

    private NetworkPort(int value) => Value = value;

    public int Value { get; }

    public static bool TryCreate(int value, out NetworkPort? port)
    {
        if (value is < Minimum or > Maximum)
        {
            port = null;
            return false;
        }

        port = new NetworkPort(value);
        return true;
    }

    public static NetworkPort Create(int value) => TryCreate(value, out var port)
        ? port!
        : throw new ArgumentOutOfRangeException(nameof(value), value,
            $"A porta deve estar entre {Minimum} e {Maximum}.");

    public override string ToString() => Value.ToString();
}

public readonly record struct RunCount
{
    public const long Minimum = 1;
    public const long Maximum = 10_000_000;

    private RunCount(long value) => Value = value;

    public long Value { get; }

    public static bool TryCreate(long value, out RunCount count)
    {
        if (value is < Minimum or > Maximum)
        {
            count = default;
            return false;
        }

        count = new RunCount(value);
        return true;
    }

    public static RunCount Create(long value) => TryCreate(value, out var count)
        ? count
        : throw new ArgumentOutOfRangeException(nameof(value), value,
            $"A quantidade deve estar entre {Minimum:N0} e {Maximum:N0}.");

    public override string ToString() => Value.ToString();
}

public readonly record struct TestInterval
{
    public static readonly TimeSpan Minimum = TimeSpan.Zero;
    public static readonly TimeSpan Maximum = TimeSpan.FromHours(1);

    private TestInterval(TimeSpan value) => Value = value;

    public TimeSpan Value { get; }

    public static bool TryCreate(TimeSpan value, out TestInterval interval)
    {
        if (value < Minimum || value > Maximum)
        {
            interval = default;
            return false;
        }

        interval = new TestInterval(value);
        return true;
    }

    public static TestInterval Create(TimeSpan value) => TryCreate(value, out var interval)
        ? interval
        : throw new ArgumentOutOfRangeException(nameof(value), value,
            $"O intervalo deve estar entre {Minimum} e {Maximum}.");
}

public readonly record struct StageTimeout
{
    public static readonly TimeSpan Minimum = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan Maximum = TimeSpan.FromMinutes(2);

    private StageTimeout(TimeSpan value) => Value = value;

    public TimeSpan Value { get; }

    public static bool TryCreate(TimeSpan value, out StageTimeout timeout)
    {
        if (value < Minimum || value > Maximum)
        {
            timeout = default;
            return false;
        }

        timeout = new StageTimeout(value);
        return true;
    }

    public static StageTimeout Create(TimeSpan value) => TryCreate(value, out var timeout)
        ? timeout
        : throw new ArgumentOutOfRangeException(nameof(value), value,
            $"O timeout deve estar entre {Minimum} e {Maximum}.");
}
