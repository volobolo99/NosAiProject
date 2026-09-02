using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;

namespace NosAi.ControlPanel;

/// <summary>
/// Reads the map id and standing cell from the attached client. Observation only:
/// the process is opened for reading, the layout is resolved, and nothing is
/// written, posted, or armed.
/// </summary>
internal static class MapWorldReader
{
    /// <summary>Attach succeeded but the map-id read did not name its own refusal.</summary>
    public const string MapIdUnreadable = "map_id_unreadable";

    /// <summary>Attach named no reason; same token <c>ClientMemorySession</c> uses when no PID was given.</summary>
    public const string ClientNotRunning = "client_not_running";

    /// <summary>Attach succeeded but the player object did not name its own refusal.</summary>
    public const string PlayerUnreadable = "player_object_unreadable";

    /// <summary>
    /// One pass over the client. A failure on either field is UNKNOWN with that
    /// field's own reason; a readable map id is not reused as a position.
    /// </summary>
    /// <param name="processId">
    /// The snapshot's client PID when it is known, otherwise zero so the first
    /// client with a window is taken — the same rule <c>ClientMemorySession</c>
    /// already applies.
    /// </param>
    public static MapWorldReading Read(int processId)
    {
        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure, processId))
            return MapWorldReading.Unknown(attachFailure ?? ClientNotRunning);

        using (session)
        {
            ClassifiedValue<int> mapId = session!.TryReadMapId(out int id, out string? mapFailure)
                ? ClassifiedValue<int>.Live(id)
                : ClassifiedValue<int>.Unknown(mapFailure ?? MapIdUnreadable);

            ClassifiedValue<int> cellX;
            ClassifiedValue<int> cellY;
            if (session.TryReadPlayer(out PlayerObjectReading player, out string? posFailure))
            {
                cellX = ClassifiedValue<int>.Live(player.X);
                cellY = ClassifiedValue<int>.Live(player.Y);
            }
            else
            {
                string reason = posFailure ?? PlayerUnreadable;
                cellX = ClassifiedValue<int>.Unknown(reason);
                cellY = ClassifiedValue<int>.Unknown(reason);
            }

            return new MapWorldReading(mapId, cellX, cellY);
        }
    }
}
