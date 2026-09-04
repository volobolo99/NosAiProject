/*
 * Third-party source copied for academic reference/reuse.
 * Original: OpenNos Emulator Project
 * Repository: https://github.com/OpenNos/OpenNos
 * Source path: OpenNos.Handler/LoginPacketHandler.cs
 * Upstream blob SHA: 5e6b7c66cc721f12fb55721106f0fbb9ffd9995e
 * License: GNU GPL v2 or later (see third_party/licenses/ and upstream LICENSE)
 *
 * This file is intentionally retained in the third_party vault. It is NOT
 * compiled into NosAiProject automatically. Preserve this header and license
 * when copying/adapting code into a covered work.
 */

using OpenNos.Core;
using OpenNos.DAL;
using OpenNos.Data;
using OpenNos.Domain;
using OpenNos.GameObject;
using OpenNos.GameObject.Packets.ClientPackets;
using OpenNos.Master.Library.Client;
using System;
using System.Configuration;
using System.Linq;
using OpenNos.DAL.EF;

namespace OpenNos.Handler
{
    public class LoginPacketHandler : IPacketHandler
    {
        private readonly ClientSession _session;

        public LoginPacketHandler(ClientSession session)
        {
            _session = session;
        }

        public string BuildServersPacket(long accountId, int sessionId)
        {
            string channelpacket = CommunicationServiceClient.Instance.RetrieveRegisteredWorldServers(sessionId);

            if (channelpacket != null)
            {
                return channelpacket;
            }

            Logger.Log.Error("Could not retrieve Worldserver groups. Please make sure they've already been registered.");
            _session.SendPacket($"fail {string.Format(Language.Instance.GetMessageFromKey(\"MAINTENANCE\"), DateTime.Now)}");
            return null;
        }

        public void VerifyLogin(LoginPacket loginPacket)
        {
            if (loginPacket == null)
            {
                return;
            }

            UserDTO user = new UserDTO
            {
                Name = loginPacket.Name,
                Password = ConfigurationManager.AppSettings["UseOldCrypto"] == "true" ? EncryptionBase.Sha512(LoginEncryption.GetPassword(loginPacket.Password)).ToUpper() : loginPacket.Password
            };

            AccountDTO loadedAccount = DAOFactory.AccountDAO.FirstOrDefault(s => s.Name == user.Name);
            if (loadedAccount != null && loadedAccount.Password.ToUpper().Equals(user.Password))
            {
                if (!CommunicationServiceClient.Instance.IsAccountConnected(loadedAccount.AccountId))
                {
                    AuthorityType type = loadedAccount.Authority;
                    PenaltyLogDTO penalty = DAOFactory.PenaltyLogDAO.FirstOrDefault(s => s.AccountId.Equals(loadedAccount.AccountId) && s.DateEnd > DateTime.Now && s.Penalty == PenaltyType.Banned);
                    if (penalty != null)
                    {
                        _session.SendPacket("failc 7");
                    }
                    else
                    {
                        switch (type)
                        {
                            case AuthorityType.Unconfirmed:
                                _session.SendPacket($"failc {(byte)LoginFailType.CantConnect}");
                                break;
                            case AuthorityType.Banned:
                                _session.SendPacket($"failc {(byte)LoginFailType.Banned}");
                                break;
                            case AuthorityType.Closed:
                                _session.SendPacket($"failc {(byte)LoginFailType.CantConnect}");
                                break;
                            default:
                                int newSessionId = SessionFactory.Instance.GenerateSessionId();
                                Logger.Log.DebugFormat(Language.Instance.GetMessageFromKey("CONNECTION"), user.Name, newSessionId);
                                try
                                {
                                    CommunicationServiceClient.Instance.RegisterAccountLogin(loadedAccount.AccountId, newSessionId, loadedAccount.Name);
                                }
                                catch (Exception ex)
                                {
                                    Logger.Log.Error("General Error SessionId: " + newSessionId, ex);
                                }
                                _session.SendPacket(BuildServersPacket(loadedAccount.AccountId, newSessionId));
                                break;
                        }
                    }
                }
                else
                {
                    _session.SendPacket($"failc {(byte)LoginFailType.AlreadyConnected}");
                }
            }
            else
            {
                _session.SendPacket($"failc {(byte)LoginFailType.AccountOrPasswordWrong}");
            }
        }
    }
}
