using System;
using System.Collections.Generic;

namespace GameViewer.Core.Logic
{
    /// <summary>
    /// 서버와의 왕복 지연(RTT) 측정. 주기마다 핑을 보내고(Due/Begin), 응답(OnPong)에서 클라이언트 시계로만 RTT 를 계산한다.
    /// 응답이 TimeoutMs 안에 안 오면 손실로 센다(Expire). 표시용 값은 최근 Window 개 표본의 평균/최대.
    /// </summary>
    public sealed class PingMeter
    {
        public const int IntervalMs = 2000, TimeoutMs = 5000, Window = 8;

        private readonly Dictionary<uint, long> _pending = new Dictionary<uint, long>();
        private readonly Queue<int> _samples = new Queue<int>();
        private uint _seq;
        private long _nextAt = long.MinValue;

        public int LastMs { get; private set; }
        public int Lost { get; private set; }
        public int Received { get; private set; }
        public bool HasSample { get { return _samples.Count > 0; } }

        public void Reset()
        {
            _pending.Clear(); _samples.Clear(); _seq = 0; _nextAt = long.MinValue; LastMs = 0; Lost = 0; Received = 0;
        }

        /// <summary>지금 핑을 보낼 때인가.</summary>
        public bool Due(long now) { return now >= _nextAt; }

        /// <summary>핑 한 개를 보낸 것으로 기록하고 순번을 돌려준다.</summary>
        public uint Begin(long now)
        {
            uint s = ++_seq;
            _pending[s] = now; _nextAt = now + IntervalMs;
            return s;
        }

        /// <summary>응답 처리. 모르는 순번(이미 만료/중복)은 무시한다. 처리했으면 true.</summary>
        public bool OnPong(uint seq, long now)
        {
            long sent;
            if (!_pending.TryGetValue(seq, out sent)) return false;
            _pending.Remove(seq);
            long rtt = now - sent;
            if (rtt < 0) return false;
            LastMs = (int)Math.Min(rtt, 99999); ++Received;
            _samples.Enqueue(LastMs); while (_samples.Count > Window) _samples.Dequeue();
            return true;
        }

        /// <summary>오래된 미응답 핑을 손실로 센다.</summary>
        public void Expire(long now)
        {
            List<uint> dead = null;
            foreach (KeyValuePair<uint, long> kv in _pending)
                if (now - kv.Value > TimeoutMs) { if (dead == null) dead = new List<uint>(); dead.Add(kv.Key); }
            if (dead == null) return;
            for (int i = 0; i < dead.Count; ++i) _pending.Remove(dead[i]);
            Lost += dead.Count;
        }

        public int AvgMs { get { if (_samples.Count == 0) return 0; long t = 0; foreach (int v in _samples) t += v; return (int)(t / _samples.Count); } }
        public int MaxMs { get { int m = 0; foreach (int v in _samples) if (v > m) m = v; return m; } }

        /// <summary>0 좋음 / 1 보통 / 2 나쁨 (평균 기준 80ms / 150ms, 최근 손실이 있으면 한 단계 나쁘게).</summary>
        public int Quality
        {
            get
            {
                int a = AvgMs; int q = a < 80 ? 0 : (a < 150 ? 1 : 2);
                if (Lost > 0 && _pending.Count > 0 && q < 2) ++q;
                return q;
            }
        }
    }
}
