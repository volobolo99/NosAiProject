# WinDivert

`WinDivertPacketSource` reaches the driver through `[DllImport("WinDivert.dll")]`, and
the Windows loader looks for it **beside the executable**. Two files have to be here:

```
third_party/windivert/WinDivert.dll
third_party/windivert/WinDivert64.sys
```

`NosAi.Runtime.csproj` copies both to the output directory when they are present, for
every configuration. That rule exists because of a real failure: the files had been
dropped into `bin/Debug` by hand, `bin/` is gitignored, and so a `-c Release` build
had no driver at all. The wire observer then refused to open for a reason that looked
like it was about the game.

The `Exists(...)` condition keeps a machine without the driver building. The capture
reports its own named failure at run time, which is the honest place for it — a build
that fails here would say nothing about whether this machine can capture.

## The binaries are committed, and what that carries

WinDivert 2.2.0 is vendored here — `WinDivert.dll`, `WinDivert64.sys`, the upstream
`LICENSE`, and `VERSION` — so that every machine and every CI runner builds a working
capture without downloading anything by hand. Until 3 September 2026 they were
deliberately absent, and the note here said so; that call was reversed by the
repository's owner, which is whose call it is.

What the decision carries, recorded because it is easy to forget once it works:

- WinDivert is **dual-licensed under LGPLv3 or GPLv2**. This repository is public, so
  committing the binaries is redistribution, and the obligations that come with it
  apply here rather than to whoever downloaded a release. The upstream licence text
  sits beside them in `LICENSE` for that reason, and `VERSION` records exactly which
  build is being redistributed.
- `WinDivert64.sys` is a **signed kernel driver** published by a third party. Its
  signature is what makes Windows load it, so it must be kept byte-for-byte as
  released. Never rebuild, patch, strip or re-sign it in place: a modified driver
  does not load, and a driver that does load after being modified is a different
  question entirely.
- Upgrading means replacing all four files together. A `.dll` newer than its `.sys`
  fails at the driver-open call, which reports `driver_signature_rejected` or
  `driver_blocked` — a named refusal that reads as though it were about this machine.

Capture needs an **elevated** console regardless: the driver is opened, and the client
declares `requireAdministrator`.
