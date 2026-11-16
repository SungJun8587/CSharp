//***************************************************************************
// KeyBindings.cs : 키 설정. 동작(BindAction)마다 키 하나를 묶는다. 키 값은 Windows 가상 키 코드(WinForms 의 Keys 와 같은 값)라
// Core 는 System.Windows.Forms 를 모른 채 쓰고, 다른 엔진에서는 그 엔진의 키를 이 값으로 바꿔 넘긴다.
// 규칙: 한 키는 한 동작에만 묶인다(이미 쓰는 키를 고르면 두 동작이 키를 맞바꾼다). Esc/방향키/Shift·Ctrl·Alt/숫자패드 1~9 는 예약이라 못 쓴다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameViewer.Core.Data;

namespace GameViewer.Core.Logic
{
    public enum BindAction
    {
        BasicAttack, SelectBoss, AutoBattle, FollowToggle, CenterCamera, CycleTarget, ToggleHelp, ToggleLog, Chat, Settings, WorldMap, HeroSelect, Screenshot, Reconnect,
        Skill1, Skill2, Skill3, Skill4, Skill5, Skill6, Skill7, Skill8, Skill9
    }

    public enum BindResult { Ok, Reserved, Swapped }

    public sealed class KeyBindings
    {
        public const int ActionCount = 23;
        public const int FirstSkill = (int)BindAction.Skill1;
        private readonly int[] _key = new int[ActionCount];

        public KeyBindings() { ResetAll(); }

        // 기본 키(가상 키 코드): Q B A F Space Tab F1 L Enter F2 M H F12 F5, 스킬 1~9 = '1'~'9'
        private static readonly int[] Defaults = { 81, 66, 65, 70, 32, 9, 112, 76, 13, 113, 77, 72, 123, 116, 49, 50, 51, 52, 53, 54, 55, 56, 57 };

        private static readonly string[] Names =
        {
            "기본 공격", "보스 선택/이동", "자동 전투", "카메라 따라가기", "카메라 가운데로", "대상 바꾸기", "도움말", "로그 창 전환", "채팅 입력", "설정", "월드맵", "영웅 선택", "스크린샷", "재접속",
            "스킬 1", "스킬 2", "스킬 3", "스킬 4", "스킬 5", "스킬 6", "스킬 7", "스킬 8", "스킬 9"
        };

        public static string ActionName(BindAction a) { return Names[(int)a]; }
        public static int DefaultKey(BindAction a) { return Defaults[(int)a]; }

        public int Get(BindAction a) { return _key[(int)a]; }
        public bool IsDefault(BindAction a) { return _key[(int)a] == Defaults[(int)a]; }

        public void ResetAll() { Array.Copy(Defaults, _key, ActionCount); }
        public void Reset(BindAction a)
        {
            int d = Defaults[(int)a];
            BindAction other;
            if (TryActionFor(d, out other) && other != a) { _key[(int)other] = _key[(int)a]; }        // 기본 키를 다른 동작이 쓰고 있으면 맞바꾼다
            _key[(int)a] = d;
        }

        /// <summary>이 키에 묶인 동작. 없으면 false.</summary>
        public bool TryActionFor(int vk, out BindAction a)
        {
            for (int i = 0; i < ActionCount; ++i) if (_key[i] == vk) { a = (BindAction)i; return true; }
            a = BindAction.BasicAttack; return false;
        }

        /// <summary>예약 키: Esc, 방향키, Shift/Ctrl/Alt, 숫자패드 1~9(스킬 1~9 와 같이 동작), 0 이하.</summary>
        public static bool IsReserved(int vk)
        {
            if (vk <= 0 || vk > 255) return true;
            if (vk == 27 || (vk >= 37 && vk <= 40) || (vk >= 16 && vk <= 18)) return true;
            if (vk >= 97 && vk <= 105) return true;
            return false;
        }

        /// <summary>동작에 키를 묶는다. 예약 키면 Reserved(변화 없음), 다른 동작이 쓰던 키면 서로 맞바꾸고 Swapped.</summary>
        public BindResult Set(BindAction a, int vk, out BindAction swappedWith)
        {
            swappedWith = a;
            if (IsReserved(vk)) return BindResult.Reserved;
            BindAction other;
            if (TryActionFor(vk, out other))
            {
                if (other == a) return BindResult.Ok;
                _key[(int)other] = _key[(int)a];
                _key[(int)a] = vk;
                swappedWith = other;
                return BindResult.Swapped;
            }
            _key[(int)a] = vk;
            return BindResult.Ok;
        }

        /// <summary>키 이름(표시용). 흔한 키만 이름을 주고 나머지는 알파벳/숫자/F키 규칙으로 만든다.</summary>
        public static string KeyName(int vk)
        {
            if (vk >= 48 && vk <= 57) return ((char)vk).ToString();
            if (vk >= 65 && vk <= 90) return ((char)vk).ToString();
            if (vk >= 112 && vk <= 135) return "F" + (vk - 111);
            switch (vk)
            {
                case 8: return "Backspace"; case 9: return "Tab"; case 13: return "Enter"; case 32: return "Space"; case 27: return "Esc";
                case 33: return "PageUp"; case 34: return "PageDown"; case 35: return "End"; case 36: return "Home"; case 45: return "Insert"; case 46: return "Delete";
                case 186: return ";"; case 187: return "="; case 188: return ","; case 189: return "-"; case 190: return "."; case 191: return "/"; case 192: return "`";
                case 219: return "["; case 220: return "\\"; case 221: return "]"; case 222: return "'";
            }
            return "#" + vk;
        }

        // ------------------------------------------------------------------ 저장/복원

        public string ToJson()
        {
            StringBuilder sb = new StringBuilder("{");
            for (int i = 0; i < ActionCount; ++i)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("\"" + ((BindAction)i).ToString() + "\": " + _key[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.Append("}").ToString();
        }

        /// <summary>JSON 객체에서 읽는다. 없는 동작은 기본 키. 겹치거나 예약 키가 있으면(손으로 고친 파일) 전체를 기본값으로 되돌리고 false.</summary>
        public bool FromObject(Dictionary<string, object> o)
        {
            int[] k = new int[ActionCount];
            Array.Copy(Defaults, k, ActionCount);
            if (o != null)
                for (int i = 0; i < ActionCount; ++i)
                {
                    object v;
                    if (o.TryGetValue(((BindAction)i).ToString(), out v) && v is double) k[i] = (int)(double)v;
                }
            HashSet<int> seen = new HashSet<int>();
            for (int i = 0; i < ActionCount; ++i)
                if (IsReserved(k[i]) || !seen.Add(k[i])) { ResetAll(); return false; }
            Array.Copy(k, _key, ActionCount);
            return true;
        }
    }
}
