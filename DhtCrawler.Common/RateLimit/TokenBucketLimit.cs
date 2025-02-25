﻿using System;

namespace DhtCrawler.Common.RateLimit
{
    public class TokenBucketLimit : IRateLimit
    {
        /// <summary>
        /// 容量
        /// </summary>
        private readonly int _capacity;
        /// <summary>
        /// 填充时间间隔
        /// </summary>
        private readonly long _fillTick;

        private readonly bool _hasLimit;

        private int _tokens;
        private long _nextTick;

        public TokenBucketLimit(int capacity, int fillInterval, TimeUnit unit)
        {
            _tokens = capacity;
            _capacity = capacity;
            _hasLimit = capacity > 0;
            _nextTick = DateTime.Now.Ticks;
            _fillTick = TimeSpan.FromMilliseconds(fillInterval * (int)unit).Ticks;
        }

        public bool Require(int count, out TimeSpan waitTime)
        {
            if (!_hasLimit)
            {
                return true;
            }
            lock (this)
            {
                while (true)
                {
                    var now = DateTime.Now.Ticks;
                    if (_nextTick <= now)
                    {
                        _tokens = _capacity;
                        _nextTick = now + _fillTick;
                    }
                    if (_tokens < count)
                    {
                        var waitTick = _nextTick - now;
                        if (waitTick <= 0)
                            continue;
                        waitTime = TimeSpan.FromTicks(waitTick);
                        return false;
                    }
                    _tokens -= count;
                    return true;
                }
            }
        }
    }
}
