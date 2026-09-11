using System;
using System.Buffers.Binary;
using NosAi.Runtime.Gate1;

namespace NosAi.Runtime.Testing
{
    public static partial class WireProtocolFuzzTestRunner
    {
        public static bool RunAll()
        {
            Console.WriteLine("=== Wire header fuzz (C-402) ===");
            bool allPassed = true;
            allPassed &= Run("Ogni lunghezza troncata sotto HeaderSize e' rifiutata come incomplete_header", TestEveryTruncatedLengthIsRefusedAsIncompleteHeader);
            allPassed &= Run("Un header valido fa round-trip per ogni valore di byte di MessageType", TestRoundTripsForEveryMessageTypeByteValue);
            allPassed &= Run("Un header valido fa round-trip per i valori limite di PayloadLength e SequenceNumber", TestRoundTripsForBoundaryPayloadAndSequenceValues);
            allPassed &= Run("Un magic number sbagliato e' sempre rifiutato", TestWrongMagicIsAlwaysRefused);
            allPassed &= Run("Ogni versione diversa da CurrentVersion e' sempre rifiutata", TestWrongVersionIsAlwaysRefused);
            allPassed &= Run("50000 buffer generati con seed fisso non fanno mai crashare il parser ne' violano il contratto", TestSeededMutationFuzzNeverViolatesTheContract);
            Console.WriteLine(allPassed ? "=== Wire header fuzz passed: 50000 iterazioni seed=1337, nessun crash, nessuna violazione. ===" : "=== Wire header fuzz FAILED. Vedi le righe FAIL sopra. ===");
            return allPassed;
        }

        private static bool Run(string name, Func<bool> check)
        {
            try { return Report(name, check(), null); }
            catch (Exception ex) { return Report(name, false, ex.GetType().Name + ": " + ex.Message); }
        }

        private static bool Report(string name, bool passed, string? error)
        {
            Console.WriteLine("[" + (passed ? "PASS" : "FAIL") + "] " + name + (error is null ? "" : " [" + error + "]"));
            return passed;
        }

        private static bool HeaderContractHolds(ReadOnlySpan<byte> source, out string? violation)
        {
            violation = null;
            if (!WireHeader.TryRead(source, out var header, out var error))
            {
                if (header.Equals(default(WireHeader)) && !string.IsNullOrEmpty(error))
                    return true;
                else
                {
                    if (!header.Equals(default(WireHeader)))
                        violation = "header non e' default nonostante il rifiuto";
                    else if (string.IsNullOrEmpty(error))
                        violation = "error nullo o vuoto nonostante il rifiuto";
                    else
                        violation = "errore sconosciuto";
                    return false;
                }
            }
            if (error != null) { violation = "error is not null"; return false; }
            if (source.Length < WireHeader.HeaderSize) { violation = "source.Length < HeaderSize"; return false; }
            if (BinaryPrimitives.ReadUInt32BigEndian(source[0..4]) != WireHeader.ExpectedMagic) { violation = "magic mismatch"; return false; }
            if (source[4] != WireHeader.CurrentVersion) { violation = "version mismatch"; return false; }
            if (header.MessageType != (WireMessageType)source[5]) { violation = "message type mismatch"; return false; }
            if (header.PayloadLength != BinaryPrimitives.ReadUInt16BigEndian(source[6..8])) { violation = "payload length mismatch"; return false; }
            if (header.SequenceNumber != BinaryPrimitives.ReadUInt32BigEndian(source[8..12])) { violation = "sequence number mismatch"; return false; }
            return true;
        }

        private static bool TestEveryTruncatedLengthIsRefusedAsIncompleteHeader()
        {
            for (int length = 0; length <= WireHeader.HeaderSize - 1; length++)
            {
                byte[] buffer = new byte[length];
                if (WireHeader.TryRead(buffer, out var header, out var error) ||
                    error != "incomplete_header" ||
                    !header.Equals(default(WireHeader)))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TestRoundTripsForEveryMessageTypeByteValue()
        {
            for (int b = 0; b <= 255; b++)
            {
                var header = new WireHeader((WireMessageType)b, 0x1234, 0xCAFEBABEu);
                byte[] buffer = new byte[WireHeader.HeaderSize];
                header.WriteTo(buffer);

                if (!HeaderContractHolds(buffer, out var violation) ||
                    !WireHeader.TryRead(buffer, out var letto, out var errore) ||
                    errore != null ||
                    letto.MessageType != (WireMessageType)b ||
                    letto.PayloadLength != 0x1234 ||
                    letto.SequenceNumber != 0xCAFEBABEu)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TestRoundTripsForBoundaryPayloadAndSequenceValues()
        {
            ushort[] payloadValues = { 0, 1, WireHeader.MaxPayloadLength };
            uint[] sequenceValues = { 0u, 1u, uint.MaxValue };

            foreach (ushort payload in payloadValues)
            {
                foreach (uint sequence in sequenceValues)
                {
                    var header = new WireHeader(WireMessageType.Heartbeat, payload, sequence);
                    byte[] buffer = new byte[WireHeader.HeaderSize];
                    header.WriteTo(buffer);

                    if (!HeaderContractHolds(buffer, out var violation) ||
                        !WireHeader.TryRead(buffer, out var letto, out var errore) ||
                        errore != null ||
                        letto.MessageType != WireMessageType.Heartbeat ||
                        letto.PayloadLength != payload ||
                        letto.SequenceNumber != sequence)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool TestWrongMagicIsAlwaysRefused()
        {
            var reference = new WireHeader(WireMessageType.Heartbeat, 1, 1u);
            byte[] referenceBuffer = new byte[WireHeader.HeaderSize];
            reference.WriteTo(referenceBuffer);

            uint[] wrongMagics = new uint[34];
            wrongMagics[0] = 0x00000000u;
            wrongMagics[1] = 0xFFFFFFFFu;
            for (int i = 0; i < 31; i++)
            {
                wrongMagics[2 + i] = WireHeader.ExpectedMagic ^ (1u << i);
            }

            foreach (uint magic in wrongMagics)
            {
                byte[] buffer = (byte[])referenceBuffer.Clone();
                BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(0, 4), magic);
                if (WireHeader.TryRead(buffer, out var header, out var error) ||
                    error != "invalid_magic" ||
                    !header.Equals(default(WireHeader)))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TestWrongVersionIsAlwaysRefused()
        {
            var reference = new WireHeader(WireMessageType.Heartbeat, 1, 1u);
            byte[] referenceBuffer = new byte[WireHeader.HeaderSize];
            reference.WriteTo(referenceBuffer);

            for (int v = 0; v <= 255; v++)
            {
                if (v == WireHeader.CurrentVersion) continue;
                byte[] buffer = (byte[])referenceBuffer.Clone();
                buffer[4] = (byte)v;
                if (WireHeader.TryRead(buffer, out var header, out var error) ||
                    error != "unsupported_version" ||
                    !header.Equals(default(WireHeader)))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TestSeededMutationFuzzNeverViolatesTheContract()
        {
            var rng = new Random(1337);
            bool failed = false;
            byte[] buffer = Array.Empty<byte>();

            for (int i = 0; i < 50000; i++)
            {
                try
                {
                    if (rng.Next(2) == 0)
                    {
                        var header = new WireHeader(
                            (WireMessageType)(byte)rng.Next(256),
                            (ushort)rng.Next(ushort.MaxValue + 1),
                            (uint)rng.NextInt64(uint.MaxValue + 1L));
                        int extraLength = rng.Next(0, 33);
                        buffer = new byte[WireHeader.HeaderSize + extraLength];
                        header.WriteTo(buffer);
                        rng.NextBytes(buffer.AsSpan(WireHeader.HeaderSize));
                    }
                    else
                    {
                        buffer = new byte[rng.Next(0, 65)];
                        rng.NextBytes(buffer);
                    }

                    if (rng.Next(2) == 0)
                    {
                        int mutations = rng.Next(0, 4);
                        for (int m = 0; m < mutations; m++)
                        {
                            if (buffer.Length == 0) break;
                            int pos = rng.Next(buffer.Length);
                            buffer[pos] = (byte)rng.Next(256);
                        }
                    }

                    if (!HeaderContractHolds(buffer, out var violation))
                    {
                        Console.WriteLine($"Fuzz iteration {i}: Violation: {violation}, Buffer: {Convert.ToHexString(buffer)}");
                        failed = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Fuzz iteration {i}: Exception at length {buffer.Length}, Buffer: {Convert.ToHexString(buffer)}, Exception: {ex.Message}");
                    failed = true;
                    break;
                }
            }

            return !failed;
        }
    }
}
