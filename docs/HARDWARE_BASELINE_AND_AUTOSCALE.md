# NosAi Hardware Baseline & First-Run Auto-Setting

**Version:** 1.0 Beta  
**Creator:** Volodymyr Ryzhuk

## 1. Purpose

This document defines the exact reference hardware profile supplied by the project owner for NosAi development, profiling and regression, plus the runtime Auto-Setting policy for PlayAi on PC and GuardAi on smartphone.

The PC profile is the authoritative development target provided by the owner. It is a baseline for optimization, not a hard deployment requirement. Runtime settings must always be selected from the actual device capabilities.

## 2. Authoritative development reference: PC / PlayAi

| Component | Exact reference specification |
|---|---|
| Device | Acer Nitro V 16 AI |
| CPU | AMD Ryzen 7 260, up to 5.1 GHz turbo, 16 MB cache |
| GPU | NVIDIA GeForce RTX 5060, 8 GB GDDR7, 95 W TDP, 1785 MHz Boost Clock |
| RAM | 16 GB DDR5, 2 × 8 GB, 5600 MT/s, expandable to 32 GB |
| Display | 16-inch WUXGA IPS, 1920 × 1200 px, 180 Hz, 300 nit, 9 ms, matte, NTSC 45% |
| Storage | 1024 GB PCIe NVMe M.2 80 mm SSD, PCIe Mainstream Performance (NVMe), free SSD slot |
| Networking | Intel Wi-Fi 6E, Bluetooth 5.3, LAN |
| Cooling | Dual-fan cooling |
| Audio | Realtek ALC245-CG (HDA)_G4, DTS:X Ultra, Acer TrueHarmony |
| Power adapter | 135 W |
| OS | Windows 11 Home |
| OEM software | Microsoft 365 Trial, NitroSense |
| Keyboard | Backlit; Fn+F11 backlight off; Fn+F12 backlight on |

These values are the **authoritative owner-supplied PC baseline**. They must be used for PlayAi development tuning and baseline regression until explicitly changed by the project owner.

## 3. Development reference: Smartphone / GuardAi

| Component | Reference |
|---|---|
| SoC | Snapdragon 865 5G |
| Charging | 65 W SuperDART |
| Display | 90 Hz Super AMOLED fullscreen |
| Main camera | 64 MP quad camera |
| Front camera | 32 MP in-display dual selfie camera |

These smartphone values remain the project's reference profile. They must not be silently treated as exact specifications of every deployment device.

## 4. Hardware capability model

The runtime normalizes hardware into capability data rather than hard-coding a device model. The normalized profile should include:

- CPU model, architecture, core/thread topology and performance information;
- total/available RAM;
- GPU model, VRAM and graphics capability;
- display resolution and refresh rate;
- storage type, capacity and available space;
- OS/runtime version;
- thermal state and limits when exposed;
- power/battery state where available;
- network capabilities;
- accelerator/inference capabilities when exposed by the platform.

OEM-specific information such as NitroSense is treated as optional telemetry/integration, not as a runtime dependency.

## 5. First-run Auto-Setting

On first launch of each device, the corresponding runtime performs a hardware capability scan and generates an optimized settings profile.

### PlayAi (PC)

The target profile must be able to represent the RTX 5060 8 GB GDDR7 configuration and the 16 GB/5600 MT/s DDR5 baseline. Where platform APIs expose them, collect GPU utilization, VRAM usage, temperature, power and clock state in addition to static hardware identity.

### GuardAi (smartphone)

Collect, where available:
- SoC/device model;
- CPU core topology and performance information;
- RAM and memory pressure;
- display resolution and refresh rate;
- battery/thermal/power state;
- Android/runtime capability information;
- camera capability only when a GuardAi feature actually requires it.

## 6. Auto-Setting rules

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

For PlayAi, the baseline 16 GB RAM / RTX 5060 8 GB profile should be treated as a tuning target, while the 32 GB RAM expansion path should be detected as a higher-memory capability rather than assumed.

Auto-Setting must be deterministic for the same normalized hardware profile and policy version.

## 7. Persistence and device identity

Auto-Setting runs automatically **only on first launch for a device**.

After successful calibration, persist:

- normalized hardware fingerprint;
- detected capabilities;
- generated settings;
- Auto-Setting policy version;
- timestamp;
- schema/configuration version.

On subsequent launches, load the saved profile instead of recalibrating when the hardware fingerprint still matches.

If the fingerprint changes materially, invalidate the old profile and perform Auto-Setting again.

The fingerprint must use hardware characteristics, not personally identifying data.

## 8. Manual override

Auto-Setting is the default. A user/operator override may tune non-safety-critical performance settings, but safety limits and GuardAi authorization cannot be overridden by performance configuration.

## 9. Development standard

The exact PC profile in section 2 is the current PlayAi development and regression standard. The smartphone profile in section 3 is the current GuardAi reference standard.

**Project version remains 1.0 Beta until explicitly changed by the project owner.**
