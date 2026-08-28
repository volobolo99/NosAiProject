from nosai.runtime.context_slimming import VRAMContextSlimmer
from nosai.runtime.hardware_watchdog import HardwareTelemetry, NOSAIHardwareWatchdog

class Probe:
    def __init__(self, telemetry): self.telemetry = telemetry
    def read(self): return self.telemetry

def test_context_slimming_is_bounded_and_structured():
    s = VRAMContextSlimmer(max_errors=3)
    result = s.comprimi_storico("f", [{"errore": "Traceback\nValueError: bad"}] * 5)
    assert len(result) == 3
    assert {"signature", "msg", "exception_type", "source"} <= result[-1].keys()

def test_watchdog_trips_on_thermal_limit():
    probe = Probe(HardwareTelemetry(gpu_temperature_c=81.0))
    wd = NOSAIHardwareWatchdog(max_temp=80.0, cooling_seconds=0, probe=probe)
    decision = wd.check()
    assert decision.allowed is False
    assert decision.reason == "thermal_limit"
    assert wd.cooling is True
