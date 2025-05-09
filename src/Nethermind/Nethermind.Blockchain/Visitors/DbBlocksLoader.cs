// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Logging;

namespace Nethermind.Blockchain.Visitors
{
    public class DbBlocksLoader : IBlockTreeVisitor, IDisposable
    {
        public const int DefaultBatchSize = 4000;

        private readonly long _batchSize;
        private readonly long _blocksToLoad;
        private readonly IBlockTree _blockTree;
        private readonly ILogger _logger;

        private readonly BlockTreeSuggestPacer _blockTreeSuggestPacer;

        public DbBlocksLoader(IBlockTree blockTree,
            ILogger logger,
            long? startBlockNumber = null,
            long batchSize = DefaultBatchSize,
            long maxBlocksToLoad = long.MaxValue)
        {
            _blockTree = blockTree ?? throw new ArgumentNullException(nameof(blockTree));
            _blockTreeSuggestPacer = new BlockTreeSuggestPacer(_blockTree, batchSize, batchSize / 2);
            _logger = logger;

            _batchSize = batchSize;
            StartLevelInclusive = Math.Max(0L, startBlockNumber ?? (_blockTree.Head?.Number + 1) ?? 0L);
            _blocksToLoad = Math.Min(maxBlocksToLoad, _blockTree.BestKnownNumber - StartLevelInclusive);
            EndLevelExclusive = StartLevelInclusive + _blocksToLoad + 1;

            LogPlannedOperation();
        }

        public bool PreventsAcceptingNewBlocks => true;
        public bool CalculateTotalDifficultyIfMissing => true;
        public long StartLevelInclusive { get; }

        public long EndLevelExclusive { get; }

        Task<LevelVisitOutcome> IBlockTreeVisitor.VisitLevelStart(ChainLevelInfo? chainLevelInfo, long levelNumber, CancellationToken cancellationToken)
        {
            if (chainLevelInfo is null)
            {
                return Task.FromResult(LevelVisitOutcome.StopVisiting);
            }

            if (chainLevelInfo.BlockInfos.Length == 0)
            {
                // this should never happen as we should have run the fixer first
                throw new InvalidDataException("Level has no blocks when loading blocks from the DB");
            }

            return Task.FromResult(LevelVisitOutcome.None);
        }

        Task<bool> IBlockTreeVisitor.VisitMissing(Hash256 hash, CancellationToken cancellationToken)
        {
            throw new InvalidDataException($"Block {hash} is missing from the database when loading blocks.");
        }

        Task<HeaderVisitOutcome> IBlockTreeVisitor.VisitHeader(BlockHeader header, CancellationToken cancellationToken)
        {
            long i = header.Number - StartLevelInclusive;
            if (i % _batchSize == _batchSize - 1 && i != _blocksToLoad - 1 && _blockTree.Head.Number + _batchSize < header.Number)
            {
                if (_logger.IsInfo) _logger.Info($"Loaded {i + 1} out of {_blocksToLoad} headers from DB.");
            }

            return Task.FromResult(HeaderVisitOutcome.None);
        }

        async Task<BlockVisitOutcome> IBlockTreeVisitor.VisitBlock(Block block, CancellationToken cancellationToken)
        {
            // this will hang
            Task waitTask = _blockTreeSuggestPacer.WaitForQueue(block.Number, cancellationToken);

            long i = block.Number - StartLevelInclusive;
            if (!waitTask.IsCompleted)
            {
                if (_logger.IsInfo)
                {
                    _logger.Info($"Loaded {i + 1} out of {_blocksToLoad} blocks from DB into processing queue, waiting for processor before loading more.");
                }

                await waitTask;
            }

            return BlockVisitOutcome.Suggest;
        }

        Task<LevelVisitOutcome> IBlockTreeVisitor.VisitLevelEnd(ChainLevelInfo chainLevelInfo, long levelNumber, CancellationToken cancellationToken)
        {
            return Task.FromResult(LevelVisitOutcome.None);
        }

        private void LogPlannedOperation()
        {
            if (_blocksToLoad <= 0)
            {
                if (_logger.IsInfo) _logger.Info("Found no blocks to load from DB");
            }
            else
            {
                if (_logger.IsInfo) _logger.Info($"Found {_blocksToLoad} blocks to load from DB starting from current head block {_blockTree.Head?.ToString(Block.Format.Short)}");
            }
        }

        public void Dispose()
        {
            _blockTreeSuggestPacer.Dispose();
        }
    }
}
