from nosai.core.contracts import ActionType, CandidateAction, Decision, Goal, Position, WorldState
from nosai.runtime import (
    AgentRuntime, ProviderCapabilities, ProviderRegistry, ProviderRouter,
    ResourceManager, ResourceSnapshot, RoutingPolicy, RuntimeContext,
)


class StubProvider:
    def __init__(self, provider_id: str, local: bool):
        self.capabilities = ProviderCapabilities(provider_id, local)

    def decide(self, world_state, goal):
        return Decision(CandidateAction(ActionType.NOOP), 1.0, "stub")


def world():
    return WorldState(hp=100, mp=50, position=Position(0, 0))


def test_local_first_for_local_only_context():
    registry = ProviderRegistry()
    registry.register(StubProvider("local", True))
    registry.register(StubProvider("cloud", False))
    router = ProviderRouter(registry)
    candidate = router.select(RuntimeContext("s1"), ResourceSnapshot(vram_available_mb=4096))
    assert candidate.provider.capabilities.provider_id == "local"


def test_cloud_is_rejected_for_local_only_context():
    registry = ProviderRegistry()
    registry.register(StubProvider("cloud", False))
    router = ProviderRouter(registry)
    try:
        router.select(RuntimeContext("s1", requires_local=True), ResourceSnapshot())
    except RuntimeError as exc:
        assert str(exc) == "no_provider_satisfies_policy"
    else:
        raise AssertionError("cloud provider must not bypass local-only policy")


def test_runtime_never_executes_provider_output():
    registry = ProviderRegistry()
    registry.register(StubProvider("local", True))
    runtime = AgentRuntime(
        ProviderRouter(registry),
        resources=ResourceManager(lambda: ResourceSnapshot(vram_available_mb=4096)),
    )
    session = runtime.sessions.create("test")
    result = runtime.decide(world(), Goal("observe"), RuntimeContext(session.session_id))
    assert result.guard_allowed is True
    assert result.safety_allowed is True
    assert runtime.memory.recent(session.session_id)
