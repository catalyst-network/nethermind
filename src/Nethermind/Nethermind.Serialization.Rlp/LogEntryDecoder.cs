// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core;
using Nethermind.Core.Crypto;

namespace Nethermind.Serialization.Rlp
{
    public class LogEntryDecoder : IRlpStreamDecoder<LogEntry>, IRlpValueDecoder<LogEntry>
    {
        public static LogEntryDecoder Instance { get; } = new();

        public LogEntry? Decode(RlpStream rlpStream, RlpBehaviors rlpBehaviors = RlpBehaviors.None)
        {
            if (rlpStream.IsNextItemNull())
            {
                rlpStream.ReadByte();
                return null;
            }

            rlpStream.ReadSequenceLength();
            Address? address = rlpStream.DecodeAddress();
            long sequenceLength = rlpStream.ReadSequenceLength();
            Hash256[] topics = new Hash256[sequenceLength / 33];
            for (int i = 0; i < topics.Length; i++)
            {
                topics[i] = rlpStream.DecodeKeccak();
            }

            byte[] data = rlpStream.DecodeByteArray();

            return new LogEntry(address, data, topics);
        }

        public LogEntry? Decode(ref Rlp.ValueDecoderContext decoderContext, RlpBehaviors rlpBehaviors = RlpBehaviors.None)
        {
            if (decoderContext.IsNextItemNull())
            {
                decoderContext.ReadByte();
                return null;
            }

            decoderContext.ReadSequenceLength();
            Address? address = decoderContext.DecodeAddress();
            long sequenceLength = decoderContext.ReadSequenceLength();
            Hash256[] topics = new Hash256[sequenceLength / 33];
            for (int i = 0; i < topics.Length; i++)
            {
                topics[i] = decoderContext.DecodeKeccak();
            }

            byte[] data = decoderContext.DecodeByteArray();

            return new LogEntry(address, data, topics);
        }

        public Rlp Encode(LogEntry? item, RlpBehaviors rlpBehaviors = RlpBehaviors.None)
        {
            if (item is null)
            {
                return Rlp.OfEmptySequence;
            }

            RlpStream rlpStream = new(GetLength(item, rlpBehaviors));
            Encode(rlpStream, item, rlpBehaviors);
            return new Rlp(rlpStream.Data.ToArray());
        }

        public void Encode(RlpStream rlpStream, LogEntry? item, RlpBehaviors rlpBehaviors = RlpBehaviors.None)
        {
            if (item is null)
            {
                rlpStream.EncodeNullObject();
                return;
            }

            var (total, topics) = GetContentLength(item);
            rlpStream.StartSequence(total);

            rlpStream.Encode(item.Address);
            rlpStream.StartSequence(topics);

            for (var i = 0; i < item.Topics.Length; i++)
            {
                rlpStream.Encode(item.Topics[i]);
            }

            rlpStream.Encode(item.Data);
        }

        public int GetLength(LogEntry? item, RlpBehaviors rlpBehaviors = RlpBehaviors.None)
        {
            if (item is null)
            {
                return 1;
            }

            return Rlp.LengthOfSequence(GetContentLength(item).Total);
        }

        private static (int Total, int Topics) GetContentLength(LogEntry? item)
        {
            var contentLength = 0;
            if (item is null)
            {
                return (contentLength, 0);
            }

            contentLength += Rlp.LengthOf(item.Address);

            int topicsLength = GetTopicsLength(item);
            contentLength += Rlp.LengthOfSequence(topicsLength);
            contentLength += Rlp.LengthOf(item.Data);

            return (contentLength, topicsLength);
        }

        private static int GetTopicsLength(LogEntry? item)
        {
            if (item is null)
            {
                return 0;
            }

            int topicsLength = 0;
            for (int i = 0; i < item.Topics.Length; i++)
            {
                topicsLength += Rlp.LengthOf(item.Topics[i]);
            }

            return topicsLength;
        }

        public static void DecodeStructRef(scoped ref Rlp.ValueDecoderContext decoderContext, RlpBehaviors storage, out LogEntryStructRef item)
        {
            if (decoderContext.IsNextItemNull())
            {
                decoderContext.ReadByte();
                item = new LogEntryStructRef();
                return;
            }

            decoderContext.ReadSequenceLength();
            decoderContext.DecodeAddressStructRef(out var address);
            var (PrefixLength, ContentLength) = decoderContext.PeekPrefixAndContentLength();
            var sequenceLength = PrefixLength + ContentLength;
            var topics = decoderContext.Data.Slice(decoderContext.Position, sequenceLength);
            decoderContext.SkipItem();
            var data = decoderContext.DecodeByteArraySpan();

            item = new LogEntryStructRef(address, data, topics);
        }
    }
}
