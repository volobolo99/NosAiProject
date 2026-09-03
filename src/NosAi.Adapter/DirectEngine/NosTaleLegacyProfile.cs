namespace NosAi.Adapter.DirectEngine;

/// <summary>
/// The reference bot's signatures, masks and offsets, transcribed into a profile.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a candidate, not a fact.</b> It is <see cref="EngineValidationState.Unvalidated"/>
/// when built and stays that way until a resolver has both checked it and found its
/// signatures in a real client module. The numbers come from one bot compiled
/// against one build of one client; nothing here asserts that they are true of the
/// client on this machine, and the fail-closed chain in
/// <see cref="DirectEngineAdapter"/> is what stops them being used as if they were.
/// It exists so that "which legacy capabilities does the contract cover" has a
/// concrete answer rather than a list of names.
/// </para>
/// <para>
/// <b>Three corrections were made in transcription</b>, each because the original
/// form cannot be represented honestly:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <b>Absolute addresses became module offsets.</b> The reference pins its call
/// context pointers to <c>0x008F4904</c> and <c>0x00765EA8</c>, which are the
/// module-relative <c>0x4F4904</c> and <c>0x365EA8</c> plus an assumed image base
/// of <c>0x400000</c> — the same variables its own pointer walks address
/// relatively. Under ASLR the absolute form is wrong; only the relative form
/// survives, so only the relative form is recorded.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Pet and partner are separate capabilities.</b> The reference selects between
/// them with a boolean argument, and its own caller passes the partner value on the
/// pet branch, so ticking "pet attacks" commanded the partner twice.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Rest is given its signature.</b> The reference declares <c>REST_PATTERN</c>
/// and then never resolves it — <c>lpvRest</c> is never assigned, so <c>Rest()</c>
/// calls through a null pointer. The pattern is recorded here so the capability can
/// actually resolve, and refuse cleanly when it does not.
/// </description>
/// </item>
/// </list>
/// </remarks>
public static class NosTaleLegacyProfile
{
    /// <summary>The image base the reference's absolute constants assume.</summary>
    public const uint AssumedImageBase = 0x0040_0000;

    /// <summary>Module offset of the player-manager pointer the engine calls take as context.</summary>
    /// <remarks><c>0x008F4904</c> in the reference, i.e. this plus <see cref="AssumedImageBase"/>.</remarks>
    public const uint PlayerManagerOffset = 0x004F_4904;

    /// <summary>Module offset of the pet and partner manager pointer.</summary>
    public const uint MateManagerOffset = 0x004F_4908;

    /// <summary>Module offset of the context the collect call takes.</summary>
    /// <remarks><c>0x00765EA8</c> in the reference.</remarks>
    public const uint CollectContextOffset = 0x0036_5EA8;

    /// <summary>The version label this transcription carries.</summary>
    public const string ClientVersion = "nostale-legacy-reference";

    /// <summary>The character's cell: two 16-bit halves at <c>+0x00</c> and <c>+0x02</c>.</summary>
    public const string PlayerPositionPath = "player.position";

    /// <summary>Max MP, MP, max HP, HP at <c>+0x00</c>, <c>+0x04</c>, <c>+0xF0</c>, <c>+0xF4</c>.</summary>
    public const string PlayerVitalsPath = "player.vitals";

    /// <summary>The attack range the reference derived every engagement distance from.</summary>
    public const string PlayerRangePath = "player.range";

    public const string MonsterListPath = "map.monsters.list";

    public const string MonsterCountPath = "map.monsters.count";

    public const string GroundItemListPath = "map.items.list";

    public const string GroundItemCountPath = "map.items.count";

    /// <summary>Cooldowns for skills 1 to 4, stride <c>0x48</c>.</summary>
    public const string SkillCooldownLowPath = "player.skills.cooldown.low";

    /// <summary>Cooldowns for skills 5 and above, same stride.</summary>
    public const string SkillCooldownHighPath = "player.skills.cooldown.high";

    /// <summary>The partner, one hop off the mate manager.</summary>
    public const string PartnerPath = "mate.partner";

    /// <summary>The pet, one hop off the mate manager.</summary>
    public const string PetPath = "mate.pet";

    /// <summary>Builds the candidate profile. Unvalidated until a resolver says otherwise.</summary>
    public static EngineClientProfile Create() => new(
        ClientVersion,
        EngineArchitecture.X86,
        "NostaleClientX.exe",
        Signatures(),
        PointerPaths(),
        ContextOffsets());

    private static IEnumerable<EngineSignature> Signatures()
    {
        yield return new EngineSignature(
            EngineCapability.Move,
            "move",
            new byte[]
            {
                0x55, 0x8B, 0xEC, 0x83, 0xC4, 0x00, 0x53, 0x56,
                0x57, 0x66, 0x89, 0x00, 0x00, 0x89, 0x55
            },
            "xxxxx?xxxxx??xx");

        yield return new EngineSignature(
            EngineCapability.Attack,
            "attack",
            new byte[]
            {
                0x6A, 0x00, 0x6A, 0x00, 0x6A, 0x00, 0xE8, 0x00,
                0x00, 0x00, 0x00, 0xC3, 0x55
            },
            "x?x?x?x????xx");

        yield return new EngineSignature(
            EngineCapability.AttackRun,
            "attack_run",
            new byte[]
            {
                0x55, 0x8B, 0xEC, 0x51, 0x53, 0x56, 0x57, 0x88,
                0x4D, 0x00, 0x8B, 0xF2, 0x8B, 0xF8
            },
            "xxxxxxxxx?xxxx");

        yield return new EngineSignature(
            EngineCapability.Collect,
            "collect",
            new byte[]
            {
                0x55, 0x8B, 0xEC, 0x6A, 0x00, 0x6A, 0x00, 0x6A,
                0x00, 0x6A, 0x00, 0x53, 0x56, 0x8B, 0xD9, 0x8B,
                0xF2, 0x33, 0xC0, 0x55, 0x68, 0x00, 0x00, 0x00,
                0x00, 0x64, 0xFF, 0x00, 0x64, 0x89, 0x00, 0xA1
            },
            "xxxx?x?x?x?xxxxxxxxxx????xx?xx?x");

        yield return new EngineSignature(
            EngineCapability.Rest,
            "rest",
            new byte[]
            {
                0x55, 0x8B, 0xEC, 0xB9, 0x00, 0x00, 0x00, 0x00,
                0x6A, 0x00, 0x6A, 0x00, 0x49, 0x75, 0x00, 0x51,
                0x53, 0x56, 0x57, 0x33, 0xC0
            },
            "xxxx????x?x?xx?xxxxxx");

        // One entry point, two capabilities: the reference chose between pet and
        // partner with an argument, and that argument is a capability boundary.
        byte[] movePet =
        {
            0x55, 0x8B, 0xEC, 0x83, 0xC4, 0x00, 0x53, 0x56,
            0x57, 0x8B, 0xF9, 0x89, 0x55, 0x00, 0x8B, 0xD8,
            0xC6, 0x45
        };

        yield return new EngineSignature(EngineCapability.MovePet, "move_pet", movePet, "xxxxx?xxxxxxx?xxxx");
        yield return new EngineSignature(EngineCapability.MovePartner, "move_partner", movePet, "xxxxx?xxxxxxx?xxxx");

        byte[] attackPet =
        {
            0x53, 0x56, 0x8B, 0xF2, 0x8B, 0xD8, 0x8B, 0xC3,
            0xE8, 0x00, 0x00, 0x00, 0x00, 0x84, 0xC0, 0x74,
            0x00, 0x83, 0xBB
        };

        yield return new EngineSignature(
            EngineCapability.AttackWithPet, "attack_pet", attackPet, "xxxxxxxxx????xxx?xx");
        yield return new EngineSignature(
            EngineCapability.AttackWithPartner, "attack_partner", attackPet, "xxxxxxxxx????xxx?xx");
    }

    private static IEnumerable<EnginePointerPath> PointerPaths()
    {
        yield return new EnginePointerPath(PlayerPositionPath, PlayerManagerOffset, 0x20, 0x0C);
        yield return new EnginePointerPath(PlayerRangePath, PlayerManagerOffset, 0x68);
        yield return new EnginePointerPath(PlayerVitalsPath, 0x004F_4BA8, 0xE4, 0x100, 0x4C8, 0x8B8);
        yield return new EnginePointerPath(MonsterListPath, 0x0035_66D8, 0xEA4, 0x4, 0x5E4, 0x0);
        yield return new EnginePointerPath(MonsterCountPath, 0x0035_82C0, 0x8, 0x4, 0x60, 0x4, 0x608);
        yield return new EnginePointerPath(GroundItemListPath, 0x0035_66D8, 0xEB0, 0x4, 0x5C4, 0x0);
        yield return new EnginePointerPath(GroundItemCountPath, 0x0035_82C0, 0x8, 0x4, 0x7C, 0x4, 0x568);
        yield return new EnginePointerPath(SkillCooldownLowPath, 0x004F_4DD0, 0x158, 0x4, 0x4, 0x0, 0x24);
        yield return new EnginePointerPath(SkillCooldownHighPath, 0x004F_4CDC, 0x20, 0x4, 0x88, 0xE28, 0x24);
        yield return new EnginePointerPath(PartnerPath, MateManagerOffset, 0x4, 0x0);
        yield return new EnginePointerPath(PetPath, MateManagerOffset, 0x4, 0x4);
    }

    private static IEnumerable<KeyValuePair<EngineCapability, uint>> ContextOffsets()
    {
        yield return new KeyValuePair<EngineCapability, uint>(EngineCapability.Move, PlayerManagerOffset);
        yield return new KeyValuePair<EngineCapability, uint>(EngineCapability.Attack, PlayerManagerOffset);
        yield return new KeyValuePair<EngineCapability, uint>(EngineCapability.AttackRun, PlayerManagerOffset);
        yield return new KeyValuePair<EngineCapability, uint>(EngineCapability.Collect, CollectContextOffset);
        yield return new KeyValuePair<EngineCapability, uint>(EngineCapability.MovePet, MateManagerOffset);
        yield return new KeyValuePair<EngineCapability, uint>(EngineCapability.MovePartner, MateManagerOffset);
        yield return new KeyValuePair<EngineCapability, uint>(EngineCapability.AttackWithPet, MateManagerOffset);
        yield return new KeyValuePair<EngineCapability, uint>(EngineCapability.AttackWithPartner, MateManagerOffset);

        // Rest takes no context object in the reference: it loads a hardcoded
        // constant into EAX and calls. That constant is not recorded, because a
        // literal address with no derivation is not something a profile can honestly
        // claim about any build.
    }
}
