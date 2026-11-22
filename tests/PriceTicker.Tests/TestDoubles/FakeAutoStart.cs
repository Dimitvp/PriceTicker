using PriceTicker.App.Platform;

namespace PriceTicker.Tests.TestDoubles;

internal sealed class FakeAutoStart(bool isEnabled = false, bool refuseChanges = false) : IAutoStart
{
    public bool IsSupported => true;

    public bool IsEnabled { get; private set; } = isEnabled;

    public int ChangeAttempts { get; private set; }

    public bool TrySetEnabled(bool enabled)
    {
        ChangeAttempts++;
        if (refuseChanges)
        {
            return false;
        }

        IsEnabled = enabled;
        return true;
    }
}

/// <summary>In-memory <c>Run</c> key; can be told to fail like a locked-down registry.</summary>
internal sealed class FakeRunKey : IRunKey
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Exception? FailWith { get; set; }

    public string? GetValue(string name)
    {
        ThrowIfFailing();
        return Values.GetValueOrDefault(name);
    }

    public void SetValue(string name, string value)
    {
        ThrowIfFailing();
        Values[name] = value;
    }

    public void DeleteValue(string name)
    {
        ThrowIfFailing();
        Values.Remove(name);
    }

    private void ThrowIfFailing()
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }
    }
}
