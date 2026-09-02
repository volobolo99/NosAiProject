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

## The binaries are deliberately not committed

WinDivert is dual-licensed (LGPLv3 / GPLv2), and `WinDivert64.sys` is a signed kernel
driver. Vendoring either into this repository is a licensing and distribution decision
that belongs to whoever publishes it, not to a build convenience. Download the release
that matches this machine's architecture and put the two files here.

Capture needs an **elevated** console regardless: the driver is opened, and the client
declares `requireAdministrator`.
