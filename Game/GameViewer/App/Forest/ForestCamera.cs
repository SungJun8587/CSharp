//***************************************************************************
// ForestCamera.cs : 숲 길을 천천히 걷는 1인칭 카메라. 길은 닫힌 곡선이라 시간이 아무리 흘러도 끊기지 않고 이어진다.
// 걸음에 맞춰 살짝 흔들리고, 주변을 둘러보고, 주기적으로 나뭇잎 사이 하늘을 올려다본다.
//***************************************************************************
using System;
using System.Collections.Generic;

namespace GameViewer.App.Forest
{
    public struct CamPose
    {
        public V3 Eye, Forward, Up;
        public float FovY;          // 세로 시야각(라디안)
    }

    public sealed class ForestCamera
    {
        public const float WalkSpeed = 1.15f;            // m/s
        public const float EyeHeight = 1.65f;
        public const float LookCycle = 42f;              // 올려다보는 주기(초)

        private readonly ForestWorld _w;
        public ForestCamera(ForestWorld w) { _w = w; }

        /// <summary>올려다보는 정도(0~1). 주기의 끝부분에서 부드럽게 올라갔다 내려온다.</summary>
        public static float LookUp(float t)
        {
            float c = t % LookCycle; if (c < 0f) c += LookCycle;
            return FNoise.SmoothStep(29f, 33f, c) * (1f - FNoise.SmoothStep(37f, 41f, c));
        }

        public CamPose Sample(float t)
        {
            float s = t * WalkSpeed;
            float x, z, ax, az;
            _w.PathAt(s, out x, out z);
            _w.PathAt(s + 5f, out ax, out az);
            float hx = ax - x, hz = az - z, hl = (float)Math.Sqrt(hx * hx + hz * hz);
            if (hl < 1e-6f) { hx = 1f; hz = 0f; hl = 1f; }
            hx /= hl; hz /= hl;

            float up = LookUp(t);
            float yaw = 0.30f * (float)Math.Sin(t * 0.23f) + 0.12f * (float)Math.Sin(t * 0.61f + 1.3f);
            float cy = (float)Math.Cos(yaw), sy = (float)Math.Sin(yaw);
            float dx = hx * cy - hz * sy, dz = hx * sy + hz * cy;                 // 길 방향에서 yaw 만큼 돌린 수평 방향
            float pitch = -0.03f + 0.025f * (float)Math.Sin(t * 0.37f) + up * 0.95f;
            float cp = (float)Math.Cos(pitch), sp = (float)Math.Sin(pitch);

            CamPose p = new CamPose();
            p.Forward = new V3(dx * cp, sp, dz * cp);
            float bob = (float)Math.Sin(t * 6.8f) * 0.022f * (1f - up * 0.7f);
            float side = (float)Math.Sin(t * 3.4f) * 0.012f;
            p.Eye = new V3(x - hz * side, _w.Height(x, z) + EyeHeight + bob, z + hx * side);

            V3 right = V3.Cross(p.Forward, new V3(0, 1, 0)).Normalized();
            V3 upv = V3.Cross(right, p.Forward).Normalized();
            float roll = 0.02f * (float)Math.Sin(t * 0.5f) + up * 0.05f * (float)Math.Sin(t * 0.8f);
            p.Up = (upv * (float)Math.Cos(roll) + right * (float)Math.Sin(roll)).Normalized();
            p.FovY = (62f + up * 10f) * (float)Math.PI / 180f;
            return p;
        }

        // ------------------------------------------------------------------ 영웅 선택 화면: 길 위에 선 영웅 둘레를 천천히 도는 카메라

        private float _heroS = 22f;                         // 영웅이 서 있는 길 위치(출발점에서의 거리). FitHeroStage 가 나무가 가장 덜 가리는 자리로 고른다
        public const float HeroScale = 0.62f;              // 모델 한 단위가 몇 m 인지(모델 키 약 3 -> 1.9 m)
        public const float OrbitRadius = 3.2f;

        /// <summary>
        /// 길을 따라 후보 자리를 훑어, 영웅 둘레를 도는 카메라(좌우 흔들림 전 구간)와 영웅 몸이 나무 줄기에서 가장 멀리 떨어지는 자리를 고른다.
        /// 같은 숲이면 늘 같은 자리가 나온다.
        /// </summary>
        public void FitHeroStage(ForestLayout layout)
        {
            List<float> tx = new List<float>(), tz = new List<float>();
            foreach (List<Inst> l in layout.Trees) foreach (Inst t in l) { tx.Add(t.X); tz.Add(t.Z); }
            float best = -1f, bestS = _heroS;
            for (float s = 6f; s < _w.PathLength - 6f; s += 1.5f)
            {
                _heroS = s; V3 pos, dir; HeroSpot(out pos, out dir);
                float score = 1e9f;
                for (int k = -4; k <= 4; ++k)
                {
                    float sw = k * 0.15f, c = (float)Math.Cos(sw), sn = (float)Math.Sin(sw);
                    float ex = pos.X + (dir.X * c - dir.Z * sn) * (OrbitRadius + 0.2f), ez = pos.Z + (dir.X * sn + dir.Z * c) * (OrbitRadius + 0.2f);
                    for (int i = 0; i < tx.Count; ++i)
                    {
                        float dx = tx[i] - ex, dz = tz[i] - ez; float d = (float)Math.Sqrt(dx * dx + dz * dz);
                        if (d < score) score = d;
                    }
                }
                for (int i = 0; i < tx.Count; ++i)                                      // 영웅 옆에도 나무가 없게(몸을 가리지 않게)
                {
                    float dx = tx[i] - pos.X, dz = tz[i] - pos.Z; float d = (float)Math.Sqrt(dx * dx + dz * dz) * 0.8f;
                    if (d < score) score = d;
                }
                if (score > best) { best = score; bestS = s; }
            }
            _heroS = bestS;
        }

        /// <summary>영웅이 서는 자리(발 밑)와, 길 앞쪽을 가리키는 수평 단위 방향.</summary>
        public void HeroSpot(out V3 pos, out V3 dir)
        {
            float x, z, ax, az; _w.PathAt(_heroS, out x, out z); _w.PathAt(_heroS + 2f, out ax, out az);
            float dx = ax - x, dz = az - z, l = (float)Math.Sqrt(dx * dx + dz * dz); if (l < 1e-6f) { dx = 1f; dz = 0f; l = 1f; }
            dir = new V3(dx / l, 0f, dz / l); pos = new V3(x, _w.Height(x, z), z);
        }

        /// <summary>영웅을 바라보는 카메라. 길 앞쪽에서 좌우로 천천히 흔들리며(최대 약 30도) 영웅을 본다. 좌우 폭이 1.9m 이하라 길 밖 나무에 닿지 않는다.</summary>
        public CamPose HeroSample(float t)
        {
            V3 pos, dir; HeroSpot(out pos, out dir);
            float swing = 0.52f * (float)Math.Sin(t * 0.33f) + 0.08f * (float)Math.Sin(t * 0.9f + 0.7f);
            float c = (float)Math.Cos(swing), s = (float)Math.Sin(swing);
            V3 d = new V3(dir.X * c - dir.Z * s, 0f, dir.X * s + dir.Z * c);
            float rad = OrbitRadius + 0.12f * (float)Math.Sin(t * 0.21f);
            V3 target = pos + new V3(0f, 1.05f, 0f);
            V3 eye = pos + d * rad + new V3(0f, 1.25f + 0.08f * (float)Math.Sin(t * 0.27f), 0f);
            CamPose p = new CamPose();
            p.Eye = eye; p.Forward = (target - eye).Normalized();
            V3 right = V3.Cross(p.Forward, new V3(0, 1, 0)).Normalized();
            p.Up = V3.Cross(right, p.Forward).Normalized();
            p.FovY = 66f * (float)Math.PI / 180f;
            return p;
        }

        /// <summary>
        /// 목표 지점이 화면 가로 좌표 ndcX(-1..1)에 오도록 카메라를 수평으로 살짝 돌린다(오른쪽 패널을 피해 영웅을 왼쪽으로 보내는 용도).
        /// 투영 행렬을 비스듬히 만들지 않아 하늘/빛줄기 계산을 건드리지 않는다.
        /// </summary>
        public static CamPose Pan(CamPose p, float ndcX, float aspect)
        {
            float tanX = (float)Math.Tan(p.FovY * 0.5f) * aspect;
            float b = (float)Math.Atan(-ndcX * tanX);
            V3 right = V3.Cross(p.Forward, new V3(0, 1, 0)).Normalized();
            V3 f = (p.Forward * (float)Math.Cos(b) + right * (float)Math.Sin(b)).Normalized();
            V3 r2 = V3.Cross(f, new V3(0, 1, 0)).Normalized();
            CamPose q = p; q.Forward = f; q.Up = V3.Cross(r2, f).Normalized();
            return q;
        }
    }
}
