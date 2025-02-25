﻿using DhtCrawler.Common.Compare;
using DhtCrawler.Common.Filters;
using DhtCrawler.Common.Utils;
using log4net;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DhtCrawler.DHT.Message
{
    public class MessageMap : AbstractMessageMap
    {
        private class MapInfo
        {
            public byte[] InfoHash { get; set; }
            public DateTime LastTime { get; set; }
        }

        private class IdMapInfo
        {
            private readonly HashSet<long> peers;
            public TransactionId TransactionId { get; set; }
            public int Count
            {
                get
                {
                    lock (this)
                    {
                        return peers.Count;
                    }
                }
            }

            public IdMapInfo()
            {
                peers = new HashSet<long>();
            }

            public bool Add(long peer)
            {
                lock (this)
                    return peers.Add(peer);
            }

            public bool Remove(long peer)
            {
                lock (this)
                    return peers.Remove(peer);
            }

            public IList<long> GetPeers()
            {
                lock (this)
                {
                    return peers.ToArray();
                }
            }
        }

        private static readonly ILog log = LogManager.GetLogger(Assembly.GetEntryAssembly(), "watchLogger");

        private static readonly IEqualityComparer<byte[]> ByteArrayComparer =
            new WrapperEqualityComparer<byte[]>((x, y) => x.Length == y.Length && x.SequenceEqual(y),
                x => x[0] << 8 | x[x.Length - 1]);

        private readonly BlockingCollection<TransactionId> _bucket = new BlockingCollection<TransactionId>();

        private readonly ConcurrentDictionary<TransactionId, MapInfo> _mappingInfo =
            new ConcurrentDictionary<TransactionId, MapInfo>();

        private readonly ConcurrentDictionary<byte[], IdMapInfo> _idMappingInfo =
            new ConcurrentDictionary<byte[], IdMapInfo>(ByteArrayComparer);
        private readonly int _expireSeconds;
        private readonly IFilter<long> _filter;

        public override bool IsFull => _bucket.Count <= 0;

        public MessageMap(int expireSeconds)
        {
            this._expireSeconds = expireSeconds;
            InitBucket();
            _filter = IocContainer.GetService<IFilter<long>>();
        }

        private void InitBucket()
        {
            foreach (var transactionId in _bucketArray)
            {
                _bucket.Add(transactionId);
            }
        }

        private void ClearExpireMessage(int clearSize = 1000)
        {
            lock (this)
            {
                foreach (var key in _mappingInfo.Keys)
                {
                    _bucket.Add(key);
                }
                _mappingInfo.Clear();
                _idMappingInfo.Clear();
            }

            log.Info($"无可用命令ID，清除已注册命令");
            //var startTime = DateTime.Now;
            //var rmList = _mappingInfo.ToArray().OrderBy(t => t.Value.LastTime).Take(clearSize).ToDictionary(kv => kv.Value.InfoHash, kv => new { kv.Key, kv.Value.LastTime });
            //foreach (var kv in rmList)
            //{
            //    var infohash = kv.Key;
            //    var msgId = kv.Value;
            //    if (_mappingInfo.TryRemove(msgId.Key, out var map))
            //    {
            //        _bucket.Add(msgId.Key);
            //    }
            //    if (_idMappingInfo.TryRemove(infohash, out var idMap))
            //    {
            //        if (idMap.Count > 0 && (DateTime.Now - msgId.LastTime).TotalSeconds > _expireSeconds)//过期了的消息节点过滤
            //        {
            //            foreach (var peer in idMap.GetPeers())
            //            {
            //                _filter.Add(peer);
            //            }
            //        }
            //    }
            //}
            //foreach (var mapInfo in _idMappingInfo)
            //{
            //    if (mapInfo.Value.Count <= 0)
            //        _idMappingInfo.TryRemove(mapInfo.Key, out var rm);

            //}
            //log.Info($"清理过期的命令ID,清理后可用命令ID数:{_bucket.Count},用时:{(DateTime.Now - startTime).TotalSeconds}");
        }

        protected override bool RegisterGetPeersMessage(byte[] infoHash, DhtNode node, out TransactionId msgId)
        {
            var nodeId = node.CompactEndPoint().ToInt64();
            if (_filter.Contain(nodeId))
            {
                msgId = null;
                return false;
            }
            TransactionId messageId;
            if (_idMappingInfo.TryGetValue(infoHash, out var idMap))
            {
                if (!idMap.Add(nodeId))
                {
                    msgId = null;
                    return false;
                }
                messageId = idMap.TransactionId;
                if (_mappingInfo.TryGetValue(messageId, out var info))
                {
                    info.LastTime = DateTime.Now;
                }
            }
            else
            {
                msgId = null;
                var cleared = false;
                while (!_bucket.TryTake(out messageId, 1000))
                {
                    if (cleared) //清理以后没有过期命令则丢弃该消息
                    {
                        return false;
                    }
                    ClearExpireMessage();
                    cleared = true;
                }
                if (!_mappingInfo.TryAdd(messageId, new MapInfo()
                {
                    LastTime = DateTime.Now,
                    InfoHash = infoHash
                }))
                {
                    msgId = null;
                    return false;
                }
                idMap = new IdMapInfo() { TransactionId = messageId };
                idMap.Add(nodeId);
                if (_idMappingInfo.TryAdd(infoHash, idMap))
                {
                    msgId = messageId;
                    return true;
                }
                return false;
            }
            msgId = messageId;
            return true;
        }

        protected override bool RequireGetPeersRegisteredInfo(TransactionId msgId, DhtNode node, out byte[] infoHash)
        {
            if (_mappingInfo.TryGetValue(msgId, out var mapInfo))
            {
                if (_idMappingInfo.TryGetValue(mapInfo.InfoHash, out var idMap))
                {
                    lock (idMap)
                    {
                        var nodeId = node.CompactEndPoint().ToInt64();
                        var isok = idMap.Remove(nodeId);
                        if (idMap.Count <= 0)
                        {
                            _idMappingInfo.TryRemove(mapInfo.InfoHash, out var rm);
                            _mappingInfo.TryRemove(msgId, out var obj);
                            _bucket.Add(msgId);
                        }
                        if (!isok)
                        {
                            infoHash = null;
                            return false;
                        }
                    }
                }
                else
                {
                    _mappingInfo.TryRemove(msgId, out var obj);
                    _bucket.Add(msgId);
                }
            }
            if (mapInfo?.InfoHash == null)
            {
                infoHash = null;
                return false;
            }
            infoHash = mapInfo.InfoHash;
            return true;
        }
    }
}
