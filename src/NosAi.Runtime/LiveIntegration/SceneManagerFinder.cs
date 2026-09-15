using System;
using System.Globalization;
using System.Runtime.Versioning;

namespace NosAi.LiveIntegration;

public static class SceneManagerFinder
{
    public const string CandidatePath = "data/scenemanager_candidates.txt";

    public static int Run(string? candidatePath = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Reading process memory needs Windows.");
            return 2;
        }

        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure))
        {
            Console.WriteLine($"[REFUSED] {attachFailure}");
            return 1;
        }

        using (session)
        {
            if (!session!.TryReadPlayer(out PlayerObjectReading player, out string? readFailure))
            {
                Console.WriteLine($"[REFUSED] {readFailure}");
                return 1;
            }

            bool InModule(MemoryRegion region) => region.BaseAddress.ToInt64() >= session.ModuleBase.ToInt64() && region.BaseAddress.ToInt64() - session.ModuleBase.ToInt64() < session.ModuleSize;

            var regions = session.Reader.EnumerateRegions();
            foreach (MemoryRegion region in regions)
            {
                if (!region.IsPrivate && !(region.IsWritable && InModule(region)))
                    continue;

                long offset = 0;
                while (offset < region.Size)
                {
                    int length = (int)Math.Min(64 * 1024, region.Size - offset);
                    var address = new IntPtr(region.BaseAddress.ToInt64() + offset);
                    MemoryReadResult read = session.Reader.Read(address, length);
                    if (!read.Ok)
                    {
                        offset += length;
                        continue;
                    }

                    ReadOnlySpan<byte> window = read.Bytes;
                    for (int i = 0; i + 4 <= window.Length; i += 4)
                    {
                        uint operand = BitConverter.ToUInt32(window.Slice(i, 4));
                        if (!NosTaleClientLayout.IsPlausibleSceneOperand(operand, session.ModuleBase, out _))
                            continue;

                        if (!NosTaleClientLayout.TryConfirmSceneManager(session.Reader, (IntPtr)operand, out _))
                            continue;

                        if (PlayerListContainsEntity(session.Reader, (IntPtr)operand, player.EntityId))
                        {
                            long offsetFromModule = operand - (uint)session.ModuleBase.ToInt64();
                            string absoluteAddress = ((IntPtr)operand).ToString("X");
                            Console.WriteLine($"0x{offsetFromModule:X} 0x{absoluteAddress}");
                            string candidateLine = $"0x{offsetFromModule:X}";
                            string? directory = Path.GetDirectoryName(candidatePath ?? CandidatePath);
                            if (!string.IsNullOrEmpty(directory))
                                Directory.CreateDirectory(directory);
                            File.WriteAllText(candidatePath ?? CandidatePath, candidateLine);
                            return 0;
                        }
                    }

                    offset += length;
                }
            }

            Console.WriteLine("No candidate passed all checks.");
            return 1;
        }
    }

    internal static bool PlayerListContainsEntity(ProcessMemoryReader reader, IntPtr sceneCandidate, int entityId)
    {
        var playerListPtr = reader.Read(sceneCandidate + NosTaleClientLayout.PlayerListOffset, sizeof(int));
        if (!playerListPtr.Ok)
            return false;

        IntPtr list = (IntPtr)(uint)BitConverter.ToUInt32(playerListPtr.Bytes);
        if (list == IntPtr.Zero)
            return false;

        var lengthPtr = reader.Read(list + NosTaleClientLayout.ListLengthOffset, sizeof(int));
        if (!lengthPtr.Ok)
            return false;

        int length = BitConverter.ToInt32(lengthPtr.Bytes);
        if (length <= 0 || length > NosTaleClientLayout.MaxEntitiesPerList)
            return false;

        var arrayPtr = reader.Read(list + NosTaleClientLayout.ListArrayOffset, sizeof(int));
        if (!arrayPtr.Ok)
            return false;

        IntPtr array = (IntPtr)(uint)BitConverter.ToUInt32(arrayPtr.Bytes);
        if (array == IntPtr.Zero)
            return false;

        var entities = reader.Read(array, length * sizeof(int));
        if (!entities.Ok)
            return false;

        ReadOnlySpan<byte> entityBytes = entities.Bytes;
        for (int i = 0; i < length; i++)
        {
            IntPtr entityPtr = (IntPtr)(uint)BitConverter.ToUInt32(entityBytes.Slice(i * sizeof(int), sizeof(int)));
            if (entityPtr == IntPtr.Zero)
                continue;

            var idPtr = reader.Read(entityPtr + NosTaleClientLayout.EntityIdOffset, sizeof(int));
            if (!idPtr.Ok)
                continue;

            int id = BitConverter.ToInt32(idPtr.Bytes);
            if (id == entityId)
                return true;
        }

        return false;
    }
}
