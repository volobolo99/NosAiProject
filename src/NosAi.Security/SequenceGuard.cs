namespace NosAi.Security;

/// <summary>
/// Replay protection over a 1024-bit sliding window
/// (docs/ROADMAP_ESECUTIVA.md S:2.3): <c>seq &gt; HighWaterMark</c> advances the
/// window and accepts; <c>seq &lt;= HighWaterMark - windowBits</c> is too old
/// and is rejected; a sequence whose bit in the window is already set is a
/// replay and is rejected. No allocation after construction, no lock: one
/// writer per session, matching the frame codec it protects.
/// </summary>
public sealed class SequenceGuard
{
    private readonly ulong[] _window;
    private readonly int _windowBits;
    private long _highWaterMark = -1;

    public SequenceGuard(int windowBits = 1024)
    {
        if (windowBits <= 0 || windowBits % 64 != 0)
            throw new ArgumentOutOfRangeException(nameof(windowBits), windowBits, "windowBits must be a positive multiple of 64.");

        _windowBits = windowBits;
        _window = new ulong[windowBits / 64];
    }

    /// <summary>The highest sequence accepted so far, or 0 before the first accepted sequence.</summary>
    public uint HighWaterMark => _highWaterMark < 0 ? 0u : (uint)_highWaterMark;

    /// <summary>Attempts to accept <paramref name="sequence"/>, applying the three rules above in order.</summary>
    public bool TryAccept(uint sequence)
    {
        long seq = sequence;

        if (_highWaterMark < 0)
        {
            _highWaterMark = seq;
            SetBit(0);
            return true;
        }

        if (seq > _highWaterMark)
        {
            long delta = seq - _highWaterMark;
            ShiftLeft(_window, delta >= _windowBits ? _windowBits : (int)delta);
            _highWaterMark = seq;
            SetBit(0);
            return true;
        }

        long offset = _highWaterMark - seq;
        if (offset >= _windowBits)
            return false; // Too old: older than the window can distinguish from a first-time arrival.

        int bitOffset = (int)offset;
        if (TestBit(bitOffset))
            return false; // Replay: this exact sequence was already accepted.

        SetBit(bitOffset);
        return true;
    }

    private void SetBit(int offset)
    {
        _window[offset / 64] |= 1UL << (offset % 64);
    }

    private bool TestBit(int offset)
    {
        return (_window[offset / 64] & (1UL << (offset % 64))) != 0;
    }

    /// <summary>
    /// Ages the window by <paramref name="delta"/> bit positions: bit <c>i</c>
    /// becomes bit <c>i + delta</c>, freeing bits <c>0..delta-1</c> for the new
    /// high-water mark, and dropping whatever shifts past the top of the
    /// window. Equivalent to a left-shift of the window treated as one
    /// multi-word unsigned integer with <c>window[0]</c> as the least
    /// significant word.
    /// </summary>
    private static void ShiftLeft(Span<ulong> window, int delta)
    {
        if (delta <= 0)
            return;

        int wordShift = delta / 64;
        int bitShift = delta % 64;

        if (wordShift >= window.Length)
        {
            window.Clear();
            return;
        }

        if (bitShift == 0)
        {
            for (int i = window.Length - 1; i >= wordShift; i--)
                window[i] = window[i - wordShift];
        }
        else
        {
            for (int i = window.Length - 1; i >= wordShift; i--)
            {
                ulong low = window[i - wordShift];
                ulong high = i - wordShift - 1 >= 0 ? window[i - wordShift - 1] : 0UL;
                window[i] = (low << bitShift) | (high >> (64 - bitShift));
            }
        }

        for (int i = 0; i < wordShift; i++)
            window[i] = 0UL;
    }
}
