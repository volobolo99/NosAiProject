# taletool

`taletool.exe` reads the client's own container/asset formats (`.NOS`
archives, `.PKG` patch packages) and classifies or converts what it
finds. `TaletoolInvoker` (`src/NosAi.Runtime/GameData/TaletoolInvoker.cs`)
runs it as a separate OS process and reads its `--json` output; nothing
in `src/` links against it or is compiled together with it.

```
third_party/taletool/taletool.exe
third_party/taletool/LICENSE
third_party/taletool/NOTICE.md
third_party/taletool/VERSION
```

`NosAi.Runtime.csproj` copies `taletool.exe` to the output directory
when present, for every configuration -- the same `Exists(...)`-guarded
rule already used for the WinDivert files, so a machine without it still
builds and the invoker reports its own named failure at run time instead
of a build error.

## The binary is committed, and what that carries

`taletool.exe` is licensed **AGPL-3.0-or-later**. This repository is
public, so committing it is redistribution, and the obligations that
come with it apply here:

- `LICENSE` (the full AGPL-3.0-or-later text) and `NOTICE.md` (the
  upstream project's own third-party notices, covering the vendored
  zlib 1.1.2 it packages) are kept beside the binary and must travel
  with it.
- `VERSION` records the exact upstream commit, the toolchain used to
  build it, and its checksum, so the binary here is reproducible and
  auditable rather than an opaque blob.
- Running it as a separate process from `NosAi.Runtime` does not place
  NosAiProject's own source under AGPL: the two are not linked or
  combined into one program, the same reasoning that already applies to
  invoking any other external tool as a subprocess. Only `taletool.exe`
  itself carries the AGPL obligations above.
- This binary was built locally from upstream source rather than
  downloaded as a published release asset -- see `VERSION` for exactly
  what went into it and why (this environment's network policy could
  reach the upstream Git repository but not its GitHub release assets).

Upgrading means rebuilding from a newer upstream commit and replacing
all four files together, per `VERSION`'s own note.
