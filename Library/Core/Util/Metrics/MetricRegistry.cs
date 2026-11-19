// MetricRegistry.cs : 이름으로 지표를 모으고 Prometheus 텍스트로 내보내는 저장소.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Core.Util;

/// <summary>
/// 이름으로 지표를 모아 두는 저장소. 같은 이름을 다시 요청하면 같은 객체를 준다(핫패스에서는 한 번 받아 필드에 보관한다).
/// <see cref="ExportPrometheus"/>로 Prometheus 텍스트 형식을 만들 수 있다.
/// </summary>
public sealed class MetricRegistry
{
    private enum Kind { Counter, Gauge, Histogram }

    private sealed class Item
    {
        public Kind Kind;
        public string Help = "";
        public object Metric = null!;
    }

    private readonly ConcurrentDictionary<string, Item> _items = new ConcurrentDictionary<string, Item>(StringComparer.Ordinal);

    public static MetricRegistry Default { get; } = new MetricRegistry();

    public Counter Counter(string name, string help = "") => (Counter)Get(name, Kind.Counter, help, () => new Counter());
    public Gauge Gauge(string name, string help = "") => (Gauge)Get(name, Kind.Gauge, help, () => new Gauge());
    public Histogram Histogram(string name, string help = "", double[]? bounds = null) => (Histogram)Get(name, Kind.Histogram, help, () => new Histogram(bounds));

    private object Get(string name, Kind kind, string help, Func<object> make)
    {
        if (!IsValidName(name))
            throw new ArgumentException("지표 이름은 [a-zA-Z_:][a-zA-Z0-9_:]* 이어야 한다: " + name, nameof(name));
        Item item = _items.GetOrAdd(name, _ => new Item { Kind = kind, Help = help, Metric = make() });
        if (item.Kind != kind)
            throw new InvalidOperationException("지표 '" + name + "'은 이미 다른 종류로 등록돼 있다");
        return item.Metric;
    }

    private static bool IsValidName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        for (int i = 0; i < name.Length; ++i)
        {
            char c = name[i];
            bool ok = c == '_' || c == ':' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (i > 0 && c >= '0' && c <= '9');
            if (!ok) return false;
        }
        return true;
    }

    private static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Prometheus 텍스트 노출 형식(이름순).</summary>
    public string ExportPrometheus()
    {
        var names = new List<string>(_items.Keys);
        names.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (string name in names)
        {
            Item item = _items[name];
            if (item.Help.Length > 0)
                sb.Append("# HELP ").Append(name).Append(' ').Append(item.Help.Replace("\\", "\\\\").Replace("\n", "\\n")).Append('\n');
            switch (item.Kind)
            {
                case Kind.Counter:
                    sb.Append("# TYPE ").Append(name).Append(" counter\n");
                    sb.Append(name).Append(' ').Append(((Counter)item.Metric).Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
                    break;
                case Kind.Gauge:
                    sb.Append("# TYPE ").Append(name).Append(" gauge\n");
                    sb.Append(name).Append(' ').Append(Num(((Gauge)item.Metric).Value)).Append('\n');
                    break;
                default:
                    {
                        var h = (Histogram)item.Metric;
                        sb.Append("# TYPE ").Append(name).Append(" histogram\n");
                        long[] snap = h.Snapshot();
                        long cum = 0;
                        for (int i = 0; i < snap.Length; ++i)
                        {
                            cum += snap[i];
                            string le = i < h.Bounds.Count ? Num(h.Bounds[i]) : "+Inf";
                            sb.Append(name).Append("_bucket{le=\"").Append(le).Append("\"} ").Append(cum.ToString(CultureInfo.InvariantCulture)).Append('\n');
                        }
                        sb.Append(name).Append("_sum ").Append(Num(h.Sum)).Append('\n');
                        sb.Append(name).Append("_count ").Append(cum.ToString(CultureInfo.InvariantCulture)).Append('\n');
                        break;
                    }
            }
        }
        return sb.ToString();
    }
}
