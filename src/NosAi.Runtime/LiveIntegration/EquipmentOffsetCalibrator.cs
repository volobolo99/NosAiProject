using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Runtime.Versioning;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Perception.Network;

namespace NosAi.LiveIntegration
{
    public readonly record struct EquipmentArrayHit(IntPtr BaseAddress, ImmutableDictionary<int, int> SlotVnums);

    public static class EquipmentOffsetCalibrator
    {
        public const string Flag = "--calibrate-equipment";
        public const string NoEquipReadingReason = "calibrate_equipment_no_equip_reading";
        public const string UnchangedPrefix = "calibrate_equipment_set_did_not_change";
        public const string NoCandidatePrefix = "calibrate_equipment_no_address_held_the_array";
        public const string AmbiguousPrefix = "calibrate_equipment_ambiguous";
        public const string EquipFeedUnavailableReason = "calibrate_equipment_feed_unavailable";
        public const string NotWindowsReason = "calibrate_equipment_requires_windows";

        public static ImmutableDictionary<int, int> KeepOnlyNonZero(NosAi.Runtime.Perception.Network.WornEquipment reading)
        {
            var builder = ImmutableDictionary.CreateBuilder<int, int>();
            foreach (var slot in reading.Slots)
            {
                if (slot.Vnum != 0)
                    builder[slot.Slot] = slot.Vnum;
            }
            return builder.ToImmutable();
        }

        public static bool TryPickAnchor(ImmutableDictionary<int, int> slotVnums, out int anchorSlot, out int anchorVnum)
        {
            anchorSlot = 0;
            anchorVnum = 0;
            
            if (slotVnums.IsEmpty)
                return false;
                
            // Get the first slot in ascending order (EquipmentSlot enum is ordered 0..17)
            var minSlot = slotVnums.Keys.Min();
            anchorSlot = minSlot;
            anchorVnum = slotVnums[minSlot];
            return true;
        }

        public static List<EquipmentArrayHit> KeepMatchingArray(IReadOnlyList<IntPtr> anchorAddresses, int anchorSlot, ImmutableDictionary<int, int> expectedSlotVnums, Func<IntPtr, int?> readInt32)
        {
            var survivors = new List<EquipmentArrayHit>();
            
            foreach (IntPtr anchorAddress in anchorAddresses)
            {
                IntPtr baseAddress = new IntPtr(anchorAddress.ToInt64() - (anchorSlot * 4));
                bool isValid = true;
                
                foreach (var kvp in expectedSlotVnums)
                {
                    int slot = kvp.Key;
                    int expectedVnum = kvp.Value;
                    IntPtr slotAddress = new IntPtr(baseAddress.ToInt64() + (slot * 4));
                    int? actualVnum = readInt32(slotAddress);
                    
                    if (actualVnum is null || actualVnum != expectedVnum)
                    {
                        isValid = false;
                        break;
                    }
                }
                
                if (isValid)
                {
                    survivors.Add(new EquipmentArrayHit(baseAddress, expectedSlotVnums));
                }
            }
            
            return survivors;
        }

        public static List<EquipmentArrayHit> Confirm(IReadOnlyList<EquipmentArrayHit> previous, ImmutableDictionary<int, int> expectedSlotVnums, Func<IntPtr, int?> readInt32)
        {
            var survivors = new List<EquipmentArrayHit>();
            
            foreach (var hit in previous)
            {
                bool isValid = true;
                IntPtr baseAddress = hit.BaseAddress;
                
                foreach (var kvp in expectedSlotVnums)
                {
                    int slot = kvp.Key;
                    int expectedVnum = kvp.Value;
                    IntPtr slotAddress = new IntPtr(baseAddress.ToInt64() + (slot * 4));
                    int? actualVnum = readInt32(slotAddress);
                    
                    if (actualVnum is null || actualVnum != expectedVnum)
                    {
                        isValid = false;
                        break;
                    }
                }
                
                if (isValid)
                {
                    survivors.Add(hit);
                }
            }
            
            return survivors;
        }

        public static bool CanConfirm(ImmutableDictionary<int, int> before, ImmutableDictionary<int, int> after)
        {
            if (before.Count != after.Count)
                return true;
                
            foreach (var kvp in before)
            {
                if (!after.TryGetValue(kvp.Key, out int afterValue) || afterValue != kvp.Value)
                    return true;
            }
            
            return false;
        }

        public static string? UnchangedReason(ImmutableDictionary<int, int> before, ImmutableDictionary<int, int> after)
        {
            if (CanConfirm(before, after))
                return null;
                
            return $"{UnchangedPrefix}";
        }

        public static string? Verdict(IReadOnlyList<EquipmentArrayHit> survivors)
        {
            if (survivors.Count == 1)
                return null;
                
            if (survivors.Count == 0)
                return $"{NoCandidatePrefix}";
                
            return $"{AmbiguousPrefix}:{survivors.Count}";
        }

        [SupportedOSPlatform("windows")]
        public static int Run(int seconds = 20)
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.WriteLine($"[REFUSED] {NotWindowsReason}");
                return 1;
            }

            if (!TryFindWindow(out ClientWindow window, out int processId, out string? windowFailure))
            {
                Console.WriteLine($"[REFUSED] {windowFailure}");
                return 1;
            }

            if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure, processId))
            {
                Console.WriteLine($"[REFUSED] {attachFailure}");
                return 1;
            }

            using (session)
            {
                ClientNetworkObservation network = ClientNetworkObserver.Observe(processId);
                if (!network.Observed || network.Primary is not ClientTcpConnection primary)
                {
                    Console.WriteLine($"[REFUSED] {EquipFeedUnavailableReason}:{network.FailureReason ?? "no_single_game_connection"}");
                    return 1;
                }

                WinDivertPacketSource? packets = WinDivertPacketSource.TryOpen(
                    primary.Remote.Address, primary.Remote.Port, out string? openFailure);
                if (packets is null)
                {
                    Console.WriteLine($"[REFUSED] {EquipFeedUnavailableReason}:{openFailure}");
                    return 1;
                }

                var endpoint = new GameEndpoint(primary.Remote.Address.ToString(), primary.Remote.Port);
                using ReassembledObservationSource observationSource =
                    ReassembledObservationSource.ForNosTaleWorld(packets, DataSourceKind.Live);
                var observer = new GameTrafficObserver(
                    observationSource,
                    new ScopedGameTrafficFilter(endpoint),
                    new NosTaleWorldProtocolDecoder());

                // Read the first equipment state
                Console.WriteLine("Round 1: wait for a real change in equipment (equip or unequip something)");
                WornEquipment? lastEquip = null;
                // The client's own equip/unequip actions publish `eq`, confirmed
                // empirically on a live client on 2026-09-12 -- `equip` never arrived
                // during ordinary play (cross-checked against Rutherther/NosSmooth's
                // independent packet definitions: `eq` carries exactly the ten
                // visually-rendered slots in a fixed order -- Hat, Armor, MainWeapon,
                // SecondaryWeapon, Mask, Fairy, CostumeSuit, CostumeHat, WeaponSkin,
                // WingSkin -- while `equip` carries the full slot-id-addressed set with
                // rarity/upgrade detail and is sent on a different, rarer trigger this
                // session did not identify). This calibrator only needs ONE consistent
                // numbering to search memory with, and `eq` is the one the client
                // actually sends during ordinary play.
                //
                // The stride-4 contiguous-array hypothesis this file searches for was
                // tried live against a real client on 2026-09-12 and did NOT hold: the
                // wire confirmed a real change, an anchor was picked, memory was
                // scanned, and zero addresses held the array. Per this contract, no
                // second attempt with a guessed stride follows a negative result --
                // that is the honest boundary of what this module proves.
                Func<WornEquipment?> readLatestEquip = () =>
                {
                    NetworkObservationReport report = observer.ObservePending();
                    if (report.Equipment is { Opcode: EquipmentWireOpcode.Eq } equip)
                        lastEquip = equip;
                    return lastEquip;
                };

                // Wait for first equipment reading
                var startTime = DateTime.UtcNow;
                WornEquipment? firstReading = null;
                while (DateTime.UtcNow.Subtract(startTime).TotalSeconds < seconds)
                {
                    firstReading = readLatestEquip();
                    if (firstReading is not null)
                        break;
                    System.Threading.Thread.Sleep(100);
                }

                if (firstReading is null)
                {
                    Console.WriteLine($"[REFUSED] {NoEquipReadingReason}");
                    return 1;
                }

                var firstSlotVnums = KeepOnlyNonZero(firstReading);
                if (!TryPickAnchor(firstSlotVnums, out int anchorSlot, out int anchorVnum))
                {
                    Console.WriteLine($"[REFUSED] {NoEquipReadingReason}");
                    return 1;
                }

                // Scan for the anchor value
                Func<IntPtr, int?> readInt32 = address =>
                {
                    MemoryReadResult result = session.Reader.Read(address, sizeof(int));
                    return result.Ok ? BitConverter.ToInt32(result.Bytes) : null;
                };

                MemoryScanner.ScanResult scan = MemoryScanner.Scan(session.Reader, anchorVnum);
                var matchingAddresses = KeepMatchingArray(scan.Addresses, anchorSlot, firstSlotVnums, readInt32);

                if (matchingAddresses.Count == 0)
                {
                    Console.WriteLine($"[REFUSED] {NoCandidatePrefix}");
                    return 1;
                }

                // Second round: readLatestEquip() remembers the round-1 reading until a
                // new packet actually arrives, so "not null" is not "changed" -- the
                // very first poll would otherwise hand back round 1's own value and
                // exit before the operator had any real chance to act.
                Console.WriteLine("Round 2: wait for another real change in equipment");
                startTime = DateTime.UtcNow;
                ImmutableDictionary<int, int> secondSlotVnums = firstSlotVnums;
                bool changed = false;
                while (DateTime.UtcNow.Subtract(startTime).TotalSeconds < seconds)
                {
                    WornEquipment? candidate = readLatestEquip();
                    if (candidate is not null)
                    {
                        ImmutableDictionary<int, int> candidateSlots = KeepOnlyNonZero(candidate);
                        if (CanConfirm(firstSlotVnums, candidateSlots))
                        {
                            secondSlotVnums = candidateSlots;
                            changed = true;
                            break;
                        }
                    }
                    System.Threading.Thread.Sleep(100);
                }

                if (!changed)
                {
                    Console.WriteLine($"[REFUSED] {UnchangedReason(firstSlotVnums, secondSlotVnums)}");
                    return 1;
                }

                var survivors = Confirm(matchingAddresses, secondSlotVnums, readInt32);
                if (Verdict(survivors) is { } verdict)
                {
                    Console.WriteLine($"[REFUSED] {verdict}");
                    return 1;
                }

                // Success
                var survivor = survivors[0];
                int anchorResult = PointerAnchorHunter.Report(session, survivor.BaseAddress, PointerAnchorHunter.DefaultSpan, "equipment");
                return anchorResult == 0 ? 0 : 1;
            }
        }

        private static bool TryFindWindow(out NosAi.Runtime.Perception.ClientWindow window, out int processId, out string? failureReason)
        {
            processId = 0;
            foreach (string name in RealClientConnector.DefaultProcessNames)
            {
                foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        NosAi.Runtime.Perception.ClientWindow? found = ClientWindowLocator.TryFind(process.Id, out string? why);
                        if (found is not null)
                        {
                            window = found;
                            processId = process.Id;
                            failureReason = null;
                            return true;
                        }

                        failureReason = why;
                    }
                }
            }

            window = null!;
            failureReason = $"window_not_located:{string.Join('/', RealClientConnector.DefaultProcessNames)}";
            return false;
        }
    }
}
