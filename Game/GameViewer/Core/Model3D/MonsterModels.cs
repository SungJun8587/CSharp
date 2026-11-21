//***************************************************************************
// MonsterModels.cs : 몬스터/영웅 종류(템플릿 ID) -> 3D 모델 설정. Assets/sprites.json 의 "model3d" 항목에서 채운다.
//   { "kind": "monster", "template": 21, "model3d": "models/Yeti.gltf", "walkStride": 1.6, ... }
//   영웅도 같다(kind "hero", template 1..4). 몬스터와 영웅의 번호는 따로 센다.
//   모델이 등록된 종류는 OpenGL 로 실시간으로 그리고, 등록이 없거나 GL 을 쓸 수 없으면 기존 이미지 시트/코드 그림을 쓴다.
//***************************************************************************
using System.Collections.Generic;

namespace GameViewer.Core.Model3D
{
    public sealed class ModelSpec
    {
        public string Path;             // sprites.json 기준 상대 경로
        public float WalkStride = 1.6f; // 걷기 한 바퀴에 걷는 거리(월드 단위)
    }

    public static class MonsterModels
    {
        private static readonly Dictionary<ulong, ModelSpec> Specs = new Dictionary<ulong, ModelSpec>();
        private static ulong Key(bool monster, uint tpl) { return ((monster ? 1UL : 0UL) << 32) | tpl; }
        private static readonly object Lock = new object();

        public static void Set(bool monster, uint tpl, ModelSpec spec) { lock (Lock) Specs[Key(monster, tpl)] = spec; }
        public static void Clear() { lock (Lock) Specs.Clear(); }
        public static ModelSpec Get(bool monster, uint tpl) { ModelSpec s; lock (Lock) return Specs.TryGetValue(Key(monster, tpl), out s) ? s : null; }
        public static bool Has(bool monster, uint tpl) { lock (Lock) return Specs.ContainsKey(Key(monster, tpl)); }
    }
}
