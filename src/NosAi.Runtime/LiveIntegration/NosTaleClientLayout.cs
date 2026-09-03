using System.Globalization;
using NosAi.Runtime.Navigation;

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

    /// <summary>Client image base to the id of the map the character is on.</summary>
    /// <remarks>
    /// <para>
    /// <b>Measured, not guessed, and measured against two different questions.</b>
    /// <c>+0x30</c> on the player manager, which the community documentation gave,
    /// held a heap pointer: <c>--grid-check</c> read <c>506534864</c> where the
    /// extracted map ids are all under a thousand. What replaced the guess is the
    /// oracle in <see cref="NosAi.Runtime.Navigation.MapIdFinder"/>: a word is a
    /// candidate only while it names a <c>.grid</c> whose rectangle contains the
    /// character.
    /// </para>
    /// <para>
    /// On 2 September 2026 that filter survived four maps and one client restart
    /// and left exactly one candidate. The two proofs answer two different
    /// questions and neither substitutes for the other: crossing a portal shows
    /// the field <i>is</i> the map id, and restarting the client shows this number
    /// is an <i>offset</i> rather than an address that happened to work once.
    /// </para>
    /// <para>
    /// It is measured from the <b>image</b>, not from the manager, so it is a
    /// global the client keeps rather than a field of the character's object. That
    /// is why it is resolved against the module base this layout was found in, and
    /// why it survives relocation: the base is read again on every attach.
    /// </para>
    /// <para>
    /// It is still not a licence to trust the number. The id is only
    /// <c>Candidate</c> until it resolves to a grid that contains the character,
    /// repeatedly - the validity predicate ADR-0014 asks for - and
    /// <c>--grid-check</c> is where that is checked.
    /// </para>
    /// </remarks>
    public const int MapIdModuleOffset = 0x38D1BC;

    /// <summary>Reported when the word at that offset cannot be a map id.</summary>
    public const string MapIdImplausiblePrefix = "map_id_implausible";

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

    /// <summary>Player manager → the object of the entity the character has selected.</summary>
    /// <remarks>
    /// <para>
    /// <b>Found by the behavioural oracle, not by a signature and not by analogy.</b>
    /// <see cref="NosAi.Runtime.Navigation.TargetIdFinder"/> keeps a word only while it
    /// changes exactly when the selection changes <i>and</i> returns to the same value
    /// every time the target is cleared. On 2 September 2026 that survived six
    /// selections, two clearings and one client restart, and left exactly one candidate:
    /// this offset, with zero as its "nobody" value.
    /// </para>
    /// <para>
    /// <b>It holds a pointer, not an id, and the numbers say so.</b> The hunt was looking
    /// for an entity id and found something better behaved: two runs read
    /// <c>0x22C8A4F0</c> and <c>0x1F5BA4F0</c>, while real entity ids on this build are
    /// three orders of magnitude smaller (the character's own is <c>0x00348A11</c>, and
    /// one taken off the wire is <c>0x0004CA32</c>). Both values sit in the same heap
    /// region as the manager, and their low sixteen bits are <i>identical across two
    /// different processes</i> — that is the signature of an allocation, not of a number.
    /// </para>
    /// <para>
    /// <b>Why the oracle found it anyway, and why that is the right outcome.</b> A
    /// pointer to the target behaves exactly like an id of the target: it changes with
    /// the selection and goes to zero when there is none. The behavioural constraint
    /// found the correct field precisely <i>because</i> it never looked at the contents.
    /// A content filter would have discarded it and the hunt would have ended empty,
    /// with the oracle taking the blame.
    /// </para>
    /// <para>
    /// For <c>HasTarget</c> this is more direct than an id would have been: non-zero is a
    /// target, zero is none. Which entity it is remains a separate, unproven question —
    /// see <see cref="TargetEntityIdIsAHypothesisReason"/>.
    /// </para>
    /// </remarks>
    public const int TargetPointerOffset = 0x44;

    /// <summary>
    /// Why the id behind the target pointer is reported as a candidate and never as a reading.
    /// </summary>
    /// <remarks>
    /// By analogy with the character — the player object hangs at
    /// <see cref="PlayerObjectOffset"/> and its id at <see cref="EntityIdOffset"/> — the
    /// id of the selected entity should be at <c>[manager + TargetPointerOffset] +
    /// EntityIdOffset</c>. <b>An analogy is not a measurement.</b> The project asks two
    /// independent sources to agree before a number is established, exactly as the map id
    /// required both a portal crossing and a restart; here the second source is
    /// <c>ct</c> on the wire, which names the selected entity. Until a run shows the two
    /// agreeing, the identity stays UNKNOWN with this reason — and <c>HasTarget</c> works
    /// regardless, because knowing <i>that</i> there is a target and knowing <i>which</i>
    /// are two facts and the first does not wait for the second.
    /// </remarks>
    public const string TargetEntityIdIsAHypothesisReason = "target_entity_id_not_established";

    /// <summary>
    /// The scene manager: every entity the client has on the current map.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The address is a direct pointer, so only one dereference follows the match
    /// (offsets <c>{1}</c> against the operand at <see cref="SceneOperandOffset"/>).
    /// </para>
    /// <para>
    /// <b>This is data, not code, and it is weak.</b> Twenty-five bytes of which
    /// eleven are <c>00</c>, seven are <c>FF</c> and five are wildcards — and the
    /// four bytes taken as the pointer are wildcards of its own pattern, so the
    /// signature places no constraint whatever on the value it hands back. A run
    /// of <c>FF</c> bytes in the image satisfies it and yields <c>0xFFFFFFFF</c> as
    /// an address; that is exactly what happened on the live client on
    /// 3 September 2026 (see <see cref="IsPlausibleSceneOperand"/>).
    /// </para>
    /// <para>
    /// It is left as it was measured all the same. A replacement pattern cannot be
    /// tested without the client in front of it, and a guessed signature is the
    /// same unverifiable number this file refuses everywhere else. What can be
    /// fixed without the client is what is done with a match: an operand that
    /// cannot be a pointer is rejected by name before anything follows it.
    /// </para>
    /// </remarks>
    public const string SceneManagerSignature =
        "FF ?? ?? ?? ?? ?? FF FF FF 00 00 00 00 00 00 00 00 00 00 00 00 FF FF FF FF";

    /// <summary>Where the scene manager's address sits inside its match.</summary>
    public const int SceneOperandOffset = 1;

    /// <summary>Reported when a matched operand is zero, so there is nothing to follow.</summary>
    public const string SceneOperandNullReason = "scene_operand_null";

    /// <summary>Reported when a matched operand has every one of its 32 bits set.</summary>
    public const string SceneOperandAllBitsSetReason = "scene_operand_all_bits_set";

    /// <summary>Reported when a matched operand is not four-byte aligned.</summary>
    public const string SceneOperandMisalignedReason = "scene_operand_misaligned";

    /// <summary>Reported when a matched operand sits below the client's own image.</summary>
    /// <remarks>The base it was compared against is appended, since nothing else names it.</remarks>
    public const string SceneOperandBelowModuleBasePrefix = "scene_operand_below_module_base";

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

    /// <summary>
    /// Monster object → the object that holds the display name.
    /// </summary>
    /// <remarks>
    /// Candidate chain from <c>docs/MAPPA_MEMORIA_CLIENT_CANDIDATI.md</c> § 4.1,
    /// not an established offset. The string predicate refuses a wrong landing;
    /// concordance with <c>in</c> is what would establish it. Not used for
    /// players or NPCs: each kind stays its own case until demonstrated.
    /// </remarks>
    public const int MonsterNameObjectOffset = 0x1BC;

    /// <summary>Name object → the ANSI <c>char*</c> for a monster.</summary>
    public const int MonsterNamePointerOffset = 0x04;

    /// <summary>Ground item object → the object that holds the display name.</summary>
    public const int GroundItemNameObjectOffset = 0xC4;

    /// <summary>Name object → the ANSI <c>char*</c> for a ground item.</summary>
    public const int GroundItemNamePointerOffset = 0x38;

    private readonly IntPtr _pointerHolder;
    private readonly IntPtr _moduleBase;

    private NosTaleClientLayout(IntPtr pointerHolder, IntPtr moduleBase)
    {
        _pointerHolder = pointerHolder;
        _moduleBase = moduleBase;
    }

    /// <summary>The absolute address the client's code loads the manager from.</summary>
    public IntPtr PlayerManagerPointerAddress => _pointerHolder;

    /// <summary>
    /// The image base this layout was resolved in.
    /// </summary>
    /// <remarks>
    /// Held rather than passed in per call, so that a module-relative offset can
    /// never be resolved against a base from a different attach.
    /// </remarks>
    public IntPtr ModuleBase => _moduleBase;

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

            layout = new NosTaleClientLayout(holder, moduleBase);
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
    /// <summary>
    /// Whether a target is selected, and the candidate id behind it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Zero is an answer here, not a failure.</b> Everywhere else in this layout a
    /// null pointer means the chain broke and <see cref="TryFollow"/> refuses by name;
    /// at <see cref="TargetPointerOffset"/> zero is what the client writes when nothing
    /// is selected, and treating it as a broken read would turn "no target" into
    /// UNKNOWN — collapsing two of ADR-0018's three states into one. So the word is read
    /// directly and interpreted, rather than followed.
    /// </para>
    /// <para>
    /// The id is only ever a <i>candidate</i>: see
    /// <see cref="TargetEntityIdIsAHypothesisReason"/>. It is read and returned so an
    /// operator command can show it beside what <c>ct</c> says, which is how the
    /// hypothesis becomes a measurement — or does not.
    /// </para>
    /// </remarks>
    public bool TryReadTarget(
        ProcessMemoryReader reader,
        out TargetPointerReading reading,
        out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(reader);
        reading = default;

        if (!TryFollow(reader, _pointerHolder, "player_manager", out IntPtr manager, out failureReason))
            return false;

        MemoryReadResult pointer = reader.Read(manager + TargetPointerOffset, sizeof(int));
        if (!pointer.Ok)
        {
            failureReason = pointer.FailureReason ?? "target_pointer_unreadable";
            return false;
        }

        uint address = BitConverter.ToUInt32(pointer.Bytes);
        if (address == 0)
        {
            // The client's own "nobody", measured by the oracle rather than assumed.
            reading = new TargetPointerReading(manager, IntPtr.Zero, null, null);
            failureReason = null;
            return true;
        }

        var target = new IntPtr(address);
        MemoryReadResult id = reader.Read(target + EntityIdOffset, sizeof(int));
        int? candidate = id.Ok && id.Bytes.Length == sizeof(int)
            ? BitConverter.ToInt32(id.Bytes, 0)
            : null;

        reading = new TargetPointerReading(
            manager,
            target,
            candidate,
            candidate is null ? id.FailureReason ?? "target_entity_id_unreadable" : null);

        failureReason = null;
        return true;
    }

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
    /// Follows the chain far enough to hand back the two bases it resolves, so a
    /// caller can express an address it found as a distance from one of them.
    /// </summary>
    /// <remarks>
    /// An address found by scanning is a fact about one run of the process; a
    /// distance from a base that is resolved again on every attach is a fact
    /// about the client. This is what turns the first into the second, and it is
    /// why nothing here is cached: both bases move, and the manager is replaced
    /// on a map change.
    /// </remarks>
    public bool TryResolveBases(
        ProcessMemoryReader reader,
        out IntPtr playerManager,
        out IntPtr playerObject,
        out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(reader);
        playerObject = IntPtr.Zero;

        if (!TryFollow(reader, _pointerHolder, "player_manager", out playerManager, out failureReason))
            return false;

        return TryFollow(
            reader, playerManager + PlayerObjectOffset, "player_object", out playerObject, out failureReason);
    }

    /// <summary>
    /// Scans the player manager and player object windows for the stats-block
    /// shape. Followed on every call, never cached: both bases move, and a
    /// remembered address is whatever occupies that memory after.
    /// </summary>
    /// <remarks>
    /// Zero hits is a successful scan that found nothing, not a failed read.
    /// Both windows unreadable is a failed read. The RVA the third source
    /// printed is not consulted.
    /// </remarks>
    public bool TryScanPlayerVitals(
        ProcessMemoryReader reader,
        out IReadOnlyList<PlayerVitalsHit> hits,
        out string? failureReason,
        int windowBytes = PlayerVitalsScan.DefaultWindowBytes)
    {
        ArgumentNullException.ThrowIfNull(reader);
        hits = Array.Empty<PlayerVitalsHit>();

        if (!TryResolveBases(reader, out IntPtr manager, out IntPtr playerObject, out failureReason))
            return false;

        // Bounded before it sizes a read, and by this scan's own limit rather
        // than the map id finder's anchor rule, which is a different question.
        int window = PlayerVitalsScan.ClampWindow(windowBytes);
        var found = new List<PlayerVitalsHit>();

        MemoryReadResult managerBytes = reader.Read(manager, window);
        if (managerBytes.Ok)
            PlayerVitalsScan.Collect(managerBytes.Bytes, MapIdAnchorKind.PlayerManager, found);

        MemoryReadResult objectBytes = reader.Read(playerObject, window);
        if (objectBytes.Ok)
            PlayerVitalsScan.Collect(objectBytes.Bytes, MapIdAnchorKind.PlayerObject, found);

        if (!managerBytes.Ok && !objectBytes.Ok)
        {
            failureReason = managerBytes.FailureReason
                            ?? objectBytes.FailureReason
                            ?? "player_vitals_windows_unreadable";
            return false;
        }

        hits = found;
        failureReason = null;
        return true;
    }

    /// <summary>
    /// The unique structural candidate, or a named refusal when the scan is
    /// empty or ambiguous. Still UNKNOWN: uniqueness is not concordance.
    /// </summary>
    public bool TryReadPlayerVitals(
        ProcessMemoryReader reader,
        out PlayerVitalsCandidate reading,
        out string? failureReason)
    {
        reading = default;
        if (!TryScanPlayerVitals(reader, out IReadOnlyList<PlayerVitalsHit> hits, out failureReason))
            return false;

        if (hits.Count == 0)
        {
            reading = PlayerVitalsCandidate.Missing(PlayerVitalsCandidate.NotFoundReason);
            failureReason = PlayerVitalsCandidate.NotFoundReason;
            return false;
        }

        if (hits.Count > 1)
        {
            failureReason = string.Create(CultureInfo.InvariantCulture,
                $"{PlayerVitalsCandidate.AmbiguousPrefix}:{hits.Count}");
            reading = PlayerVitalsCandidate.Missing(failureReason);
            return false;
        }

        reading = PlayerVitalsCandidate.From(hits[0]);
        failureReason = null;
        return true;
    }

    /// <summary>
    /// Reads the map id the client keeps at <see cref="MapIdModuleOffset"/>, or
    /// says why it could not.
    /// </summary>
    /// <remarks>
    /// Read on every call and never cached, for the same reason as
    /// <see cref="TryReadPlayer"/>: the value is the answer to "where is the
    /// character now", and a remembered one answers "where was it".
    /// </remarks>
    public bool TryReadMapId(
        ProcessMemoryReader reader,
        out int mapId,
        out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(reader);
        mapId = 0;

        if (_moduleBase == IntPtr.Zero)
        {
            failureReason = "client_module_not_located";
            return false;
        }

        MemoryReadResult bytes = reader.Read(_moduleBase + MapIdModuleOffset, sizeof(int));
        if (!bytes.Ok)
        {
            failureReason = bytes.FailureReason ?? "map_id_unreadable";
            return false;
        }

        int value = BitConverter.ToInt32(bytes.Bytes);

        // A negative id is not a small mistake, it is a different field: the
        // extracted ids are all positive and under a thousand, so a value outside
        // that shape is reported rather than carried into a file lookup.
        if (value < 0)
        {
            failureReason = $"{MapIdImplausiblePrefix}:{value.ToString(CultureInfo.InvariantCulture)}";
            return false;
        }

        mapId = value;
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
                (ushort)(packed >> 16),
                TryReadEntityName(reader, entity, kind)));
        }

        entities = found;
        failureReason = null;
        return true;
    }

    /// <summary>
    /// Which name chain this kind has, when it has one.
    /// </summary>
    /// <remarks>
    /// Player and NPC have no demonstrated chain. Returning false is the
    /// honest answer, not an invitation to reuse the monster offsets.
    /// </remarks>
    public static bool TryNameChain(
        MapEntityKind kind, out int fromEntity, out int fromNameObject, out string? failureReason)
    {
        switch (kind)
        {
            case MapEntityKind.Monster:
                fromEntity = MonsterNameObjectOffset;
                fromNameObject = MonsterNamePointerOffset;
                failureReason = null;
                return true;
            case MapEntityKind.GroundItem:
                fromEntity = GroundItemNameObjectOffset;
                fromNameObject = GroundItemNamePointerOffset;
                failureReason = null;
                return true;
            default:
                fromEntity = 0;
                fromNameObject = 0;
                failureReason =
                    $"{EntityNameCandidate.ChainNotEstablishedPrefix}:{kind.ToString().ToLowerInvariant()}";
                return false;
        }
    }

    /// <summary>
    /// Follows this kind's candidate name chain and parses the ANSI string.
    /// </summary>
    /// <remarks>
    /// Followed on every call, never cached: the manager is replaced on a map
    /// change, and a remembered pointer is whatever occupies that address after.
    /// A successful parse is still
    /// <see cref="EntityNameCandidate.NotEstablishedReason"/>.
    /// </remarks>
    public static EntityNameCandidate TryReadEntityName(
        ProcessMemoryReader reader, IntPtr entity, MapEntityKind kind)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (entity == IntPtr.Zero)
            return EntityNameCandidate.Missing("entity_name_object_null");

        if (!TryNameChain(kind, out int fromEntity, out int fromNameObject, out string? noChain))
            return EntityNameCandidate.Missing(noChain!);

        if (!TryFollow(reader, entity + fromEntity, $"{kind.ToString().ToLowerInvariant()}_name_object",
                out IntPtr nameObject, out string? objectFailure))
            return EntityNameCandidate.Missing(objectFailure!);

        if (!TryFollow(reader, nameObject + fromNameObject, $"{kind.ToString().ToLowerInvariant()}_name_pointer",
                out IntPtr namePointer, out string? pointerFailure))
            return EntityNameCandidate.Missing(pointerFailure!);

        MemoryReadResult bytes = reader.Read(namePointer, EntityNameText.MaxBytes);
        if (!bytes.Ok)
            return EntityNameCandidate.Missing(bytes.FailureReason ?? "entity_name_unreadable");

        return EntityNameText.TryParseAnsi(bytes.Bytes, out string? name, out string? parseFailure)
            ? EntityNameCandidate.Candidate(name!)
            : EntityNameCandidate.Missing(parseFailure!);
    }

    /// <summary>
    /// Whether a matched operand could be an object pointer at all, and which
    /// check says it could not.
    /// </summary>
    /// <param name="operand">The four bytes the match hands back, read as little-endian.</param>
    /// <param name="moduleBase">Base of the client image this match was found in.</param>
    /// <param name="rejection">The named check that refused. Null exactly when the operand passes.</param>
    /// <remarks>
    /// <para>
    /// <b>Where this came from.</b> On 3 September 2026, on a live client in game
    /// with the console elevated, <c>--entity-names</c> refused all four lists
    /// with <c>scene_manager_not_confirmed:1_candidates:0xFFFFFFFF:</c>
    /// <c>player_list_pointer_unreadable_at+0xC</c>. Two things were wrong with
    /// that. The address was <c>0xFFFFFFFF</c> — every bit set, which no allocator
    /// returns — so the pattern had landed in a run of <c>FF</c> filler and read
    /// more <c>FF</c> filler as an address. And the reason blamed the list pointer,
    /// pointing the next hour of work at <see cref="PlayerListOffset"/>, when
    /// nothing was ever wrong at <c>+0x0C</c>: the operand should never have been
    /// followed. A refusal that names the wrong cause costs more than no refusal.
    /// </para>
    /// <para>
    /// <b>The three things a pointer on this client cannot be.</b> Zero is nothing
    /// to follow. <c>0xFFFFFFFF</c> is filler — it is also the last addressable
    /// byte of a 32-bit space, so a four-byte read there could not complete even
    /// if something were mapped. An address that is not four-byte aligned is not
    /// the start of an object this layout can read: every field it reaches through
    /// the scene manager is a 32-bit word at a fixed offset, and Windows hands back
    /// heap blocks aligned to at least eight bytes in a 32-bit process, so an
    /// unaligned base would be a coincidence rather than an allocation. And an
    /// address below the image base is below anything this client has been seen to
    /// allocate: the two object pointers actually measured on it are
    /// <c>0x22C8A4F0</c> and <c>0x1F5BA4F0</c> (recorded at
    /// <see cref="TargetPointerOffset"/>), more than a hundred times a typical
    /// <c>0x400000</c> image base.
    /// </para>
    /// <para>
    /// <b>Order matters.</b> All-bits-set is tested before alignment because
    /// <c>0xFFFFFFFF</c> fails alignment too, and reporting the misalignment would
    /// name a symptom where the evidence names filler.
    /// </para>
    /// <para>
    /// <b>What is deliberately not checked.</b> Not an upper bound: a
    /// <c>LARGEADDRESSAWARE</c> 32-bit process on 64-bit Windows can hold pointers
    /// up to <c>0xFFFFFFFE</c>, so any ceiling short of the whole space would be a
    /// guess that could reject a real address. Not "inside the image" either: every
    /// object pointer this project has measured on this client is a heap address,
    /// but one client is one sample and a static in <c>.data</c> is possible in
    /// principle — this rejects what cannot be a pointer, not what is unlikely to
    /// be one.
    /// </para>
    /// <para>
    /// <b>And not a committed-region check.</b>
    /// <see cref="ProcessMemoryReader.EnumerateRegions"/> could say whether the
    /// address is mapped, but it answers a question the very next step already
    /// answers: <see cref="TryConfirmSceneManager"/> reads at the candidate and
    /// reports the OS failure by name when it is not mapped. Paying a full
    /// <c>VirtualQueryEx</c> walk of the address space per candidate to pre-empt a
    /// read that costs one syscall is the wrong trade, and caching the walk is not
    /// available: this chain is deliberately re-followed on every call, so a
    /// remembered region map would be a statement about a previous moment. Keeping
    /// it out is also what makes this predicate pure — no process, no platform, no
    /// handle — and therefore testable at all.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The module base is not positive. A base is resolved before any match is
    /// examined, so a missing one is a caller defect and not a reading: refusing it
    /// by name here would let the bound silently vanish, and a bound that quietly
    /// stops applying is the decoration this file exists to avoid.
    /// </exception>
    public static bool IsPlausibleSceneOperand(uint operand, IntPtr moduleBase, out string? rejection)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(moduleBase.ToInt64());

        if (operand == 0)
        {
            rejection = SceneOperandNullReason;
            return false;
        }

        if (operand == uint.MaxValue)
        {
            rejection = SceneOperandAllBitsSetReason;
            return false;
        }

        if ((operand & 0x3) != 0)
        {
            rejection = SceneOperandMisalignedReason;
            return false;
        }

        // Compared as signed 64-bit on both sides: the operand is 32-bit and
        // unsigned, the base is a pointer-sized value, and narrowing either one to
        // meet the other is how a comparison silently stops meaning what it says.
        if ((long)operand < moduleBase.ToInt64())
        {
            // The operand is not repeated: every caller already has it — the
            // resolver prefixes it onto the reason, a direct caller passed it in.
            // The base is what the reason adds, because nothing else names it.
            rejection = string.Create(CultureInfo.InvariantCulture,
                $"{SceneOperandBelowModuleBasePrefix}:0x{moduleBase.ToInt64():X}");
            return false;
        }

        rejection = null;
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
    /// So every match is tried and each candidate has to prove itself, in two
    /// stages that fail differently. First the operand has to be capable of being
    /// a pointer at all — <see cref="IsPlausibleSceneOperand"/>, which costs no
    /// read and is what stops <c>0xFFFFFFFF</c> from being dereferenced and then
    /// blamed on a list offset. Only then is it followed: its four list pointers
    /// must be readable and their lengths plausible. This is the same rule the
    /// character id enforces one level up — a signature is a hypothesis, and the
    /// thing that turns it into a reading is a check it could not have passed by
    /// accident.
    /// </para>
    /// </remarks>
    private static bool TryResolveScene(
        ProcessMemoryReader reader, IntPtr moduleBase, long moduleSize,
        out IntPtr scene, out string? failureReason)
    {
        scene = IntPtr.Zero;
        failureReason = null;

        // Not simply non-zero: a base that is zero or negative is not a base, and
        // IsPlausibleSceneOperand throws on one rather than let its bound quietly
        // stop applying. The refusal is named here so that never becomes an
        // exception escaping a read path.
        if (moduleBase.ToInt64() <= 0 || moduleSize <= 0)
        {
            failureReason = "client_module_not_located";
            return false;
        }

        SignatureByte[] signature = ParseSignature(SceneManagerSignature);
        const int sliceLength = 1 << 20;
        int overlap = signature.Length - 1;
        var candidates = 0;
        string? firstRejection = null;

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

                uint operandValue = BitConverter.ToUInt32(operand.Bytes);
                if (!IsPlausibleSceneOperand(operandValue, moduleBase, out string? implausible))
                {
                    // A zero operand is not a candidate and never was one: this
                    // signature is eleven zero bytes long in the middle and lands
                    // in padding routinely, so counting those would bury the count
                    // the operator reads. Every other implausible operand is
                    // counted, because it is a place that looked like the scene
                    // manager right up until the operand was examined — and saying
                    // so is the difference between "one candidate, and here is what
                    // was wrong with it" and a signature that must be re-derived.
                    if (implausible == SceneOperandNullReason)
                        continue;

                    candidates++;
                    firstRejection ??= string.Create(CultureInfo.InvariantCulture,
                        $"0x{operandValue:X}:{implausible}");
                    continue;
                }

                var pointer = (IntPtr)operandValue;

                candidates++;
                if (!TryConfirmSceneManager(reader, pointer, out string? rejection))
                {
                    // The first rejection is the one reported. With a single
                    // candidate it is the whole answer, and with several the
                    // operator still gets a check they can act on rather than a
                    // count they cannot.
                    firstRejection ??= string.Create(CultureInfo.InvariantCulture,
                        $"0x{pointer.ToInt64():X}:{rejection}");
                    continue;
                }

                scene = pointer;
                return true;
            }
        }

        failureReason = candidates == 0
            ? "scene_manager_signature_not_found"
            : string.Create(CultureInfo.InvariantCulture,
                $"scene_manager_not_confirmed:{candidates}_candidates:{firstRejection}");
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
        => TryConfirmSceneManager(reader, candidate, out _);

    /// <summary>
    /// Whether a candidate behaves like the scene manager, and which check said no.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All four lists must be readable and sized plausibly. A stray pointer clears
    /// one of those by luck now and then; clearing all four is what makes this a
    /// check rather than a guess.
    /// </para>
    /// <para>
    /// <b>Why the reason is carried out.</b> A bare <c>false</c> here reaches the
    /// operator as « 1 candidate rejected », which names nothing they can act on:
    /// a wrong offset for this client build and a list the client legitimately
    /// keeps null read exactly the same. The reason names the list, the offset and
    /// the value seen, so the next decision is taken on a reading instead of on a
    /// hunch.
    /// </para>
    /// </remarks>
    internal static bool TryConfirmSceneManager(
        ProcessMemoryReader reader, IntPtr candidate, out string? failureReason)
    {
        foreach ((string name, int listOffset) in new[]
                 {
                     ("player", PlayerListOffset),
                     ("monster", MonsterListOffset),
                     ("npc", NpcListOffset),
                     ("ground", GroundItemListOffset),
                 })
        {
            MemoryReadResult listPointer = reader.Read(candidate + listOffset, sizeof(int));
            if (!listPointer.Ok)
            {
                failureReason = string.Create(CultureInfo.InvariantCulture,
                    $"{name}_list_pointer_unreadable_at+0x{listOffset:X}");
                return false;
            }

            var list = (IntPtr)BitConverter.ToUInt32(listPointer.Bytes);
            if (list == IntPtr.Zero)
            {
                failureReason = string.Create(CultureInfo.InvariantCulture,
                    $"{name}_list_pointer_null_at+0x{listOffset:X}");
                return false;
            }

            MemoryReadResult lengthBytes = reader.Read(list + ListLengthOffset, sizeof(int));
            if (!lengthBytes.Ok)
            {
                failureReason = string.Create(CultureInfo.InvariantCulture,
                    $"{name}_list_length_unreadable_at_0x{list.ToInt64():X}+0x{ListLengthOffset:X}");
                return false;
            }

            int length = BitConverter.ToInt32(lengthBytes.Bytes);
            if (length < 0 || length > MaxEntitiesPerList)
            {
                failureReason = string.Create(CultureInfo.InvariantCulture,
                    $"{name}_list_length_implausible:{length}");
                return false;
            }

            // A non-empty list must have an array behind it. An empty one need
            // not, and an empty monster list is an ordinary quiet map.
            if (length == 0)
                continue;

            MemoryReadResult arrayPointer = reader.Read(list + ListArrayOffset, sizeof(int));
            if (!arrayPointer.Ok)
            {
                failureReason = string.Create(CultureInfo.InvariantCulture,
                    $"{name}_list_array_unreadable:{length}_entries");
                return false;
            }

            var array = (IntPtr)BitConverter.ToUInt32(arrayPointer.Bytes);
            if (array == IntPtr.Zero || !reader.Read(array, sizeof(int)).Ok)
            {
                failureReason = string.Create(CultureInfo.InvariantCulture,
                    $"{name}_list_array_unusable:{length}_entries_at_0x{array.ToInt64():X}");
                return false;
            }
        }

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
/// <summary>What the target pointer says, and the candidate behind it.</summary>
/// <param name="PlayerManager">The manager the offset was measured from.</param>
/// <param name="TargetObject">The selected entity's object, or zero when nothing is selected.</param>
/// <param name="CandidateEntityId">
/// The word at <c>[TargetObject] + EntityIdOffset</c>, by analogy with the player object.
/// A <b>candidate</b>, never a reading: see
/// <see cref="NosTaleClientLayout.TargetEntityIdIsAHypothesisReason"/>.
/// </param>
/// <param name="CandidateFailureReason">Why there is no candidate, when there is none.</param>
public readonly record struct TargetPointerReading(
    IntPtr PlayerManager,
    IntPtr TargetObject,
    int? CandidateEntityId,
    string? CandidateFailureReason)
{
    /// <summary>True exactly when the client has something selected.</summary>
    public bool HasTarget => TargetObject != IntPtr.Zero;
}

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
/// <param name="Name">
/// Candidate display name. Always UNKNOWN: see
/// <see cref="EntityNameCandidate"/>.
/// </param>
public readonly record struct MapEntityReading(
    long EntityId,
    ushort X,
    ushort Y,
    EntityNameCandidate Name = default);

/// <summary>Which of the scene manager's four lists to read.</summary>
public enum MapEntityKind
{
    Player,
    Monster,
    Npc,
    GroundItem,
}
