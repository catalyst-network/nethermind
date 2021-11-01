//  Copyright (c) 2021 Demerzel Solutions Limited
//  This file is part of the Nethermind library.
// 
//  The Nethermind library is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  The Nethermind library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
// 

using System;
using System.Collections.Generic;
using DotNetty.Common.Utilities;
using Nethermind.AccountAbstraction.Data;
using Nethermind.AccountAbstraction.Source;
using Nethermind.Core.Crypto;
using Nethermind.JsonRpc;
using Nethermind.Logging;
using Nethermind.Network;
using Nethermind.Network.P2P;
using Nethermind.Network.Rlpx;
using Nethermind.Stats;
using Nethermind.Stats.Model;
using Timeouts = Nethermind.Network.Timeouts;

namespace Nethermind.AccountAbstraction.Network
{
    public class AaProtocolHandler : ProtocolHandlerBase, IZeroProtocolHandler
    {
        private readonly ISession _session;
        private readonly IUserOperationPool _userOperationPool;

        public AaProtocolHandler(ISession session,
            IMessageSerializationService serializer,
            INodeStatsManager nodeStatsManager,
            IUserOperationPool userOperationPool,
            ILogManager logManager)
            : base(session, nodeStatsManager, serializer, logManager)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _userOperationPool = userOperationPool ?? throw new ArgumentNullException(nameof(userOperationPool));
        }
        
        public override byte ProtocolVersion => 0;
        
        public override string ProtocolCode => Protocol.AA;
        
        public override int MessageIdSpaceSize => 4;

        public override string Name => "aa";
        
        protected override TimeSpan InitTimeout => Timeouts.Eth;
        
        public override event EventHandler<ProtocolInitializedEventArgs>? ProtocolInitialized;

        public override event EventHandler<ProtocolEventArgs> SubprotocolRequested
        {
            add { }
            remove { }
        }

        public override void Init()
        {
            ProtocolInitialized?.Invoke(this, new ProtocolInitializedEventArgs(this));
        }

        public override void HandleMessage(Packet message)
        {
            ZeroPacket zeroPacket = new(message);
            try
            {
                HandleMessage(zeroPacket);
            }
            finally
            {
                zeroPacket.SafeRelease();
            }
        }
        
        public void HandleMessage(ZeroPacket message)
        {
            // int size = message.Content.ReadableBytes;

            switch (message.PacketType)
            {
                case AaMessageCode.UserOperations:
                    Metrics.UserOperationsMessagesReceived++;
                    UserOperationsMessage uopMsg = Deserialize<UserOperationsMessage>(message.Content);
                    ReportIn(uopMsg);
                    Handle(uopMsg);
                    break;
                // case AaMessageCode.NewPooledUserOperationsHashes:
                //     NewPooledUserOperationsHashesMessage newPooledUopMsg =
                //         Deserialize<NewPooledUserOperationsHashesMessage>(message.Content);
                //     Metrics.NewPooledUserOperationsHashesMessagesReceived++;
                //     ReportIn(newPooledUopMsg);
                //     Handle(newPooledUopMsg);
                //     break;
                // case AaMessageCode.GetPooledUserOperations:
                //     GetPooledUserOperationsMessage getPooledUopMsg
                //         = Deserialize<GetPooledUserOperationsMessage>(message.Content);
                //     Metrics.GetPooledUserOperationsMessagesReceived++;
                //     ReportIn(getPooledUopMsg);
                //     Handle(getPooledUopMsg);
                //     break;
                // case AaMessageCode.PooledUserOperations:
                //     PooledUserOperationsMessage pooledUopMsg
                //         = Deserialize<PooledUserOperationsMessage>(message.Content);
                //     Metrics.PooledUserOperationsMessagesReceived++;
                //     ReportIn(pooledUopMsg);
                //     Handle(pooledUopMsg.EthMessage);
                //     break;
            }
        }

        private void Handle(UserOperationsMessage uopMsg)
        {
            IList<UserOperation> userOperations = uopMsg.UserOperations;
            for (int i = 0; i < userOperations.Count; i++)
            {
                UserOperation uop = userOperations[i];
                ResultWrapper<Keccak> result = _userOperationPool.AddUserOperation(uop);

                if (Logger.IsTrace) Logger.Trace($"{_session.Node:c} sent {uop.Hash} uop and it was {result}");
            }
        }

        public override void DisconnectProtocol(DisconnectReason disconnectReason, string details)
        {
            Dispose();
        }
        
        public override void Dispose() { }
    }
}