// SOURCE: NosCoreIO/NosCore
// UPSTREAM PATH: src/NosCore.PacketHandlers/Movement/WalkPacketHandler.cs
// UPSTREAM REVISION (blob SHA): 93d429815fb3fe9e964abf6e5ea61a5185f6355d
// LICENSE: MIT
// COPYRIGHT: NosCoreIO, 2026
// STATUS: reference copy; NOT wired into NosAi runtime
// IMPORTANT: this is server-side emulator code. It must not be used to bypass
// NosAi's unprivileged client-observation/action boundary.

//  __  _  __    __   ___ __  ___ ___
// |  \\| |/__\\ /' _/ / _//__\\| _ \\ __|
// | | ' | \\/ |`._`.| \\_| \\/ | v / _|
// |_|\\__|\\__/ |___/ \\__/\\__/|_|_\\___|

using NodaTime;
using NosCore.Data.Enumerations.I18N;
using NosCore.Data.Enumerations.Map;
using NosCore.GameObject.Ecs.Extensions;
using NosCore.GameObject.Infastructure;
using NosCore.GameObject.Messaging.Events;
using NosCore.GameObject.Networking.ClientSession;
using NosCore.Networking;
using NosCore.Networking.SessionGroup.ChannelMatcher;
using NosCore.Packets.ClientPackets.Movement;
using NosCore.PathFinder.Interfaces;
using NosCore.Shared.I18N;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Wolverine;

namespace NosCore.PacketHandlers.Movement
{
    public class WalkPacketHandler(IHeuristic distanceCalculator, ILogger<WalkPacketHandler> logger, IClock clock,
            ILogLanguageLocalizer<LogLanguageKey> logLanguage, IMessageBus messageBus)
        : PacketHandler<WalkPacket>, IWorldPacketHandler
    {
        private readonly double _speedDiffAllowed = 1D / 3;

        public override async Task ExecuteAsync(WalkPacket walkPacket, ClientSession session)
        {
            var distance = (int)distanceCalculator.GetDistance((session.Character.PositionX, session.Character.PositionY), (walkPacket.XCoordinate, walkPacket.YCoordinate)) - 1;

            if (session.Character.Speed < walkPacket.Speed)
            {
                logger.LogWarning(
                    "walk dropped: server speed {ServerSpeed} < packet speed {PacketSpeed} for character {VisualId} from ({FromX},{FromY}) to ({ToX},{ToY})",
                    session.Character.Speed, walkPacket.Speed, session.Character.VisualId,
                    session.Character.PositionX, session.Character.PositionY,
                    walkPacket.XCoordinate, walkPacket.YCoordinate);
                return;
            }

            if (distance > session.Character.Speed / 2)
            {
                logger.LogWarning(
                    "walk dropped: distance {Distance} > speed/2 ({HalfSpeed}) for character {VisualId} from ({FromX},{FromY}) to ({ToX},{ToY}) packetSpeed={PacketSpeed}",
                    distance, session.Character.Speed / 2, session.Character.VisualId,
                    session.Character.PositionX, session.Character.PositionY,
                    walkPacket.XCoordinate, walkPacket.YCoordinate, walkPacket.Speed);
                return;
            }

            if ((walkPacket.XCoordinate + walkPacket.YCoordinate) % 3 % 2 != walkPacket.CheckSum)
            {
                await session.DisconnectAsync();
                logger.LogError(logLanguage[LogLanguageKey.WALK_CHECKSUM_INVALID], session.Character.VisualId);
                return;
            }

            var travelTime = 2500 / walkPacket.Speed * distance;
            if (travelTime > 1000 * (_speedDiffAllowed + 1))
            {
                await session.DisconnectAsync();
                logger.LogError(logLanguage[LogLanguageKey.SPEED_INVALID], session.Character.VisualId);
                return;
            }

            if (!session.Character.MapInstance.Map.IsWalkable(walkPacket.XCoordinate, walkPacket.YCoordinate))
            {
                logger.LogWarning(
                    "walk dropped: destination not walkable for character {VisualId} from ({FromX},{FromY}) to ({ToX},{ToY})",
                    session.Character.VisualId,
                    session.Character.PositionX, session.Character.PositionY,
                    walkPacket.XCoordinate, walkPacket.YCoordinate);
                return;
            }

            session.Character.PositionX = walkPacket.XCoordinate;
            session.Character.PositionY = walkPacket.YCoordinate;
            if (session.Character.MapInstance.MapInstanceType == MapInstanceType.BaseMapInstance)
            {
                session.Character.MapX = walkPacket.XCoordinate;
                session.Character.MapY = walkPacket.YCoordinate;
            }
            session.Character.LastMove = clock.GetCurrentInstant();

            logger.LogInformation(
                "walk accepted: character {VisualId} wrote ({WrittenX},{WrittenY}); ECS now reads PositionX/Y=({StoredX},{StoredY}) MapX/Y=({StoredMapX},{StoredMapY})",
                session.Character.VisualId,
                walkPacket.XCoordinate, walkPacket.YCoordinate,
                session.Character.PositionX, session.Character.PositionY,
                session.Character.MapX, session.Character.MapY);

            await session.Character.MapInstance.SendPacketAsync(session.Character.GenerateMove(),
                new EveryoneBut(session.Channel!.Id));

            await messageBus.PublishAsync(new CharacterMovedEvent(
                session.Character,
                session.Character.MapX,
                session.Character.MapY,
                session.Character.MapId));
        }
    }
}
