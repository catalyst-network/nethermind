using System.Collections.Generic;
using System.Linq;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Int256;

namespace Nethermind.JsonRpc.Modules.Eth
{
    public class GasPriceEstimateTxInsertionManager : ITxInsertionManager
    {
        private readonly IGasPriceOracle _gasPriceOracle;
        private readonly UInt256? _ignoreUnder;
        private readonly UInt256 _baseFee;
        private readonly ISpecProvider _specProvider;
        public GasPriceEstimateTxInsertionManager(IGasPriceOracle gasPriceOracle, UInt256? ignoreUnder, 
            UInt256 baseFee, ISpecProvider specProvider)
        {
            _gasPriceOracle = gasPriceOracle;
            _ignoreUnder = ignoreUnder;
            _specProvider = specProvider;
            _baseFee = baseFee;
        }

        public int AddValidTxFromBlockAndReturnCount(Block block)
        {
            if (block.Transactions.Length > 0)
            {
                Transaction[] transactionsInBlock = block.Transactions;
                int countTxAdded = AddTxAndReturnCountAdded(transactionsInBlock, block);

                if (countTxAdded == 0)
                {
                    GetTxGasPriceList().Add((UInt256) _gasPriceOracle.FallbackGasPrice!);
                    countTxAdded++;
                }

                return countTxAdded;
            }
            else
            {
                GetTxGasPriceList().Add((UInt256) _gasPriceOracle.FallbackGasPrice!);
                return 1;
            }
        }

        private int AddTxAndReturnCountAdded(Transaction[] txInBlock, Block block)
        {
            int countTxAdded = 0;
            bool eip1559Enabled = _specProvider.GetSpec(block.Number).IsEip1559Enabled;
            IEnumerable<Transaction> txsSortedByEffectiveGasPrice = txInBlock.OrderBy(tx => EffectiveGasPrice(tx, eip1559Enabled));
            foreach (Transaction tx in txsSortedByEffectiveGasPrice)
            {
                if (TransactionCanBeAdded(tx, block, eip1559Enabled))
                {
                    GetTxGasPriceList().Add(EffectiveGasPrice(tx, eip1559Enabled));
                    countTxAdded++;
                }

                if (countTxAdded >= EthGasPriceConstants.TxLimitFromABlock)
                {
                    break;
                }
            }

            return countTxAdded;
        }


        private UInt256 EffectiveGasPrice(Transaction transaction, bool eip1559Enabled)
        {
            return transaction.CalculateEffectiveGasPrice(eip1559Enabled, _baseFee);
        }

        private bool TransactionCanBeAdded(Transaction transaction, Block block, bool eip1559Enabled)
        {
            return transaction.GasPrice >= _ignoreUnder && Eip1559ModeCompatible(transaction, eip1559Enabled) &&
                   TxNotSentByBeneficiary(transaction, block);
        }

        private bool Eip1559ModeCompatible(Transaction transaction, bool eip1559Enabled)
        {
            return eip1559Enabled || !transaction.IsEip1559;
        }

        private bool TxNotSentByBeneficiary(Transaction transaction, Block block)
        {
            if (block.Beneficiary == null)
            {
                return true;
            }

            return block.Beneficiary != transaction.SenderAddress;
        }

        protected virtual List<UInt256> GetTxGasPriceList()
        {
            return _gasPriceOracle.TxGasPriceList;
        }
    }
}