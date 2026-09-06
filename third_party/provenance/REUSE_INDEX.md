# Third-Party Reuse Index

| Local file | Upstream | Upstream path | Blob SHA | License | Status |
|---|---|---|---|---|---|
| `sources/opennos/reference/LoginPacketHandler.cs` | OpenNos/OpenNos | `OpenNos.Handler/LoginPacketHandler.cs` | `5e6b7c66cc721f12fb55721106f0fbb9ffd9995e` | GPL v2+ | Reference copy; reusable subject to GPL terms |
| `sources/noscore/reference/WalkPacketHandler.cs` | NosCoreIO/NosCore | `src/NosCore.PacketHandlers/Movement/WalkPacketHandler.cs` | `93d429815fb3fe9e964abf6e5ea61a5185f6355d` | MIT | Reference copy; reusable with notice preservation |
| `sources/chickenapi/reference/BasicEventPipelineAsync.cs` | BlowaXD/ChickenAPI | `src/ChickenAPI.Core/Events/BasicEventPipelineAsync.cs` | `090c4ebe343ff1bc89d530977ae55acf156cebf6` | GPL v3 | Reference copy; reusable subject to GPL terms |
| `sources/saltyemu/reference/WorldServer.cs` | BlowaXD/SaltyEmu | `src/World/WorldServer.cs` | `c39557744c8f857416c8db10d4ef7a3e8d354372` | Verify upstream LICENSE before redistribution | Reference copy; architecture study/reuse |
| `sources/luxkun/ReGoap/reference/IReGoapAction.cs` | luxkun/ReGoap | `ReGoap/Core/IReGoapAction.cs` | `cd0f12f20b30c61d00f49739187b0938bdbfdba6` | Apache-2.0 | Reference copy; planner/action contract |
| `sources/luxkun/ReGoap/reference/IReGoapAgent.cs` | luxkun/ReGoap | `ReGoap/Core/IReGoapAgent.cs` | `667203a1fda4bcf83093c6aa16436a3c7da04685` | Apache-2.0 | Reference copy; agent abstraction |
| `sources/luxkun/ReGoap/reference/IReGoapGoal.cs` | luxkun/ReGoap | `ReGoap/Core/IReGoapGoal.cs` | `005de38fbdb603b27b419f2044f33bc1aaad3e27` | Apache-2.0 | Reference copy; goal abstraction |
| `sources/luxkun/ReGoap/reference/IReGoapMemory.cs` | luxkun/ReGoap | `ReGoap/Core/IReGoapMemory.cs` | `931ccf486fdf60248c9788ed5ead870c38c3be92` | Apache-2.0 | Reference copy; planner memory abstraction |
| `sources/luxkun/ReGoap/reference/IReGoapSensor.cs` | luxkun/ReGoap | `ReGoap/Core/IReGoapSensor.cs` | `89f4a83685da23b687826d9aa9e79750dc6d1796` | Apache-2.0 | Reference copy; sensor abstraction |
| `sources/luxkun/ReGoap/reference/ReGoapCondition.cs` | luxkun/ReGoap | `ReGoap/Core/ReGoapCondition.cs` | `e2cca83565b10ffe0bf3f43ac963c766b253874a` | Apache-2.0 | Adapted reference excerpt; preserve upstream attribution |
| `sources/microsoft/Memora/reference/README.md` | microsoft/Memora | `README.md` | `64fce15fa0101c6f8054f9683904be72b3233013` | MIT | Research synopsis; not source code |
| `sources/joslat/agent-memory-dotnet/reference/README.md` | joslat/agent-memory-dotnet | `README.md` | `7821d97d7ce3b3c37c55537e557c932e7aa0dd71` | MIT | Research synopsis; not source code |
| `sources/ikpil/DotRecast/reference/README.md` | ikpil/DotRecast | `README.md` | `8cc246e6ae4fa26ecc1e5df28e43c30d9c45259e` | ZLib ||
| `taletool/taletool.exe` | imxeno/taletool | `crates/taletool-cli` (binario compilato, non un file copiato) | `5a7078e54472f0f1141955300a57b50ac8329cda` (commit upstream) | AGPL-3.0-or-later | Vendorizzato e integrato come funzionalità interna, eseguito come processo esterno da `TaletoolInvoker` (mai collegato al runtime); dettagli di build in `third_party/taletool/VERSION` |


