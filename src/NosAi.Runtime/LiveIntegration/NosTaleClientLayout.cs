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

    /// <summary>
    /// Player manager → the square the character is walking to.
    /// </summary>
    /// <remarks>
    /// Two <c>int16</c> at <c>+0x08</c> and <c>+0x0A</c>, beside the manager's own
    /// copy of the current position. It is the only place the character's
    /// <i>intent</i> is readable: the wire reports where things are, never where
    /// this character is heading, so a move that was accepted and a move that was
    /// ignored look identical without it.
    /// </remarks>
    public const int WalkTargetOffset = 0x08;

    /// <summary>Player manager → its own copy of the current position.</summary>
    /// <remarks>
    /// Two <c>int16</c> at <c>+0x04</c>. The map object at
    /// <see cref="PositionOffset"/> carries the same pair, and the two are read
    /// together precisely because they can be compared: two structures the client
    /// maintains separately agreeing is one more thing a wrong chain has to get
    /// right by luck.
    /// </remarks>
    public const int ManagerPositionOffset = 0x04;

    /// <summary>Map object → entity id. Common to players, monsters and NPCs.</summary>
    public const int EntityIdOffset = 0x08;

    /// <summary>
    /// The scene manager: every entity the client has on the current map.
    /// </summary>
    /// <remarks>
    /// The address is a direct pointer, so only one dereference follows the match
    /// (offsets <c>{1}</c> against the operand at <see cref="SceneOperandOffset"/>).
    /// </remarks>
    public const string SceneManagerSignature =
        "FF ?? ?? ?? ?? ?? FF FF FF 00 00 00 00 00 00 00 00 00 00 00 00 FF FF FF FF";

    /// <summary>Where the scene manager's address sits inside its match.</summary>
    public const int SceneOperandOffset = 1;

    /// <summary>Scene manager → the four entity lists.</summary>
    public const int PlayerListOffset = 0x0C;
    public const int MonsterListOffset = 0x10;
    public const int NpcListOffset = 0x14;
    public const int GroundItemListOffset = 0x18;

    /// <summary>List → the array of object pointers.</summary>
    public const int ListArrayOffset = 0x04;

    /// <summary>List → how many entries the array holds.</summary>
    public const int ListLengthOffset = 0x08;

    /// <summary>
    /// The most entities one list is believed to hold.
    /// </summary>
    /// <remarks>
    /// A length is four bytes of whatever the pointer happened to land on when
    /// the chain is wrong, so it is bounded before it is used to size a loop. The
    /// real captures show 158 distinct entities across a whole session; anything
    /// past this is not a crowded map, it is a bad read.
    /// </remarks>
    public const int MaxEntitiesPerList = 4096;

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

        // The manager keeps its own copy of the position and, beside it, the
        // square the character is walking to. Both are read here so the two
        // copies can be compared and the intent is available at all.
        MemoryReadResult managerPosition = reader.Read(manager + ManagerPositionOffset, sizeof(int));
        MemoryReadResult walkTarget = reader.Read(manager + WalkTargetOffset, sizeof(int));

        uint packed = BitConverter.ToUInt32(position.Bytes);
        reading = new PlayerObjectReading(
            CharacterId: BitConverter.ToInt32(idBytes.Bytes),
            EntityId: BitConverter.ToInt32(entityId.Bytes),
            X: (ushort)(packed & 0xFFFF),
            Y: (ushort)(packed >> 16),
            ManagerX: managerPosition.Ok ? BitConverter.ToInt16(managerPosition.Bytes, 0) : null,
            ManagerY: managerPosition.Ok ? BitConverter.ToInt16(managerPosition.Bytes, 2) : null,
            WalkTargetX: walkTarget.Ok ? BitConverter.ToInt16(walkTarget.Bytes, 0) : null,
            WalkTargetY: walkTarget.Ok ? BitConverter.ToInt16(walkTarget.Bytes, 2) : null);
        failureReason = null;
        return true;
    }

    /// <summary>
    /// Reads one of the scene manager's entity lists: every monster, NPC, player
    /// or ground item the client currently has on the map.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What F2-2 needs. The wire gives the same entities, but only as they are
    /// mentioned: a capture that starts mid-session has 25 <c>in</c> against 7685
    /// <c>mv</c>, so everything already on screen stays unreported until it moves.
    /// The client's own list has all of them at once, and it has them the moment
    /// it is asked rather than when a packet happens to arrive.
    /// </para>
    /// <para>
    /// Resolved separately from the player manager and not cached alongside it:
    /// the two are different signatures with different failure modes, and a
    /// runtime that can read the character but not the map should say exactly
    /// that.
    /// </para>
    /// </remarks>
    public static bool TryReadEntities(
        ProcessMemoryReader reader,
        IntPtr moduleBase,
        long moduleSize,
        MapEntityKind kind,
        out IReadOnlyList<MapEntityReading> entities,
        out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(reader);
        entities = Array.Empty<MapEntityReading>();

        if (!TryResolveScene(reader, moduleBase, moduleSize, out IntPtr scene, out failureReason))
            return false;

        int listOffset = kind switch
        {
            MapEntityKind.Player => PlayerListOffset,
            MapEntityKind.Monster => MonsterListOffset,
            MapEntityKind.Npc => NpcListOffset,
            MapEntityKind.GroundItem => GroundItemListOffset,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        if (!TryFollow(reader, scene + listOffset, $"{kind}_list", out IntPtr list, out failureReason))
            return false;

        MemoryReadResult lengthBytes = reader.Read(list + ListLengthOffset, sizeof(int));
        if (!lengthBytes.Ok)
        {
            failureReason = lengthBytes.FailureReason ?? "entity_list_length_unreadable";
            return false;
        }

        int length = BitConverter.ToInt32(lengthBytes.Bytes);
        if (length < 0 || length > MaxEntitiesPerList)
        {
            // A length is four bytes of whatever the chain landed on when it is
            // wrong. Refusing beats allocating for it.
            failureReason = $"entity_list_length_implausible:{length}";
            return false;
        }

        if (length == 0)
        {
            entities = Array.Empty<MapEntityReading>();
            failureReason = null;
            return true;
        }

        if (!TryFollow(reader, list + ListArrayOffset, $"{kind}_array", out IntPtr array, out failureReason))
            return false;

        // The pointer array in one read: length reads would let the list change
        // underneath and mix entries from two different moments.
        MemoryReadResult pointers = reader.Read(array, length * sizeof(int));
        if (!pointers.Ok)
        {
            failureReason = pointers.FailureReason ?? "entity_array_unreadable";
            return false;
        }

        var found = new List<MapEntityReading>(length);
        for (var i = 0; i < length; i++)
        {
            var entity = (IntPtr)BitConverter.ToUInt32(pointers.Bytes, i * sizeof(int));
            if (entity == IntPtr.Zero)
                continue;

            MemoryReadResult idBytes = reader.Read(entity + EntityIdOffset, sizeof(int));
            MemoryReadResult positionBytes = reader.Read(entity + PositionOffset, sizeof(int));
            // One unreadable entry does not discard the rest: a list read while
            // something despawns is ordinary, and the entities that did read are
            // still entities that were there.
            if (!idBytes.Ok || !positionBytes.Ok)
                continue;

            uint packed = BitConverter.ToUInt32(positionBytes.Bytes);
            found.Add(new MapEntityReading(
                BitConverter.ToInt32(idBytes.Bytes),
                (ushort)(packed & 0xFFFF),
                (ushort)(packed >> 16)));
        }

        entities = found;
        failureReason = null;
        return true;
    }

    /// <summary>
    /// Finds the scene manager, whose match holds a direct pointer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike the player manager's, this signature is data and it is loose: one
    /// <c>FF</c>, five wildcards, then a run of zeros. A 5.7 MB image contains
    /// several byte runs shaped like that, so the <i>first</i> match is not
    /// evidence of anything — taking it produced a pointer whose lists read back
    /// ERROR_PARTIAL_COPY.
    /// </para>
    /// <para>
    /// So every match is tried and each candidate has to prove itself: its four
    /// list pointers must be readable and their lengths plausible. This is the
    /// same rule the character id enforces one level up — a signature is a
    /// hypothesis, and the thing that turns it into a reading is a check it could
    /// not have passed by accident.
    /// </para>
    /// </remarks>
    private static bool TryResolveScene(
        ProcessMemoryReader reader, IntPtr moduleBase, long moduleSize,
        out IntPtr scene, out string? failureReason)
    {
        scene = IntPtr.Zero;
        failureReason = null;

        if (moduleBase == IntPtr.Zero || moduleSize <= 0)
        {
            failureReason = "client_module_not_located";
            return false;
        }

        SignatureByte[] signature = ParseSignature(SceneManagerSignature);
        const int sliceLength = 1 << 20;
        int overlap = signature.Length - 1;
        var candidates = 0;

        for (long offset = 0; offset < moduleSize; offset += sliceLength - overlap)
        {
            int length = (int)Math.Min(sliceLength, moduleSize - offset);
            if (length < signature.Length)
                break;

            MemoryReadResult slice = reader.Read(moduleBase + (int)offset, length);
            if (!slice.Ok)
                continue;

            var searchFrom = 0;
            while (true)
            {
                int index = IndexOfSignature(slice.Bytes, signature, searchFrom);
                if (index < 0)
                    break;

                searchFrom = index + 1;
                IntPtr match = moduleBase + (int)offset + index;

                MemoryReadResult operand = reader.Read(match + SceneOperandOffset, sizeof(int));
                if (!operand.Ok)
                    continue;

                var pointer = (IntPtr)BitConverter.ToUInt32(operand.Bytes);
                if (pointer == IntPtr.Zero)
                    continue;

                candidates++;
                if (!LooksLikeSceneManager(reader, pointer))
                    continue;

                scene = pointer;
                return true;
            }
        }

        failureReason = candidates == 0
            ? "scene_manager_signature_not_found"
            : $"scene_manager_not_confirmed:{candidates}_candidates_rejected";
        return false;
    }

    /// <summary>
    /// Whether a candidate address behaves like the scene manager.
    /// </summary>
    /// <remarks>
    /// All four lists must be readable and sized plausibly. A stray pointer clears
    /// one of those by luck now and then; clearing all four is what makes this a
    /// check rather than a guess.
    /// </remarks>
    private static bool LooksLikeSceneManager(ProcessMemoryReader reader, IntPtr candidate)
    {
        foreach (int listOffset in new[]
                 { PlayerListOffset, MonsterListOffset, NpcListOffset, GroundItemListOffset })
        {
            MemoryReadResult listPointer = reader.Read(candidate + listOffset, sizeof(int));
            if (!listPointer.Ok)
                return false;

            var list = (IntPtr)BitConverter.ToUInt32(listPointer.Bytes);
            if (list == IntPtr.Zero)
                return false;

            MemoryReadResult lengthBytes = reader.Read(list + ListLengthOffset, sizeof(int));
            if (!lengthBytes.Ok)
                return false;

            int length = BitConverter.ToInt32(lengthBytes.Bytes);
            if (length < 0 || length > MaxEntitiesPerList)
                return false;

            // A non-empty list must have an array behind it. An empty one need
            // not, and an empty monster list is an ordinary quiet map.
            if (length == 0)
                continue;

            MemoryReadResult arrayPointer = reader.Read(list + ListArrayOffset, sizeof(int));
            if (!arrayPointer.Ok)
                return false;

            var array = (IntPtr)BitConverter.ToUInt32(arrayPointer.Bytes);
            if (array == IntPtr.Zero || !reader.Read(array, sizeof(int)).Ok)
                return false;
        }

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

    internal static int IndexOfSignature(
        ReadOnlySpan<byte> haystack, ReadOnlySpan<SignatureByte> signature, int from = 0)
    {
        if (signature.Length == 0 || haystack.Length < signature.Length || from < 0)
            return -1;

        int last = haystack.Length - signature.Length;
        for (int start = from; start <= last; start++)
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
/// <param name="ManagerX">
/// The manager's own copy of the position, or null when it could not be read. It
/// should equal <paramref name="X"/>; when it does not, one of the two structures
/// is not the character's.
/// </param>
/// <param name="WalkTargetX">
/// The square the character is walking to, or null. The one readable statement of
/// intent: the wire never says where this character is heading.
/// </param>
public readonly record struct PlayerObjectReading(
    int CharacterId,
    int EntityId,
    ushort X,
    ushort Y,
    short? ManagerX = null,
    short? ManagerY = null,
    short? WalkTargetX = null,
    short? WalkTargetY = null)
{
    /// <summary>Whether the two copies of the position agree.</summary>
    /// <remarks>
    /// Null when the manager's copy could not be read, which is not a
    /// disagreement. A mismatch is: two structures the client maintains itself
    /// should not differ, and a chain that reads one of them wrongly is caught
    /// here without needing anything outside the process.
    /// </remarks>
    public bool? PositionCopiesAgree => ManagerX is { } mx && ManagerY is { } my
        ? mx == X && my == Y
        : null;
}

/// <summary>One entity the client has on the current map.</summary>
public readonly record struct MapEntityReading(long EntityId, ushort X, ushort Y);

/// <summary>Which of the scene manager's four lists to read.</summary>
public enum MapEntityKind
{
    Player,
    Monster,
    Npc,
    GroundItem,
}
