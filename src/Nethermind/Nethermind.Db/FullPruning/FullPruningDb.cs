﻿//  Copyright (c) 2021 Demerzel Solutions Limited
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nethermind.Core;
using Nethermind.Logging;

namespace Nethermind.Db.FullPruning
{
    public class FullPruningDb : IDb, IFullPruningDb
    {
        private readonly RocksDbSettings _settings;
        private readonly IRocksDbFactory _dbFactory;
        private readonly Action? _updateDuplicateWriteMetrics;

        private IDb _currentDb;
        private PruningContext? _pruningContext;

        public FullPruningDb(RocksDbSettings settings, IRocksDbFactory dbFactory, Action? updateDuplicateWriteMetrics = null)
        {
            _settings = settings;
            _dbFactory = dbFactory;
            _updateDuplicateWriteMetrics = updateDuplicateWriteMetrics;
            _currentDb = CreateDb(_settings);
        }

        private IDb CreateDb(RocksDbSettings settings) => _dbFactory.CreateDb(settings);

        public byte[]? this[byte[] key]
        {
            get => _currentDb[key];
            set
            {
                _currentDb[key] = value;
                IDb? cloningDb = _pruningContext?.CloningDb;
                if (cloningDb != null)
                {
                    cloningDb[key] = value;
                    _updateDuplicateWriteMetrics?.Invoke();
                }
            }
        }

        public IBatch StartBatch() => 
            new DuplicatingBatch(
                _currentDb.StartBatch(), 
                _pruningContext?.CloningDb.StartBatch(),
                _updateDuplicateWriteMetrics);

        public void Dispose()
        {
            _currentDb.Dispose();
            _pruningContext?.CloningDb.Dispose();
        }

        public string Name => _settings.DbName;

        public KeyValuePair<byte[], byte[]?>[] this[byte[][] keys] => _currentDb[keys];

        public IEnumerable<KeyValuePair<byte[], byte[]>> GetAll(bool ordered = false) => _currentDb.GetAll(ordered);

        public IEnumerable<byte[]> GetAllValues(bool ordered = false) => _currentDb.GetAllValues(ordered);

        public void Remove(byte[] key)
        {
            _currentDb.Remove(key);
            IDb? cloningDb = _pruningContext?.CloningDb;
            cloningDb?.Remove(key);
        }

        public bool KeyExists(byte[] key) => _currentDb.KeyExists(key);

        public IDb Innermost => this; // we cannot expose a DB that will potentially be later deleted

        public void Flush()
        {
            _currentDb.Flush();
            IDb? cloningDb = _pruningContext?.CloningDb;
            cloningDb?.Flush();
        }

        public void Clear()
        {
            _currentDb.Clear();
            IDb? cloningDb = _pruningContext?.CloningDb;
            cloningDb?.Clear();
        }

        public bool CanStartPruning => _pruningContext is null;

        public virtual bool TryStartPruning(out IPruningContext context)
        {
            RocksDbSettings ClonedDbSettings()
            {
                RocksDbSettings clonedDbSettings = _settings.Clone();
                clonedDbSettings.DeleteOnStart = true;
                return clonedDbSettings;
            }
            
            PruningContext newContext = new(this, CreateDb(ClonedDbSettings()), _updateDuplicateWriteMetrics);
            PruningContext? pruningContext = Interlocked.CompareExchange(ref _pruningContext, newContext, null);
            context = pruningContext ?? newContext;
            return pruningContext is null;
        }
        
        public string GetPath(string basePath) => _settings.DbPath.GetApplicationResourcePath(basePath);
        public string InnerDbName => _currentDb.Name;

        private void FinishPruning()
        {
            IDb oldDb = Interlocked.Exchange(ref _currentDb, _pruningContext?.CloningDb);
            Task.Run(() => oldDb.Clear());
        }
        
        private void CancelPruning(PruningContext pruningContext)
        {
            Interlocked.CompareExchange(ref _pruningContext, null, pruningContext);
        }

        private class PruningContext : IPruningContext
        {
            private bool _commited = false;
            public IDb CloningDb { get; }
            private readonly FullPruningDb _db;
            private readonly Action? _updateDuplicateWriteMetrics;

            public PruningContext(FullPruningDb db, IDb cloningDb, Action? updateDuplicateWriteMetrics)
            {
                CloningDb = cloningDb;
                _db = db;
                _updateDuplicateWriteMetrics = updateDuplicateWriteMetrics;
            }

            public byte[]? this[byte[] key]
            {
                set
                {
                    CloningDb[key] = value;
                    _updateDuplicateWriteMetrics?.Invoke();
                }
            }

            public void Commit()
            {
                _db.FinishPruning();
                _commited = true;
            }

            public void MarkStart()
            {
                Metrics.StateDbPruning = 1;
            }

            public void Dispose()
            {
                _db.CancelPruning(this);
                if (!_commited)
                {
                    CloningDb.Clear();
                }
                Metrics.StateDbPruning = 0;
            }
        }
        
        private class DuplicatingBatch : IBatch
        {
            private readonly IBatch _batch;
            private readonly IBatch? _clonedBatch;
            private readonly Action? _updateDuplicateWriteMetrics;

            public DuplicatingBatch(
                IBatch batch, 
                IBatch? clonedBatch, 
                Action? updateDuplicateWriteMetrics)
            {
                _batch = batch;
                _clonedBatch = clonedBatch;
                _updateDuplicateWriteMetrics = updateDuplicateWriteMetrics;
            }

            public void Dispose()
            {
                _batch.Dispose();
                _clonedBatch?.Dispose();
            }

            public byte[]? this[byte[] key]
            {
                get => _batch[key];
                set
                {
                    _batch[key] = value;
                    if (_clonedBatch is not null)
                    {
                        _clonedBatch[key] = value;
                        _updateDuplicateWriteMetrics?.Invoke();
                    }
                }
            }
        }
    }
}