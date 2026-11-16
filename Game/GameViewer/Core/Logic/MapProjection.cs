//***************************************************************************
// MapProjection.cs : 월드 좌표 <-> 지도 창 좌표 변환(확대/이동 포함). 월드맵 창과 클릭 이동이 같은 식을 쓴다.
//***************************************************************************
using System;

namespace GameViewer.Core.Logic
{
    public struct MapProjection
    {
        public float PanelX, PanelY, PanelW, PanelH;     // 지도가 그려지는 영역(화면 좌표)
        public float WorldMinX, WorldMinY, WorldMaxX, WorldMaxY;
        public float Zoom;                               // 1 = 월드 전체가 영역에 맞음
        public float CenterX, CenterY;                   // 영역 가운데에 오는 월드 좌표

        /// <summary>월드 전체가 영역에 꼭 맞는 배율(px/unit) — 가로/세로 중 작은 쪽 기준.</summary>
        public float FitScale
        {
            get
            {
                float ww = Math.Max(1f, WorldMaxX - WorldMinX), wh = Math.Max(1f, WorldMaxY - WorldMinY);
                return Math.Min(PanelW / ww, PanelH / wh);
            }
        }
        public float Scale { get { return FitScale * Zoom; } }

        public float ToScreenX(float wx) { return PanelX + PanelW * 0.5f + (wx - CenterX) * Scale; }
        public float ToScreenY(float wy) { return PanelY + PanelH * 0.5f + (wy - CenterY) * Scale; }
        public float ToWorldX(float sx) { return CenterX + (sx - PanelX - PanelW * 0.5f) / Scale; }
        public float ToWorldY(float sy) { return CenterY + (sy - PanelY - PanelH * 0.5f) / Scale; }

        public bool Contains(float sx, float sy) { return sx >= PanelX && sy >= PanelY && sx < PanelX + PanelW && sy < PanelY + PanelH; }

        /// <summary>월드가 영역 밖으로 너무 벗어나지 않게 중심과 배율을 바로잡는다.</summary>
        public void Clamp()
        {
            if (Zoom < 1f) Zoom = 1f;
            if (Zoom > 8f) Zoom = 8f;
            float halfW = PanelW * 0.5f / Scale, halfH = PanelH * 0.5f / Scale;
            float ww = WorldMaxX - WorldMinX, wh = WorldMaxY - WorldMinY;
            CenterX = halfW * 2f >= ww ? (WorldMinX + WorldMaxX) * 0.5f : Math.Max(WorldMinX + halfW, Math.Min(WorldMaxX - halfW, CenterX));
            CenterY = halfH * 2f >= wh ? (WorldMinY + WorldMaxY) * 0.5f : Math.Max(WorldMinY + halfH, Math.Min(WorldMaxY - halfH, CenterY));
        }

        /// <summary>커서(sx,sy) 아래의 월드 지점이 그대로 있도록 배율을 바꾼다(휠 확대).</summary>
        public void ZoomAt(float sx, float sy, float factor)
        {
            float wx = ToWorldX(sx), wy = ToWorldY(sy);
            Zoom *= factor;
            if (Zoom < 1f) Zoom = 1f;
            if (Zoom > 8f) Zoom = 8f;
            CenterX = wx - (sx - PanelX - PanelW * 0.5f) / Scale;
            CenterY = wy - (sy - PanelY - PanelH * 0.5f) / Scale;
            Clamp();
        }
    }
}
