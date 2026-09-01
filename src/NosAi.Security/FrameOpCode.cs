namespace NosAi.Security;

/// <summary>
/// Closed set of application opcodes carried inside a Gate 1
/// <see cref="NosFrameHeader"/> (docs/ROADMAP_ESECUTIVA.md S:2.3). Values
/// outside this set are discarded as <c>FaultCode.FrameInvalid</c> before any
/// payload is interpreted. Handshake messages are Noise records, not opcodes.
/// </summary>
public enum FrameOpCode : byte
{
    Heartbeat = 0x01,
    PresentCapability = 0x02,
    CapabilityDecision = 0x03,
    Disconnect = 0x04,
    Rekey = 0x05
}
