using System.Globalization;

namespace NosAi.LiveIntegration;

/// <summary>
/// One byte of a signature: a value to match, or a wildcard.
/// </summary>
public readonly record struct SignatureByte(byte Value, bool IsWildcard)
{
    public bool Matches(byte candidate) => IsWildcard || candidate == Value;
}

/// <summary>
/// Where the controlled character's object lives in the client's memory, found by
/// the shape of the code that reaches it rather than by a remembered address.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a pattern and not an offset.</b> F1-9 describes finding an address by
/// scanning for a value and narrowing across changes the operator causes, then
/// re-verifying it after a restart — and records, correctly, that an offset never
/// re-verified is "an address that worked once". A code signature removes that
/// problem instead of managing it: nothing is remembered between runs, so ASLR
/// and restarts stop being a source of error, and the search is repeated from
/// scratch every time the runtime attaches.
/// </para>
/// <para>
/// <b>Where the numbers come from.</b> The signature and the offsets are those
/// published by <c>NosSmooth.Local</c> (Rutherther, MIT), which binds to the same
/// client. They are used here as a starting hypothesis and are not trusted on
/// authority: <see cref="PlayerObjectReading"/> carries the character id the
/// client holds, and the runtime checks it against the id the <i>server</i> sent
/// on the wire. Two independent sources agreeing on one number is the validity
/// check ADR-0014 demands; a pointer chain that produced a plausible coordinate
/// but the wrong id is rejected.
/// </para>
/// </remarks>
public sealed class NosTaleClientLayout
{
    /// <summary>
    /// The instruction sequence that loads the player manager pointer.
    /// </summary>
    /// <remarks>
    /// <c>xor ecx, ecx; mov edx, [ebp-4]; mov eax, [addr]; call …</c>. The four
    /// bytes at <see cref="PointerOperandOffset"/> are the operand of the
    /// <c>mov eax, moffs32</c>: the absolute address of the pointer, baked into
    /// the code, which is why finding the code finds the data.
    /// </remarks>
    public const string PlayerManagerSignature = "33 C9 8B 55 FC A1 ?? ?? ?? ?? E8 ?? ?? ?? ??";

    /// <summary>Where the absolute address sits inside the matched instructions.</summary>
    public const int PointerOperandOffset = 6;

    /// <summary>Player manager → the character's own map object.</summary>
    public const int PlayerObjectOffset = 0x20;

    /// <summary>Player manager → the character id the client is holding.</summary>
    public const int PlayerIdOffset = 0x24;

    /// <summary>Map object → entity id. Common to players, monsters and NPCs.</summary>
    public const int EntityIdOffset = 0x08;

    /// <summary>
    /// Map object → x, as a 16-bit value with y in the two bytes after it.
    /// </summary>
    /// <remarks>
    /// Both coordinates are <c>uint16</c> and adjacent, so one 32-bit read takes
    /// them together: x in the low half, y in the high half. Reading them as two
    /// separate words would let the character move between the two reads and
    /// produce a position it was never at — a pair that passes every range check
    /// and describes nowhere.
    /// </remarks>
    public const int PositionOffset = 0x0C;

    private readonly IntPtr _pointerHolder;

    private NosTaleClientLayout(IntPtr pointerHolder) => _pointerHolder = pointerHolder;

    /// <summary>The absolute address the client's code loads the manager from.</summary>
    public IntPtr PlayerManagerPointerAddress => _pointerHolder;

    /// <summary>
    /// Finds the layout in the client's own image, or says why it could not.
    /// </summary>
    /// <param name="moduleBase">Base of the module to search; the client executable.</param>
    /// <param name="moduleSize">Its size in bytes.</param>
    /// <remarks>
    /// Only the image is searched. The signature is code, and the same bytes
    /// occurring in private data would be a coincidence pointing at nothing.
    /// </remarks>
    public static bool TryResolve(
        ProcessMemoryReader reader,
        IntPtr moduleBase,
        long moduleSize,
        out NosTaleClientLayout? layout,
        out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(reader);
        layout = null;
        failureReason = null;

        if (moduleBase == IntPtr.Zero || moduleSize <= 0)
        {
            failureReason = "client_module_not_located";
            return false;
        }

        SignatureByte[] signature = ParseSignature(PlayerManagerSignature);

        // Read the image in slices rather than whole: a client image is tens of
        // megabytes and the caller is a runtime, not a one-off tool.
        const int sliceLength = 1 << 20;
        int overlap = signature.Length - 1;

        for (long offset = 0; offset < moduleSize; offset += sliceLength - overlap)
        {
            int length = (int)Math.Min(sliceLength, moduleSize - offset);
            if (length < signature.Length)
                break;

            MemoryReadResult slice = reader.Read(moduleBase + (int)offset, length);
            if (!slice.Ok)
                continue;

            int index = IndexOfSignature(slice.Bytes, signature);
            if (index < 0)
                continue;

            IntPtr match = moduleBase + (int)offset + index;
            MemoryReadResult operand = reader.Read(match + PointerOperandOffset, sizeof(int));
            if (!operand.Ok)
            {
                failureReason = operand.FailureReason ?? "signature_operand_unreadable";
                return false;
            }

            var holder = (IntPtr)BitConverter.ToUInt32(operand.Bytes);
            if (holder == IntPtr.Zero)
            {
                failureReason = "signature_operand_is_null";
                return false;
            }

            layout = new NosTaleClientLayout(holder);
            return true;
        }

        failureReason = "player_manager_signature_not_found";
        return false;
    }

    /// <summary>
    /// Follows the chain and reads the character's id and position, or says where
    /// it broke.
    /// </summary>
    /// <remarks>
    /// The chain is followed on every call, never cached. The manager holds a
    /// pointer to the map object and the client replaces that object — on a map
    /// change, for one — so a remembered object address is a reading of whatever
    /// occupies that memory afterwards.
    /// </remarks>
    public bool TryReadPlayer(
        ProcessMemoryReader reader,
        out PlayerObjectReading reading,
        out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(reader);
        reading = default;

        if (!TryFollow(reader, _pointerHolder, "player_manager", out IntPtr manager, out failureReason))
            return false;

        MemoryReadResult idBytes = reader.Read(manager + PlayerIdOffset, sizeof(int));
        if (!idBytes.Ok)
        {
            failureReason = idBytes.FailureReason ?? "player_id_unreadable";
            return false;
        }

        if (!TryFollow(reader, manager + PlayerObjectOffset, "player_object", out IntPtr playerObject, out failureReason))
            return false;

        MemoryReadResult entityId = reader.Read(playerObject + EntityIdOffset, sizeof(int));
        if (!entityId.Ok)
        {
            failureReason = entityId.FailureReason ?? "entity_id_unreadable";
            return false;
        }

        // One read for both coordinates: see PositionOffset.
        MemoryReadResult position = reader.Read(playerObject + PositionOffset, sizeof(int));
        if (!position.Ok)
        {
            failureReason = position.FailureReason ?? "position_unreadable";
            return false;
        }

        uint packed = BitConverter.ToUInt32(position.Bytes);
        reading = new PlayerObjectReading(
            CharacterId: BitConverter.ToInt32(idBytes.Bytes),
            EntityId: BitConverter.ToInt32(entityId.Bytes),
            X: (ushort)(packed & 0xFFFF),
            Y: (ushort)(packed >> 16));
        failureReason = null;
        return true;
    }

    /// <summary>Reads a pointer and refuses a null one by name.</summary>
    private static bool TryFollow(
        ProcessMemoryReader reader, IntPtr at, string what, out IntPtr value, out string? failureReason)
    {
        value = IntPtr.Zero;
        MemoryReadResult result = reader.Read(at, sizeof(int));
        if (!result.Ok)
        {
            failureReason = result.FailureReason ?? $"{what}_unreadable";
            return false;
        }

        uint pointer = BitConverter.ToUInt32(result.Bytes);
        if (pointer == 0)
        {
            // Null is the ordinary state before the character is in the world, so
            // it is a named refusal rather than an error: the runtime attached
            // while the client sat at the login screen.
            failureReason = $"{what}_null";
            return false;
        }

        value = (IntPtr)pointer;
        failureReason = null;
        return true;
    }

    internal static SignatureByte[] ParseSignature(string signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);

        string[] tokens = signature.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var parsed = new SignatureByte[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i] == "??")
            {
                parsed[i] = new SignatureByte(0, IsWildcard: true);
                continue;
            }

            if (!byte.TryParse(tokens[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
                throw new ArgumentException($"Not a signature byte: '{tokens[i]}'.", nameof(signature));

            parsed[i] = new SignatureByte(value, IsWildcard: false);
        }

        return parsed;
    }

    internal static int IndexOfSignature(ReadOnlySpan<byte> haystack, ReadOnlySpan<SignatureByte> signature)
    {
        if (signature.Length == 0 || haystack.Length < signature.Length)
            return -1;

        int last = haystack.Length - signature.Length;
        for (var start = 0; start <= last; start++)
        {
            var matched = true;
            for (var i = 0; i < signature.Length; i++)
            {
                if (!signature[i].Matches(haystack[start + i]))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
                return start;
        }

        return -1;
    }
}

/// <summary>What one pass over the character's object read.</summary>
/// <param name="CharacterId">
/// The id the player manager holds. Checked against the id the server sent, which
/// is what turns a plausible pointer chain into a confirmed one.
/// </param>
/// <param name="EntityId">The id on the map object itself.</param>
public readonly record struct PlayerObjectReading(int CharacterId, int EntityId, ushort X, ushort Y);
