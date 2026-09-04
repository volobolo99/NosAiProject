/*
 * Third-party source copied for academic reference/reuse.
 * Original: BlowaXD/SaltyEmu
 * Repository: https://github.com/BlowaXD/SaltyEmu
 * Source path: src/World/WorldServer.cs
 * Upstream blob SHA: c39557744c8f857416c8db10d4ef7a3e8d354372
 * License: GPL-family as declared by upstream; verify against upstream LICENSE
 * before redistribution.
 *
 * Reference only; not compiled automatically by NosAiProject.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autofac;
using ChickenAPI.Core.IoC;
using ChickenAPI.Core.Plugins;
using ChickenAPI.Core.Plugins.Exceptions;
using ChickenAPI.Packets;
using NLog;
using SaltyEmu.BasicAlgorithmPlugin;
using SaltyEmu.BasicPlugin;
using SaltyEmu.Communication.Utils;
using SaltyEmu.Core.Plugins;
using SaltyEmu.DatabasePlugin;
using SaltyEmu.FriendsPlugin;
using SaltyEmu.PathfinderPlugin;
using SaltyEmu.RedisWrappers;
using World.Network;
using World.Packets;
using Logger = ChickenAPI.Core.Logging.Logger;

namespace World
{
    internal class WorldServer
    {
        private static readonly Logger Log = Logger.GetLogger<WorldServer>();
        private static readonly IPluginManager PluginManager = new SimplePluginManager();

        private static readonly List<IPlugin> Plugins = new List<IPlugin>
        {
            new DatabasePlugin(),
            new BasicAlgorithmPlugin(),
            new RedisPlugin(),
            new BasicPlugin(),
            new PathfinderPlugin(),
            new PacketHandlerPlugin(),
            new EssentialsPlugin(),
            new RelationsPlugin()
        };

        private static bool InitializePlugins()
        {
            try
            {
                ChickenContainer.Builder.Register(s => PluginManager).As<IPluginManager>();
                if (!Directory.Exists("plugins"))
                {
                    Directory.CreateDirectory("plugins");
                }

                foreach (IPlugin plugin in Plugins)
                {
                    plugin.OnLoad();
                }

                Plugins.AddRange(PluginManager.LoadPlugins(new DirectoryInfo("plugins")));
            }
            catch (CriticalPluginException e)
            {
                Log.Error("[PLUGIN]", e);
                return true;
            }
            catch (Exception e)
            {
                Log.Error("Plugins load", e);
            }

            return false;
        }

        private static void InitializeConfigs()
        {
            string port = Environment.GetEnvironmentVariable("SERVER_PORT") ?? "1337";
            if (!int.TryParse(port, out int intPort))
            {
                intPort = 1337;
            }

            string tick = Environment.GetEnvironmentVariable("SERVER_REFRESH_RATE") ?? "10";
            if (!int.TryParse(tick, out int tickRate))
            {
                tickRate = 10;
            }

            Server.Port = intPort;
            Server.Ip = Environment.GetEnvironmentVariable("SERVER_IP") ?? "127.0.0.1";
            Server.WorldGroup = Environment.GetEnvironmentVariable("SERVER_WORLDGROUP") ?? "NosWings";
            Server.TickRate = tickRate;
            Log.Info($"SERVER_IP : {Server.Ip}");
            Log.Info($"SERVER_PORT : {Server.Port}");
            Log.Info($"SERVER_WORLDGROUP : {Server.WorldGroup}");
            Log.Info($"SERVER_REFRESH_RATE : {Server.TickRate} Hz");
        }

        private static void InitializeLogger()
        {
            Logger.Initialize();
        }

        private static void EnablePlugins(PluginEnableTime enableTime)
        {
            Log.Info($"Enabling plugins of type {enableTime}");
            foreach (IPlugin plugin in Plugins.Where(s => s.EnableTime == enableTime))
            {
                plugin.OnEnable();
            }
        }

        private static void Main()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            InitializeLogger();
            InitializeConfigs();
            ChickenContainer.Builder.Register(_ => new PluggablePacketFactory()).As<IPacketFactory>().SingleInstance();
            if (InitializePlugins())
            {
                return;
            }

            EnablePlugins(PluginEnableTime.PreContainerBuild);
            CommunicationIocInjector.Inject();
            ChickenContainer.Initialize();
            if (Server.RegisterServer())
            {
                Log.Info("REGISTER_FAIL_SERVER_API");
                return;
            }

            EnablePlugins(PluginEnableTime.PostContainerBuild);
            Server.RunServerAsync(1337).ConfigureAwait(false).GetAwaiter().GetResult();
            Server.UnregisterServer();
            LogManager.Shutdown();
        }
    }
}
