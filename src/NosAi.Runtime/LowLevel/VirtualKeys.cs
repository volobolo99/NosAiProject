// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// LowLevel — Risoluzione dei nomi di tasto in virtual-key code Windows
// ============================================================================
//
// La mappatura precedente copriva solo A-Z, 0-9, SPACE, ENTER, ESC e TAB: le
// skill di NosTale stanno su F1-F12, quindi "F1" sollevava ArgumentException e
// l'intera barra delle abilità era irraggiungibile.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NosAi.Runtime.LowLevel;

/// <summary>A resolved key: the key itself plus any modifiers to hold around it.</summary>
public readonly record struct KeyChord(ushort VirtualKey, ImmutableArray<ushort> Modifiers)
{
    public static KeyChord Simple(ushort virtualKey) => new(virtualKey, ImmutableArray<ushort>.Empty);
}

/// <summary>
/// Maps human-written key names to Windows virtual-key codes.
/// </summary>
/// <remarks>
/// Accepts a single chord such as <c>F1</c>, <c>CTRL+S</c> or <c>SHIFT+F4</c>.
/// Resolution is strict: an unknown name is refused rather than silently mapped
/// to something plausible, because a wrong key press is a real action taken in
/// the game.
/// </remarks>
public static class VirtualKeys
{
    public const ushort Shift = 0x10;
    public const ushort Control = 0x11;
    public const ushort Alt = 0x12;

    private static readonly IReadOnlyDictionary<string, ushort> Named = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
    {
        ["SPACE"] = 0x20,
        ["ENTER"] = 0x0D, ["RETURN"] = 0x0D,
        ["ESC"] = 0x1B, ["ESCAPE"] = 0x1B,
        ["TAB"] = 0x09,
        ["BACKSPACE"] = 0x08, ["BACK"] = 0x08,
        ["DELETE"] = 0x2E, ["DEL"] = 0x2E,
        ["INSERT"] = 0x2D, ["INS"] = 0x2D,
        ["HOME"] = 0x24, ["END"] = 0x23,
        ["PAGEUP"] = 0x21, ["PGUP"] = 0x21,
        ["PAGEDOWN"] = 0x22, ["PGDN"] = 0x22,

        ["UP"] = 0x26, ["DOWN"] = 0x28, ["LEFT"] = 0x25, ["RIGHT"] = 0x27,

        ["SHIFT"] = Shift, ["CTRL"] = Control, ["CONTROL"] = Control, ["ALT"] = Alt,

        // NosTale drives its skill bar from the function keys; without these the
        // whole ability set was unreachable.
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
        ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
        ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,

        ["NUM0"] = 0x60, ["NUM1"] = 0x61, ["NUM2"] = 0x62, ["NUM3"] = 0x63,
        ["NUM4"] = 0x64, ["NUM5"] = 0x65, ["NUM6"] = 0x66, ["NUM7"] = 0x67,
        ["NUM8"] = 0x68, ["NUM9"] = 0x69,
        ["NUMMULTIPLY"] = 0x6A, ["NUMADD"] = 0x6B, ["NUMSUBTRACT"] = 0x6D,
        ["NUMDECIMAL"] = 0x6E, ["NUMDIVIDE"] = 0x6F,
    };

    /// <summary>Every key name this runtime can actuate.</summary>
    public static IReadOnlyCollection<string> SupportedNames => (IReadOnlyCollection<string>)Named.Keys;

    /// <summary>Resolves a chord such as <c>F1</c> or <c>CTRL+SHIFT+A</c>.</summary>
    public static bool TryResolve(string? name, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(name)) return false;

        string[] parts = name.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        var modifiers = ImmutableArray.CreateBuilder<ushort>(parts.Length - 1);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!TryResolveSingle(parts[i], out ushort modifier)) return false;
            if (modifier is not (Shift or Control or Alt)) return false;   // only real modifiers may lead
            modifiers.Add(modifier);
        }

        if (!TryResolveSingle(parts[^1], out ushort key)) return false;
        chord = new KeyChord(key, modifiers.ToImmutable());
        return true;
    }

    private static bool TryResolveSingle(string token, out ushort virtualKey)
    {
        virtualKey = 0;
        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            // Letters and digits share their ASCII value with their virtual-key code.
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
            {
                virtualKey = c;
                return true;
            }
        }
        return Named.TryGetValue(token, out virtualKey);
    }
}
