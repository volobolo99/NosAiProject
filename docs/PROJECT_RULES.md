# NosAi Project Rules

## Primary bring-up rule
The first milestone is **Play AI + Play Guard minimal bring-up**. The system must first reach the smallest reliable state in which:

1. Play AI can start on the PC.
2. Play Guard can start on the PC.
3. Guard AI can start on the phone.
4. PC Guard and phone Guard AI establish a local authenticated session.
5. Both sides exchange heartbeat and capability/status messages.
6. Disconnect/reconnect is deterministic and safe.
7. The complete minimal path is testable without the game client.

Only after this baseline is proven should feature development proceed to richer vision, memory, LLM optimization, game adapters, and advanced automation.

## Architecture rule
- Keep the new repository as the clean canonical implementation.
- Reuse the old repository only as a source for selected contracts, algorithms, tests, or documentation after review.
- Keep Play AI, Play Guard, and Guard AI separated by explicit contracts and transport interfaces.
- Default to localhost/LAN-only communication during bring-up.
- Every decision that can affect execution must pass through the Safety Gate.
- Simulation must remain available so the stack can be tested without a game client.
- CI must validate the minimal bring-up path before feature work is accepted.

## External implementation boundary
Some future integrations may require capabilities that are outside this implementation scope or require separate specialist work (for example game-client-specific anti-cheat interaction, client bypasses, or packet/network manipulation). These capabilities are **not to be silently deleted from the architecture or roadmap**. They must instead remain represented as explicit integration interfaces/placeholders and be marked `EXTERNAL_IMPLEMENTATION_REQUIRED` until a separately supplied implementation can be reviewed and integrated safely.

No such integration is required for the minimal bring-up milestone.
