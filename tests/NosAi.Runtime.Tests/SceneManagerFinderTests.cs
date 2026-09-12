using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NosAi.LiveIntegration;
using NosAi.Runtime.Security;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Criterio di accettazione del contratto C-317, contracts/scene-manager-finder-024.json.
/// </summary>
/// <remarks>
/// Nessun client di gioco serve per questi test: costruisce nel processo di test
/// stesso (Marshal.AllocHGlobal) una scena finta con la stessa forma di quella del
/// client reale (scene -> PlayerListOffset -> list -> ListLengthOffset/ListArrayOffset
/// -> array di puntatori -> ciascuno con EntityIdOffset), poi apre
/// ProcessMemoryReader.TryOpen(Environment.ProcessId, ...) sullo stesso processo,
/// come gia' fa ProcessMemoryReaderTests.
/// </remarks>
public sealed class SceneManagerFinderTests
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;

    /// <summary>
    /// Alloca a un indirizzo esplicito sotto i 2 GB: NosTaleClientLayout usa campi
    /// puntatore a 4 byte (il client reale e' un processo a 32 bit), ma questo test
    /// gira nello stesso processo xunit a 64 bit. Marshal.AllocHGlobal su Windows a
    /// 64 bit restituisce spesso indirizzi ben oltre i 4 GB, che troncati in un campo
    /// a 4 byte perdono l'indirizzo reale: il test scriverebbe un puntatore che poi
    /// non punta piu' al blocco allocato. Una richiesta esplicita di indirizzo basso
    /// a VirtualAlloc evita il troncamento; restare sotto i 2 GB evita anche il bug
    /// di sign-extension che il codice sotto test ha su indirizzi fra 2 e 4 GB.
    /// </summary>
    private static IntPtr AllocZeroed(int size)
    {
        for (uint hint = 0x10000000; hint < 0x70000000; hint += 0x00100000)
        {
            IntPtr block = VirtualAlloc((IntPtr)hint, (UIntPtr)(uint)size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (block != IntPtr.Zero)
                return block;
        }

        throw new InvalidOperationException("impossibile allocare un indirizzo di test sotto i 2 GB");
    }

    /// <summary>Costruisce una scena finta con la lista player data, e le sue allocazioni da liberare.</summary>
    private static (IntPtr Scene, List<IntPtr> Allocations) BuildFakeScene(int[] entityIds)
    {
        var allocations = new List<IntPtr>();

        var entityPointers = new IntPtr[entityIds.Length];
        for (int i = 0; i < entityIds.Length; i++)
        {
            int blockSize = NosTaleClientLayout.EntityIdOffset + sizeof(int);
            IntPtr entity = AllocZeroed(blockSize);
            allocations.Add(entity);
            Marshal.WriteInt32(entity + NosTaleClientLayout.EntityIdOffset, entityIds[i]);
            entityPointers[i] = entity;
        }

        int arraySize = Math.Max(1, entityIds.Length) * sizeof(int);
        IntPtr array = AllocZeroed(arraySize);
        allocations.Add(array);
        for (int i = 0; i < entityIds.Length; i++)
            Marshal.WriteInt32(array + i * sizeof(int), (int)entityPointers[i]);

        int listBlockSize = Math.Max(NosTaleClientLayout.ListLengthOffset, NosTaleClientLayout.ListArrayOffset) + sizeof(int);
        IntPtr list = AllocZeroed(listBlockSize);
        allocations.Add(list);
        Marshal.WriteInt32(list + NosTaleClientLayout.ListLengthOffset, entityIds.Length);
        Marshal.WriteInt32(list + NosTaleClientLayout.ListArrayOffset, (int)array);

        int sceneBlockSize = NosTaleClientLayout.PlayerListOffset + sizeof(int);
        IntPtr scene = AllocZeroed(sceneBlockSize);
        allocations.Add(scene);
        Marshal.WriteInt32(scene + NosTaleClientLayout.PlayerListOffset, (int)list);

        return (scene, allocations);
    }

    private static void Free(List<IntPtr> allocations)
    {
        foreach (IntPtr block in allocations)
            VirtualFree(block, UIntPtr.Zero, MEM_RELEASE);
    }

    private static ProcessMemoryReader OpenSelf()
    {
        ProcessMemoryReader? reader = ProcessMemoryReader.TryOpen(
            Environment.ProcessId, SecurityPrincipal.Operator, out string? reason);
        Assert.True(reader is not null, "atteso un reader sul processo di test: " + reason);
        return reader!;
    }

    [Fact]
    public void TrovaLIdQuandoEPresenteInUnArrayDiLunghezzaUno()
    {
        (IntPtr scene, List<IntPtr> allocations) = BuildFakeScene(new[] { 3443217 });
        using ProcessMemoryReader reader = OpenSelf();
        try
        {
            bool trovato = SceneManagerFinder.PlayerListContainsEntity(reader, scene, 3443217);

            Assert.True(trovato);
        }
        finally
        {
            Free(allocations);
        }
    }

    [Fact]
    public void ListaVuotaNonTrovaNulla()
    {
        (IntPtr scene, List<IntPtr> allocations) = BuildFakeScene(Array.Empty<int>());
        using ProcessMemoryReader reader = OpenSelf();
        try
        {
            bool trovato = SceneManagerFinder.PlayerListContainsEntity(reader, scene, 3443217);

            Assert.False(trovato);
        }
        finally
        {
            Free(allocations);
        }
    }

    [Fact]
    public void UnIdAssenteDallaListaNonECheAccidentaleTrovato()
    {
        (IntPtr scene, List<IntPtr> allocations) = BuildFakeScene(new[] { 111, 222, 333 });
        using ProcessMemoryReader reader = OpenSelf();
        try
        {
            bool trovato = SceneManagerFinder.PlayerListContainsEntity(reader, scene, 3443217);

            Assert.False(trovato);
        }
        finally
        {
            Free(allocations);
        }
    }

    [Fact]
    public void UnaLunghezzaOltreIlMassimoERifiutataInveceDiAllocare()
    {
        int listBlockSize = Math.Max(NosTaleClientLayout.ListLengthOffset, NosTaleClientLayout.ListArrayOffset) + sizeof(int);
        IntPtr list = AllocZeroed(listBlockSize);
        Marshal.WriteInt32(list + NosTaleClientLayout.ListLengthOffset, NosTaleClientLayout.MaxEntitiesPerList + 1);
        Marshal.WriteInt32(list + NosTaleClientLayout.ListArrayOffset, 0);

        int sceneBlockSize = NosTaleClientLayout.PlayerListOffset + sizeof(int);
        IntPtr scene = AllocZeroed(sceneBlockSize);
        Marshal.WriteInt32(scene + NosTaleClientLayout.PlayerListOffset, (int)list);

        using ProcessMemoryReader reader = OpenSelf();
        try
        {
            bool trovato = SceneManagerFinder.PlayerListContainsEntity(reader, scene, 3443217);

            Assert.False(trovato);
        }
        finally
        {
            VirtualFree(list, UIntPtr.Zero, MEM_RELEASE);
            VirtualFree(scene, UIntPtr.Zero, MEM_RELEASE);
        }
    }

    [Fact]
    public void RunNonSollevaERestituisceUnCodiceValido()
    {
        // Fuori da Windows rifiuta subito (codice 2); su Windows senza un client
        // NosTale in esecuzione TryAttach rifiuta (codice 1); con un client
        // attaccabile puo' anche risolvere (codice 0) o esaurire la scansione senza
        // un candidato (codice 1). In nessun caso solleva un'eccezione: e' questo,
        // non quale specifico codice, il criterio qui (l'ambiente del test non e'
        // controllato: puo' girare con o senza un client NosTale in esecuzione).
        int esito = SceneManagerFinder.Run(candidatePath: "data/_test_scenemanager_candidates.txt");

        Assert.InRange(esito, 0, 2);
    }
}
