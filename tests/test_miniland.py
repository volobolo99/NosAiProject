from nosai.miniland.automation import FishingAutomation, FishingResult, MinilandAction, MinilandCommand


class FakeAdapter:
    def __init__(self):
        self.commands = []

    def send_command(self, command):
        self.commands.append(command)
        return True

    def read_result(self):
        return FishingResult(True, catches=1)


def test_fishing_cycle_uses_adapter_only():
    adapter = FakeAdapter()
    bot = FishingAutomation(adapter)
    results = bot.run_cycle(2, 500)
    assert len(results) == 2
    assert [c.action for c in adapter.commands] == [MinilandAction.FISH, MinilandAction.FISH, MinilandAction.STOP]


def test_command_requires_started_runtime():
    adapter = FakeAdapter()
    bot = FishingAutomation(adapter)
    try:
        bot.execute(MinilandCommand(MinilandAction.FISH))
    except RuntimeError:
        pass
    else:
        raise AssertionError("il comando deve richiedere un runtime avviato")
