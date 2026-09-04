# Third-Party Reuse Index

| Local file | Upstream | Upstream path | Blob SHA | License | Status |
|---|---|---|---|---|---|
| `sources/opennos/reference/LoginPacketHandler.cs` | OpenNos/OpenNos | `OpenNos.Handler/LoginPacketHandler.cs` | `5e6b7c66cc721f12fb55721106f0fbb9ffd9995e` | GPL v2+ | Reference copy; reusable subject to GPL terms |
| `sources/noscore/reference/WalkPacketHandler.cs` | NosCoreIO/NosCore | `src/NosCore.PacketHandlers/Movement/WalkPacketHandler.cs` | `93d429815fb3fe9e964abf6e5ea61a5185f6355d` | MIT | Reference copy; reusable with notice preservation |
| `sources/chickenapi/reference/BasicEventPipelineAsync.cs` | BlowaXD/ChickenAPI | `src/ChickenAPI.Core/Events/BasicEventPipelineAsync.cs` | `090c4ebe343ff1bc89d530977ae55acf156cebf6` | GPL v3 | Reference copy; reusable subject to GPL terms |
| `sources/saltyemu/reference/WorldServer.cs` | BlowaXD/SaltyEmu | `src/World/WorldServer.cs` | `c39557744c8f857416c8db10d4ef7a3e8d354372` | Verify upstream LICENSE before redistribution | Reference copy; architecture study/reuse |

## Agent rule

When a task concerns packet handling, event pipelines, movement, plugin architecture, or world-server composition, inspect this index first and then the relevant local source file. Do not spend tokens searching GitHub for the same implementation unless the local source is insufficient.

## Non-deletion rule

Third-party source files are preserved intentionally. Never delete them automatically because of license concerns. If a licensing ambiguity is discovered, mark the entry `REVIEW_REQUIRED` and report it to the human before deletion or replacement.
