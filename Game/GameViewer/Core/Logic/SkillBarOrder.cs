//***************************************************************************
// SkillBarOrder.cs : 스킬바 칸 순서. 플레이어가 칸을 끌어다 바꾼 순서를 영웅별로 기억하고, 영웅 데이터의 스킬 목록에 적용한다.
//   - 원본 목록에 없는 스킬은 무시하고, 새로 생긴 스킬은 뒤에 붙인다(데이터가 바뀌어도 깨지지 않는다).
//***************************************************************************
using System.Collections.Generic;

namespace GameViewer.Core.Logic
{
    public sealed class SkillBarOrder
    {
        private readonly Dictionary<uint, List<uint>> _byHero = new Dictionary<uint, List<uint>>();

        /// <summary>원본 스킬 목록에 저장된 순서를 적용한 새 목록.</summary>
        public List<uint> Apply(uint heroId, List<uint> raw)
        {
            List<uint> saved;
            if (!_byHero.TryGetValue(heroId, out saved)) return raw;
            List<uint> r = new List<uint>(raw.Count);
            foreach (uint id in saved) if (raw.Contains(id) && !r.Contains(id)) r.Add(id);
            foreach (uint id in raw) if (!r.Contains(id)) r.Add(id);
            return r;
        }

        /// <summary>적용된 목록의 i번째와 j번째 스킬 칸을 맞바꾼다. 범위 밖이거나 같은 칸이면 false.</summary>
        public bool Swap(uint heroId, List<uint> raw, int i, int j)
        {
            List<uint> cur = new List<uint>(Apply(heroId, raw));
            if (i == j || i < 0 || j < 0 || i >= cur.Count || j >= cur.Count) return false;
            uint t = cur[i]; cur[i] = cur[j]; cur[j] = t;
            _byHero[heroId] = cur;
            return true;
        }

        public void Reset(uint heroId) { _byHero.Remove(heroId); }
    }
}
