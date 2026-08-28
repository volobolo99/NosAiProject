# NosAi Hardware Baseline & First-Run Auto-Setting

**Version:** 1.0 Beta  
**Creator:** Volodymyr Ryzhuk

## 1. Purpose

This document defines the reference hardware profiles for NosAi development and the runtime auto-setting policy for PlayAi on PC and GuardAi on smartphone.

The reference hardware is a development baseline, not a minimum requirement. Production runtime settings must be selected from the actual device capabilities.

## 2. Development reference: PC / PlayAi

| Component | Reference |
|---|---|
| Device | Acer Nitro V 16 AI |
| CPU | AMD Ryzen 7 260 |
| GPU | NVIDIA RTX 5060 |
| Display | 16-inch WUXGA IPS, 180 Hz |
| RAM | 16 GB DDR5 |
| Storage | 1024 GB SSD |
| OS | Windows 11 Home |

## 3. Development reference: Smartphone / GuardAi

| Component | Reference |
|---|---|
| SoC | Snapdragon 865 5G |
| Charging | 65 W SuperDART |
| Display | 90 Hz Super AMOLED fullscreen |
| Main camera | 64 MP quad camera |
| Front camera | 32 MP in-display dual selfie camera |

These values are the project's **reference profile** supplied by the project owner. They must not be silently treated as exact specifications of every deployment device.

## 4. First-run Auto-Setting

On the first launch of each device, the corresponding runtime performs a hardware capability scan and generates an optimized settings profile.

### PlayAi (PC)

Collect, where available:
- CPU model and logical processor count;
- total/available RAM;
- GPU model, VRAM and graphics capability;
- display resolution and refresh rate;
- OS/runtime information;
- storage availability;
- thermal/power state when exposed by the platform.

### GuardAi (smartphone)

Collect, where available:
- SoC/device model;
- CPU core topology and available performance information;
- RAM;
- display resolution and refresh rate;
- battery/thermal/power state;
- camera capability only when a GuardAi feature actually requires it;
- Android/runtime capability information.

## 5. Auto-Setting rules

Auto-Setting must optimize for **stability first, then responsiveness, then resource efficiency**. It must never assume that the reference hardware is present.

The generated profile should contain at least:

- compute tier;
- memory tier;
- graphics tier (PC);
- display tier;
- inference/update budget;
- perception sampling budget;
- telemetry frequency;
- concurrency/worker budget;
- power/thermal policy;
- safe fallback limits.

Auto-Setting must be deterministic for the same normalized hardware profile and policy version.

## 6. Persistence and device identity

Auto-Setting runs automatically **only on first launch for a device**.

After successful calibration, persist:

- normalized hardware fingerprint;
- detected capabilities;
- generated settings;
- Auto-Setting policy version;
- timestamp;
- schema/configuration version.

On subsequent launches, load the saved profile instead of recalibrating when the hardware fingerprint still matches.

If the fingerprint changes materially (for example CPU/GPU/device replacement or a major capability change), invalidate the old profile and perform Auto-Setting again.

The fingerprint must use hardware characteristics, not personally identifying data.

## 7. Manual override

Auto-Setting is the default. A user/operator override may tune non-safety-critical performance settings, but safety limits and GuardAi authorization cannot be overridden by performance configuration.

## 8. Development standard

The reference PC and smartphone profiles above are the standard targets for development, profiling and baseline regression tests until the project owner explicitly changes them.

**Project version remains 1.0 Beta until explicitly changed by the project owner.**
