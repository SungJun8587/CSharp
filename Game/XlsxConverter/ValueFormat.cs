using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace XlsxConverter
{
    /// <summary>값 표기 규칙과 숫자 읽기.</summary>
    public static class ValueFormat
    {
        /// <summary>
        /// double을 Python repr(float)과 같은 최단 왕복 표기로 바꿉니다. (예: 5.0, 0.1, 1e-07, 1.5e+20)
        /// 정수 값도 ".0"을 붙여 실수임을 드러낸다.
        /// </summary>
        public static string FloatRepr(double v)
        {
            if (v == 0.0) return double.IsNegative(v) ? "-0.0" : "0.0";

            // .NET의 "R"은 최단 왕복 표기다. 가수 자릿수와 10의 지수로 풀어 다시 조립한다.
            string r = Math.Abs(v).ToString("R", CultureInfo.InvariantCulture);
            int exp = 0;
            int e = r.IndexOfAny(new[] { 'E', 'e' });
            string mant = r;
            if (e >= 0)
            {
                exp = int.Parse(r.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                mant = r.Substring(0, e);
            }
            int dot = mant.IndexOf('.');
            string intPart = dot < 0 ? mant : mant.Substring(0, dot);
            string fracPart = dot < 0 ? string.Empty : mant.Substring(dot + 1);
            string digits = intPart + fracPart;
            int pointPos = intPart.Length + exp;           // 소수점이 digits의 몇 번째 자리 앞에 오는가
            int lead = 0;
            while (lead < digits.Length - 1 && digits[lead] == '0') ++lead;
            digits = digits.Substring(lead);
            pointPos -= lead;
            digits = digits.TrimEnd('0');
            if (digits.Length == 0) digits = "0";
            int exp10 = pointPos - 1;

            var sb = new StringBuilder();
            if (v < 0) sb.Append('-');
            if (exp10 >= -4 && exp10 < 16)
            {
                if (exp10 >= 0)
                {
                    if (digits.Length <= exp10 + 1) sb.Append(digits).Append('0', exp10 + 1 - digits.Length).Append(".0");
                    else sb.Append(digits, 0, exp10 + 1).Append('.').Append(digits, exp10 + 1, digits.Length - exp10 - 1);
                }
                else sb.Append("0.").Append('0', -exp10 - 1).Append(digits);
            }
            else
            {
                sb.Append(digits[0]);
                if (digits.Length > 1) sb.Append('.').Append(digits, 1, digits.Length - 1);
                sb.Append('e').Append(exp10 < 0 ? '-' : '+').Append(Math.Abs(exp10).ToString("00", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 셀 값을 문자열로 바꿉니다. 정수 모양의 숫자는 정수로, 그 외 숫자는 FloatRepr로, 불리언은 True/False로 표기합니다.
        /// </summary>
        public static string CellToString(in Cell c)
        {
            if (c.Kind == CellKind.Bool) return c.Boolean ? "True" : "False";
            if (c.Kind == CellKind.Str) return c.Text;
            if (c.Text.IndexOfAny(new[] { '.', 'e', 'E' }) < 0) return c.Text;
            if (!TextUtil.ParseDouble(c.Text, out double v)) v = 0;
            return FloatRepr(v);
        }

        /// <summary>영문 대문자를 소문자로 바꿉니다.</summary>
        public static string Lower(string text) => AsciiChar.Lower(text);

        /// <summary>셀을 유한한 숫자로 읽습니다. (SheetConverter와 DefinitionSheets가 같은 규칙을 쓰도록 한곳에 둔다)</summary>
        public static bool ParseNumber(in Cell cell, out double value, out string why)
        {
            value = 0;
            why = string.Empty;
            if (cell.Kind == CellKind.Bool) { why = "숫자가 아님: " + CellToString(cell); return false; }

            if (cell.Kind == CellKind.Num) { if (!TextUtil.ParseDouble(cell.Text, out value)) value = 0; }
            else
            {
                // 0x1p3 같은 16진 표기는 엑셀 사용자가 의도한 숫자가 아니므로 ParseDouble이 받지 않는다.
                if (!TextUtil.ParseDouble(TextUtil.Trim(cell.Text), out value)) { value = 0; why = "숫자가 아님: " + TextUtil.Quote(cell.Text); return false; }
            }
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                why = "유한한 숫자가 아님: " + (cell.Kind == CellKind.Str ? TextUtil.Quote(cell.Text) : CellToString(cell));
                return false;
            }
            return true;
        }

        /// <summary>셀을 정수로 읽습니다. (uint32 기반 enum의 2^31 이상 값도 표현하기 위해 int64로 읽는다)</summary>
        public static bool ParseInt64(in Cell cell, out long value, out string why)
        {
            value = 0;
            if (!ParseNumber(cell, out double v, out why)) return false;
            if (v != Math.Floor(v)) { why = "정수가 아님: " + TextUtil.Trim(cell.Text); return false; }
            if (Math.Abs(v) > 9007199254740992.0) { why = "값이 너무 큼: " + TextUtil.Trim(cell.Text); return false; }   // 2^53. 이보다 크면 double이 정수를 정확히 표현하지 못한다
            value = (long)v;
            return true;
        }
    }
}
