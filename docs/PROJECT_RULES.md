# NosAi Project Rules

## Primary bring-up rule
The first milestone is **Play AI + Play Guard + Guard AI minimal bring-up**. The system must first reach the smallest reliable state in which:

1. Play AI can start on the PC.
2. Play Guard can start on the PC.
3. Guard AI can start on the phone.
4. PC Guard and phone Guard AI establish a local authenticated session.
5. Both sides exchange heartbeat and capability/status messages.
6. Disconnect/reconnect is deterministic and safe.
7. The complete minimal path is testable without the game client.

Only after this baseline is proven should richer game perception, memory, LLM optimization, game adapters, and advanced automation proceed.

## Product role rule
NosAi is designed as an automated player whose objective is to advance a character toward explicit goals efficiently while minimizing unnecessary time, effort, resource waste and avoidable risk.

- **Play AI** replaces the human at the execution layer: it receives approved actions/plans, operates through available game-interface adapters, and reports observations/results.
- **Guard AI** is the strategic protection and evaluation layer: it analyzes risk, uncertainty, constraints and proposed plans/actions and can reject, constrain, downgrade or request reconsideration.
- **Progression Engine** is the planning layer: it selects and evaluates progression paths using state, predictions, time, resources, risk and validated strategy knowledge.
- **Knowledge Base** preserves validated strategies so knowledge can be transferred to compatible future characters instead of relearned from zero.

No role is allowed to silently assume another role's privileges.

## Architecture rule
- Keep the new repository as the clean canonical implementation.
- Reuse the old repository only as a source for selected contracts, algorithms, tests, or documentation after review.
- Keep Play AI, Play Guard, Guard AI, Progression Engine and Knowledge Base separated by explicit contracts and transport interfaces.
- Default to localhost/LAN-only communication during bring-up.
- Every decision that can affect execution must pass through the Safety Gate.
- Simulation must remain available so the stack can be tested without a game client.
- CI must validate the minimal bring-up path before feature work is accepted.
- Do not optimize hardware or the local LLM before deterministic functional behavior is proven.

## Strategy and mastery rule
Strategies are persistent, evidence-backed records. They must be scoped as needed by character category/class, level range, build/equipment, content/activity, objective and relevant context.

Strategies move through an evidence lifecycle such as experimental → validated → preferred, with regression/demotion supported. A single run must never overwrite validated knowledge.

NosAi must expose an evidence-based **Mastery Score** from 0–100, with contextual breakdowns where possible. The score describes proximity to the best validated/reference behavior for a context; it is not an unsupported claim of absolute perfection.

## External implementation boundary
Some future integrations may require capabilities that are outside this implementation scope or require separate specialist work (for example game-client-specific anti-cheat interaction, client bypasses, or packet/network manipulation). These capabilities are **not to be silently deleted from the architecture or roadmap**. They must instead remain represented as explicit integration interfaces/placeholders and be marked `EXTERNAL_IMPLEMENTATION_REQUIRED` until a separately supplied implementation can be reviewed and integrated safely.

No such integration is required for the minimal bring-up milestone.
