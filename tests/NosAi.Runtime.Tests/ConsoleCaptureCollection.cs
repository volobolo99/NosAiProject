using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The collection every test class that redirects <see cref="System.Console"/>
/// belongs to, so no two of them ever run at the same time.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="System.Console.SetOut"/> is process-global. xUnit runs each test
/// class in its own collection <b>in parallel</b> by default, so two classes
/// that each redirect the console race for one shared handle: the second
/// redirect replaces the first, and whichever test asserts on its
/// <see cref="System.IO.StringWriter"/> afterwards reads an empty buffer or
/// the other test's output. The restore in the <c>finally</c> makes it worse
/// rather than better -- it puts back whatever writer that class captured on
/// entry, which by then may be the other class's.
/// </para>
/// <para>
/// This is not theoretical. On 2026-09-07, with the suite at 2314 tests,
/// <c>ClientWindowDpiProbeTests.ProbePrintsThatACalibrationFromAnotherRegimeIsNotUsable</c>
/// and
/// <c>ConsoleRuntimeLoggerCorrelationTests.OutsideAnyScopeTheLoggerPrintsNone</c>
/// failed together in a full run, and both passed 21/21 when run in isolation
/// and again in the next full run. The suite growing made the collision
/// likelier; nothing about those tests changed.
/// </para>
/// <para>
/// Classes in one xUnit collection run sequentially, which is the narrowest
/// fix that removes the race: it serialises the four classes that redirect the
/// console and leaves the other ~2300 tests running in parallel as before.
/// It does <b>not</b> stop an unrelated test elsewhere from writing to the
/// console while one of these holds the redirect -- that would need every
/// console writer in the suite serialised, a far larger change for a failure
/// mode that has never been observed. Any new test that calls
/// <c>Console.SetOut</c> must be added to this collection.
/// </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class ConsoleCaptureCollection
{
    /// <summary>The collection name, referenced by every class that redirects the console.</summary>
    public const string Name = "console-capture";
}
