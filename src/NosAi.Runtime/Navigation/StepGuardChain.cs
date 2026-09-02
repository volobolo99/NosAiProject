// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Navigation — The guards for one step, and the order they are asked in (C-P4)
// ============================================================================
//
// docs/CONTROLLO_PERSONAGGIO_ROADMAP.md P4: "composizione finale delle guardie e
// ordine di corto circuito". Every guard here already existed somewhere; what did
// not exist was one place that says which of them apply to an act, in which
// order, and what a refusal is called.

using System.Globalization;
using NosAi.Navigation.Pathfinding;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Safety;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Navigation;

/// <summary>The guards one step passes, in the order they are asked.</summary>
/// <remarks>
/// The numbering is the order and is load-bearing: see <see cref="StepGuardChain"/>
/// for why each one sits where it does.
/// </remarks>
public enum StepGuard : byte
{
    /// <summary>The request is a step at all: one cell, and a cell that exists.</summary>
    Shape = 0,

    /// <summary>The client's own geometry permits standing where we are and going where we ask.</summary>
    Geometry = 1,

    /// <summary>This runtime can drive this client session.</summary>
    Authority = 2,

    /// <summary>The operator has armed live input.</summary>
    Policy = 3,

    /// <summary>The world was observed recently enough, and the destination is clear.</summary>
    Occupancy = 4,

    /// <summary>The destination cell becomes a pixel of the client window.</summary>
    Projection = 5
}

/// <summary>What happened to one guard.</summary>
public enum StepGuardState : byte
{
    /// <summary>Asked, and it allowed the act.</summary>
    Passed = 0,

    /// <summary>Asked, and it refused. The chain stops here.</summary>
    Refused = 1,

    /// <summary>Never asked, because something upstream refused first.</summary>
    /// <remarks>
    /// Reported rather than omitted. A report that simply stopped would leave the
    /// reader to infer whether the remaining guards passed, were skipped, or do not
    /// exist, and "not asked" is a different fact from "passed".
    /// </remarks>
    NotEvaluated = 2
}

/// <summary>One guard's outcome.</summary>
public readonly record struct StepGuardOutcome(StepGuard Guard, StepGuardState State, string? RefusalReason);

/// <summary>What the act was asked to do, and what it is allowed to know.</summary>
/// <param name="From">The square the character is on, as observed.</param>
/// <param name="To">The adjacent square the act would move onto.</param>
/// <param name="Grid">The client's static geometry for the current map.</param>
/// <param name="View">What has been seen moving, and when the seeing was refreshed.</param>
/// <param name="NowUtc">The instant ages are measured from.</param>
public readonly record struct StepRequest(
    MapPoint From,
    MapPoint To,
    MapGrid Grid,
    OccupancyView View,
    DateTime NowUtc);

/// <summary>
/// Whether the step may be emitted, where it would land on screen, and what every
/// guard said.
/// </summary>
/// <param name="ScreenX">Meaningful only when <paramref name="IsAuthorized"/>.</param>
/// <param name="ScreenY">Meaningful only when <paramref name="IsAuthorized"/>.</param>
/// <param name="Scale">
/// The geometry the coordinate was computed under, carried to the commit point as its
/// fifth condition. Taken from the projection that produced the pixel, never re-read
/// from the live window: comparing the live scale against itself is decoration.
/// </param>
public sealed record StepAuthorization(
    bool IsAuthorized,
    StepGuard? RefusedAt,
    string? RefusalReason,
    int ScreenX,
    int ScreenY,
    GeometryShape Scale,
    IReadOnlyList<StepGuardOutcome> Outcomes);

/// <summary>
/// The guards for one step, composed once, in one order.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a chain exists at all.</b> Each of these already lived somewhere — the grid
/// in <see cref="MapGrid"/>, authority in <see cref="LowLevel.SessionActuationAuthority"/>,
/// the policy in <see cref="RuntimeSafetyController"/>, the projection in Gate 3. What
/// was missing is the statement of which ones an act must pass and in what order, and
/// a missing order is not a neutral gap: it becomes whatever order the first caller
/// happened to write, differently in each caller.
/// </para>
/// <para>
/// <b>The order, and the reason for it.</b> Two rules decide it, and they agree.
/// </para>
/// <list type="number">
/// <item>
/// <b>Name the fact furthest upstream.</b> <see cref="CommitPointValidator"/> orders
/// its five conditions the same way and for the same reason: when several things are
/// wrong, the useful sentence is the most structural one. Being told the destination
/// is occupied is no help when the runtime could not have driven the client anyway.
/// </item>
/// <item>
/// <b>Read the volatile things last.</b> Everything before <see cref="StepGuard.Occupancy"/>
/// is a fact that cannot change while the chain runs: the shape of the request, a file
/// the client shipped, a latched verdict, a switch the operator holds. Occupancy is the
/// one that can differ between two calls a second apart, so it is read as late as
/// possible — the same argument that puts the commit point after everything.
/// </item>
/// </list>
/// <para>
/// <see cref="StepGuard.Projection"/> comes last of all because it is the only one that
/// <i>produces</i> something rather than permitting it, and computing a pixel for a step
/// already refused is work spent on an act that will not happen.
/// </para>
/// <para>
/// <b>What is deliberately not here.</b> The commit point. It is not a guard in this
/// chain and must not become one: it belongs to <see cref="LowLevel.GatedInputBackend"/>,
/// runs inside the gate in the instant before the irreversible step, and re-reads a
/// world this chain has already finished looking at. A copy of it here would be a
/// second answer that could disagree with the one that counts.
/// </para>
/// <para>
/// <b>Short circuit.</b> The first refusal ends the chain. Everything after it is
/// reported <see cref="StepGuardState.NotEvaluated"/> rather than dropped, so the
/// operator's report shows the whole ladder and where it stopped.
/// </para>
/// </remarks>
public sealed class StepGuardChain
{
    /// <summary>Reported when the destination is the square the character is already on.</summary>
    public const string ZeroLengthReason = "step_zero_length";

    /// <summary>Reported when the destination is more than one cell away.</summary>
    public const string NotAdjacentPrefix = "step_not_adjacent";

    /// <summary>Reported when no grid is loaded for the current map.</summary>
    public const string GridNotLoadedReason = "step_grid_not_loaded";

    /// <summary>Reported when the character's own square is outside the grid.</summary>
    public const string OriginOffGridPrefix = "step_origin_off_grid";

    /// <summary>Reported when the grid says the character is standing somewhere it cannot stand.</summary>
    public const string OriginNotWalkablePrefix = "step_origin_not_walkable";

    /// <summary>Reported when the destination is outside the grid.</summary>
    public const string DestinationOffGridPrefix = "step_destination_off_grid";

    /// <summary>Reported when the client's geometry forbids the destination.</summary>
    public const string DestinationBlockedPrefix = "step_destination_blocked";

    /// <summary>Reported when live input is not armed.</summary>
    public const string InputNotArmedReason = "step_live_input_not_armed";

    /// <summary>Reported when no session authority was supplied to consult.</summary>
    public const string AuthorityUnknownReason = "step_session_authority_unknown";

    private static readonly StepGuard[] Order =
    {
        StepGuard.Shape,
        StepGuard.Geometry,
        StepGuard.Authority,
        StepGuard.Policy,
        StepGuard.Occupancy,
        StepGuard.Projection
    };

    private readonly Func<string?> _sessionAuthority;
    private readonly Func<RuntimeSafetyPolicy> _policySource;
    private readonly IScreenProjection _projection;
    private readonly TimeSpan? _maxViewAge;
    private readonly TimeSpan? _maxSightingAge;

    /// <param name="sessionAuthority">
    /// Why this session cannot be driven, or null when it can
    /// (<see cref="LowLevel.SessionActuationAuthority.CurrentRefusal"/>). A pure read:
    /// the chain must be askable without emitting anything, so it never triggers the
    /// authority probe. Null is not accepted — a chain with nothing to ask would pass
    /// the guard by having no opinion, which is the shape of a bypass.
    /// </param>
    /// <param name="policySource">Read per call, so a switch flipped now is obeyed now.</param>
    /// <param name="projection">Map cell to client pixel. The uncalibrated one refuses, by name.</param>
    public StepGuardChain(
        Func<string?> sessionAuthority,
        Func<RuntimeSafetyPolicy> policySource,
        IScreenProjection projection,
        TimeSpan? maxViewAge = null,
        TimeSpan? maxSightingAge = null)
    {
        _sessionAuthority = sessionAuthority ?? throw new ArgumentNullException(nameof(sessionAuthority));
        _policySource = policySource ?? throw new ArgumentNullException(nameof(policySource));
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        _maxViewAge = maxViewAge;
        _maxSightingAge = maxSightingAge;
    }

    /// <summary>Asks every guard, in order, and stops at the first refusal.</summary>
    public StepAuthorization Authorize(in StepRequest request)
    {
        var outcomes = new List<StepGuardOutcome>(Order.Length);
        var screenX = 0;
        var screenY = 0;
        GeometryShape scale = default;

        foreach (StepGuard guard in Order)
        {
            string? refusal = guard switch
            {
                StepGuard.Shape => CheckShape(in request),
                StepGuard.Geometry => CheckGeometry(in request),
                StepGuard.Authority => CheckAuthority(),
                StepGuard.Policy => CheckPolicy(),
                StepGuard.Occupancy => CheckOccupancy(in request),
                StepGuard.Projection => CheckProjection(in request, out screenX, out screenY, out scale),
                _ => $"step_guard_not_implemented:{guard}"
            };

            if (refusal is not null)
            {
                outcomes.Add(new StepGuardOutcome(guard, StepGuardState.Refused, refusal));
                AddNotEvaluatedAfter(guard, outcomes);
                return new StepAuthorization(false, guard, refusal, 0, 0, default, outcomes);
            }

            outcomes.Add(new StepGuardOutcome(guard, StepGuardState.Passed, null));
        }

        return new StepAuthorization(true, null, null, screenX, screenY, scale, outcomes);
    }

    private static void AddNotEvaluatedAfter(StepGuard refused, List<StepGuardOutcome> outcomes)
    {
        var reached = false;
        foreach (StepGuard guard in Order)
        {
            if (guard == refused)
            {
                reached = true;
                continue;
            }

            if (reached)
                outcomes.Add(new StepGuardOutcome(guard, StepGuardState.NotEvaluated, null));
        }
    }

    /// <summary>
    /// One cell, in one of the eight directions.
    /// </summary>
    /// <remarks>
    /// A request for two cells is not a refusal to be retried, it is a caller that
    /// asked the wrong object: <see cref="StepGuardChain"/> authorises a step, and a
    /// route is a sequence of them, each authorised again (§ 3). Naming it rather than
    /// quietly clamping to the nearest cell keeps the caller's mistake visible.
    /// </remarks>
    private static string? CheckShape(in StepRequest request)
    {
        int dx = request.To.X - request.From.X;
        int dy = request.To.Y - request.From.Y;

        if (dx == 0 && dy == 0)
            return ZeroLengthReason;

        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1)
            return string.Create(CultureInfo.InvariantCulture, $"{NotAdjacentPrefix}:{dx},{dy}");

        return null;
    }

    /// <summary>
    /// The client's own geometry, for both ends of the step.
    /// </summary>
    /// <remarks>
    /// <b>The origin is checked, and that is not redundant.</b> If the grid says the
    /// character is standing on a square it forbids, the grid and the world disagree —
    /// wrong map id, a patched client, a transposed grid — and every conclusion drawn
    /// from that grid is unsound, including the one about the destination. This is P1's
    /// standing-cell proof made into a precondition of acting rather than a one-off
    /// check the operator ran once.
    /// </remarks>
    private static string? CheckGeometry(in StepRequest request)
    {
        // Copied out once: the grid is a property of a readonly struct, so it cannot be
        // handed on by reference, and re-reading it per call would copy it four times.
        MapGrid grid = request.Grid;

        if (!grid.IsLoaded)
            return GridNotLoadedReason;

        if (!grid.Contains(request.From.X, request.From.Y))
            return string.Create(CultureInfo.InvariantCulture,
                $"{OriginOffGridPrefix}:{request.From.X},{request.From.Y}");

        if (StaticGeometryLayer.BaselineFor(in grid, request.From.X, request.From.Y) is not TileType.Walkable)
            return string.Create(CultureInfo.InvariantCulture,
                $"{OriginNotWalkablePrefix}:{request.From.X},{request.From.Y}");

        if (!grid.Contains(request.To.X, request.To.Y))
            return string.Create(CultureInfo.InvariantCulture,
                $"{DestinationOffGridPrefix}:{request.To.X},{request.To.Y}");

        TileType destination = StaticGeometryLayer.BaselineFor(in grid, request.To.X, request.To.Y);
        if (destination is not TileType.Walkable)
            return string.Create(CultureInfo.InvariantCulture,
                $"{DestinationBlockedPrefix}:{request.To.X},{request.To.Y}_{destination}");

        return null;
    }

    private string? CheckAuthority() => _sessionAuthority() ?? null;

    private string? CheckPolicy()
    {
        RuntimeSafetyPolicy policy = _policySource()
            ?? throw new InvalidOperationException("The safety policy source returned null; refusing to authorise a step.");

        return policy.LiveInputEnabled ? null : InputNotArmedReason;
    }

    private string? CheckOccupancy(in StepRequest request)
    {
        OccupancyView view = request.View;
        OccupancyVerdict verdict = OccupancyFreshness.Evaluate(
            request.To, in view, request.NowUtc, _maxViewAge, _maxSightingAge);

        return verdict.IsClear ? null : verdict.RefusalReason;
    }

    private string? CheckProjection(
        in StepRequest request,
        out int screenX,
        out int screenY,
        out GeometryShape scale)
    {
        scale = _projection.Scale;

        if (!_projection.TryProject(request.To.X, request.To.Y, out screenX, out screenY, out string? failure))
            return failure ?? "step_projection_failed";

        // The commit point's fifth condition compares this against the live DPI and
        // refuses an unknown one. Catching it here means the refusal names the
        // calibration rather than surfacing as commit_scale_unknown at the last moment.
        if (!scale.IsKnown)
            return "step_projection_scale_unknown";

        return null;
    }
}
