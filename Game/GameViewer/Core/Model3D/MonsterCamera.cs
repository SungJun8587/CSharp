//***************************************************************************
// MonsterCamera.cs : 몬스터 3D 그림용 카메라(GL 과 무관한 행렬 계산).
//   정사영 카메라가 ElevDeg 만큼 내려다본다(기존 이미지 시트와 같은 모습). 한 칸(정사각형)에 모델 한 마리를 그린다:
//     가로 = 모델 높이의 2배, 발 밑은 칸 아래에서 FootFrac 위, 모델의 가로 폭 전체가 들어간다.
//   yaw: 0 = 정면(화면 아래, 카메라 쪽)을 본다. 커지면 화면 오른쪽 -> 뒤 -> 왼쪽(기존 방향 번호와 같다: yaw = atan2(FaceX, FaceY)).
//   행렬은 열 우선 float[16] (GL 의 uniform 에 그대로 올린다).
//***************************************************************************
using System;

namespace GameViewer.Core.Model3D
{
    public static class MonsterCamera
    {
        public const float ElevDeg = 20f;
        public const float FootFrac = 0.2f;          // 칸 아래에서 발 밑까지(칸 높이에 대한 비율)
        public const float CellModels = 2f;          // 칸 한 변 = 모델 높이의 몇 배
        public const float FootRowFrac = 1f - FootFrac;   // 칸 위에서 발 밑까지(0.8)

        /// <summary>mvp: 모델 좌표 -> 클립 좌표. rot: 모델 -> 카메라 공간 회전(위 3x3, 법선용). hm: 모델 높이(모델 단위).</summary>
        public static void Build(float yaw, float hm, float[] mvp, float[] rot)
        {
            float c = (float)Math.Cos(yaw), s = (float)Math.Sin(yaw);
            float ce = (float)Math.Cos(ElevDeg * Math.PI / 180.0), se = (float)Math.Sin(ElevDeg * Math.PI / 180.0);
            // 모델: y 축 둘레로 yaw.  x' = c x + s z,  z' = -s x + c z
            float[] m = { c, 0, -s, 0, 0, 1, 0, 0, s, 0, c, 0, 0, 0, 0, 1 };
            // 보기: y'' = ce y' - se z',  z'' = se y' + ce z'   (z'' 클수록 카메라에 가깝다)
            float[] v = { 1, 0, 0, 0, 0, ce, se, 0, 0, -se, ce, 0, 0, 0, 0, 1 };
            Skeleton.Mul(v, 0, m, 0, rot, 0);
            float cy = (CellModels * (0.5f - FootFrac)) * hm;     // 칸 가운데의 높이(발 밑 기준): 칸이 [-FootFrac*2hm, (1-FootFrac)*2hm] 를 덮는다
            float inv = 1f / hm, depth = 1f / (4f * hm);
            float[] o = { inv, 0, 0, 0, 0, inv, 0, 0, 0, 0, -depth, 0, 0, -cy * inv, 0, 1 };
            Skeleton.Mul(o, 0, rot, 0, mvp, 0);
        }

        /// <summary>칸(정사각형)의 월드 크기 = 보이는 높이(월드 단위) x CellModels.</summary>
        public static float CellWorld(float heightWorld) { return heightWorld * CellModels; }
    }
}
