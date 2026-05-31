using System.Windows.Input;

namespace MysteriousCharacters.App.Models;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8
}

public sealed class HotkeyGesture : IEquatable<HotkeyGesture>
{
    public HotkeyModifiers Modifiers { get; set; } = HotkeyModifiers.Ctrl | HotkeyModifiers.Alt;

    public Key Key { get; set; } = Key.E;

    public bool Equals(HotkeyGesture? other)
    {
        return other is not null && Modifiers == other.Modifiers && Key == other.Key;
    }

    public override bool Equals(object? obj)
    {
        return obj is HotkeyGesture other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Modifiers, Key);
    }

    public override string ToString()
    {
        var parts = new List<string>();

        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(Key.ToString());
        return string.Join(" + ", parts);
    }
}
