//***************************************************************************
// UiLists.cs : 데이터가 많은 위젯 - 슬롯 격자(가방/스킬 칸), 스크롤 목록(상점/우편 목록).
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameViewer.Core.Ui
{
    public struct UiSlotView
    {
        public uint ItemId;         // 0 이면 빈 칸
        public uint Count;
        public int Grade;
        public bool Locked;         // 아직 열리지 않은 칸
        public bool Dimmed;         // 탭 필터에서 제외된 칸(위치는 그대로 두고 어둡게)
        public UiIconKind Icon;     // None 이면 Item
        public bool Filled { get { return ItemId != 0; } }
    }

    public interface IUiSlotSource
    {
        int SlotCount { get; }
        UiSlotView GetSlot(int index);
        UiTooltip GetSlotTooltip(int index);
    }

    /// <summary>칸 격자. 클릭=선택, 더블클릭/우클릭=사용, 끌어 놓기=이동/교환. 끌어 놓기를 받을 칸은 Dropped 로 알린다.</summary>
    public sealed class UiSlotGrid : UiWidget, IUiDropTarget
    {
        public int Columns = 6;
        public float Cell = 48, Gap = 4;
        public IUiSlotSource Source;
        public int Selected = -1;
        public Action<int> SelectChanged;
        public Action<int> Activate;
        public Action<UiDragPayload, int> Dropped;
        public Action<UiDragPayload> DroppedOutside;
        public const float DragThreshold = 6;
        public const long DoubleClickMs = 350;
        private int _downIdx = -1;
        private float _downX, _downY;
        private int _lastClickIdx = -1;
        private long _lastClickMs = -10000;

        public UiSlotGrid(float x, float y, int columns, IUiSlotSource source)
        {
            Columns = columns; Source = source;
            Bounds = new UiRect(x, y, 0, 0);
            Fit();
        }

        public int Rows { get { return Source == null ? 0 : (Source.SlotCount + Columns - 1) / Columns; } }

        /// <summary>칸 수에 맞춰 크기를 다시 잡는다.</summary>
        public void Fit()
        {
            int rows = Rows;
            Bounds = new UiRect(Bounds.X, Bounds.Y, Columns * Cell + (Columns - 1) * Gap, Math.Max(0, rows * Cell + (rows - 1) * Gap));
        }

        public override bool HitSelf { get { return true; } }

        public UiRect SlotRect(int i)
        {
            int cx = i % Columns, cy = i / Columns;
            return new UiRect(AbsX + cx * (Cell + Gap), AbsY + cy * (Cell + Gap), Cell, Cell);
        }

        public int SlotAt(float ax, float ay)
        {
            if (Source == null) return -1;
            float lx = ax - AbsX, ly = ay - AbsY;
            if (lx < 0 || ly < 0) return -1;
            float step = Cell + Gap;
            int cx = (int)(lx / step), cy = (int)(ly / step);
            if (cx >= Columns || lx - cx * step >= Cell || ly - cy * step >= Cell) return -1;
            int i = cy * Columns + cx;
            return i < Source.SlotCount ? i : -1;
        }

        private long Now { get { UiManager m = Manager; return m != null ? m.Ctx.NowMs : 0; } }

        public void SelectSlot(int i)
        {
            if (i == Selected) return;
            Selected = i;
            if (SelectChanged != null) SelectChanged(i);
        }

        public override bool OnPointerDown(float ax, float ay, int button)
        {
            int i = SlotAt(ax, ay);
            if (i < 0) return false;
            UiSlotView v = Source.GetSlot(i);
            if (v.Locked) return true;
            if (button == 1) { SelectSlot(i); if (v.Filled && Activate != null) Activate(i); return true; }
            if (button != 0) return false;
            SelectSlot(i);
            long now = Now;
            if (v.Filled && i == _lastClickIdx && now - _lastClickMs <= DoubleClickMs && Activate != null) { _lastClickIdx = -1; Activate(i); }
            else { _lastClickIdx = i; _lastClickMs = now; }
            _downIdx = v.Filled ? i : -1; _downX = ax; _downY = ay;
            return true;
        }

        public override void OnPointerMove(float ax, float ay)
        {
            if (_downIdx < 0) return;
            float dx = ax - _downX, dy = ay - _downY;
            if (dx * dx + dy * dy < DragThreshold * DragThreshold) return;
            UiManager m = Manager;
            UiSlotView v = Source.GetSlot(_downIdx);
            if (m != null && v.Filled)
                m.BeginDrag(this, new UiDragPayload { Source = this, Index = _downIdx, ItemId = v.ItemId, Count = v.Count, Icon = v.Icon == UiIconKind.None ? UiIconKind.Item : v.Icon, Grade = v.Grade });
            _downIdx = -1;
        }

        public override void OnPointerUp(float ax, float ay, int button) { _downIdx = -1; }

        public bool CanDrop(UiDragPayload p, float ax, float ay)
        {
            int i = SlotAt(ax, ay);
            return i >= 0 && !Source.GetSlot(i).Locked;
        }

        public void Drop(UiDragPayload p, float ax, float ay)
        {
            int i = SlotAt(ax, ay);
            if (i >= 0 && Dropped != null) Dropped(p, i);
        }

        public override void OnDragEnd(UiDragPayload p, bool dropped, float ax, float ay)
        {
            if (!dropped && DroppedOutside != null) DroppedOutside(p);
        }

        public override object TooltipKey(float ax, float ay)
        {
            int i = SlotAt(ax, ay);
            return i >= 0 && Source.GetSlot(i).Filled ? (object)i : null;
        }

        public override UiTooltip BuildTooltip(object key) { return Source.GetSlotTooltip((int)key); }

        protected override void DrawSelf(IUiCanvas c, UiContext ctx)
        {
            if (Source == null) return;
            UiManager m = Manager;
            UiDragPayload drag = m != null ? m.Drag : null;
            int hover = IsHover(ctx) ? SlotAt(ctx.PointerX, ctx.PointerY) : -1;
            int n = Source.SlotCount;
            for (int i = 0; i < n; ++i)
            {
                UiRect r = SlotRect(i);
                UiSlotView v = Source.GetSlot(i);
                c.Fill(r, UiTheme.SlotBg, 5);
                int border = v.Filled ? UiTheme.Grade(v.Grade) : UiTheme.SlotBorder;
                if (v.Filled) c.Gradient(r.Inset(1, 1, 1, 1), UiTheme.WithAlpha(border, 56), UiTheme.WithAlpha(border, 8), 4);
                c.Stroke(r, v.Filled ? UiTheme.WithAlpha(border, 200) : border, 1.5f, 5);
                if (v.Filled)
                {
                    bool beingDragged = drag != null && drag.Source == this && drag.Index == i;
                    c.Icon(v.Icon == UiIconKind.None ? UiIconKind.Item : v.Icon, v.ItemId, r.Inset(5, 5, 5, 5), v.Grade);
                    if (v.Count > 1)
                    {
                        string s = v.Count.ToString(CultureInfo.InvariantCulture);
                        UiRect t = new UiRect(r.X, r.Bottom - 17, r.W - 4, 15);
                        c.Text(s, t.Offset(1, 1), new UiTextStyle(11, unchecked((int)0xFF000000), UiAlign.Right, true));
                        c.Text(s, t, new UiTextStyle(11, unchecked((int)0xFFFFFFFF), UiAlign.Right, true));
                    }
                    if (beingDragged) c.Fill(r, unchecked((int)0xB0000000), 5);
                }
                if (v.Locked)
                {
                    c.Fill(r, unchecked((int)0xC0000000), 5);
                    c.Line(r.X + 14, r.Y + 14, r.Right - 14, r.Bottom - 14, UiTheme.Disabled, 2);
                    c.Line(r.Right - 14, r.Y + 14, r.X + 14, r.Bottom - 14, UiTheme.Disabled, 2);
                }
                else if (v.Dimmed) c.Fill(r, unchecked((int)0x98000000), 5);
                if (i == hover && !v.Locked) c.Fill(r, UiTheme.Hover, 5);
                if (i == Selected) c.Stroke(r.Inflate(0.5f), UiTheme.Select, 2.2f, 5);
                // 끌어 온 것을 놓을 수 있는 칸 표시
                if (drag != null && i == hover && !v.Locked) c.Stroke(r.Inflate(0.5f), UiTheme.Good, 2.2f, 5);
            }
        }
    }

    /// <summary>세로 스크롤 목록. 행 그리기는 DrawRow 가 한다. 휠, 스크롤바 끌기, 부드러운 스크롤을 지원한다.</summary>
    public sealed class UiScrollList : UiWidget
    {
        public float RowHeight = 40;
        public Func<int> Count;
        public Action<IUiCanvas, int, UiRect, bool, bool> DrawRow;        // (canvas, 행 번호, 영역, 선택됨, 마우스 올림)
        public int Selected = -1;
        public Action<int> SelectChanged;
        public Action<int> Activate;
        public Func<int, UiTooltip> RowTooltip;
        /// <summary>true 면 행 배경/선택/올림 표시 없이 내용만 그리고 스크롤만 한다(본문 읽기).</summary>
        public bool Plain;
        public float Scroll;                                               // 현재 위 오프셋(px)
        private float _target;
        private long _lastMs;
        private bool _thumbDrag;
        private float _thumbGrab;
        private int _lastClickRow = -1;
        private long _lastClickMs = -10000;
        public const float BarW = 8;

        public UiScrollList(UiRect bounds, float rowHeight, Func<int> count)
        {
            Bounds = bounds; RowHeight = rowHeight; Count = count;
        }

        public override bool HitSelf { get { return true; } }
        private int N { get { return Count != null ? Count() : 0; } }
        public float MaxScroll { get { return Math.Max(0, N * RowHeight - Bounds.H); } }
        private long Now { get { UiManager m = Manager; return m != null ? m.Ctx.NowMs : 0; } }

        public void ScrollTo(float y, bool immediate = false)
        {
            _target = Math.Max(0, Math.Min(MaxScroll, y));
            if (immediate) Scroll = _target;
        }

        public void EnsureVisible(int row)
        {
            if (row < 0) return;
            float top = row * RowHeight, bot = top + RowHeight;
            if (top < _target) ScrollTo(top);
            else if (bot > _target + Bounds.H) ScrollTo(bot - Bounds.H);
        }

        public void SelectRow(int i, bool ensureVisible = true)
        {
            int n = N;
            if (i >= n) i = n - 1;
            if (i < -1) i = -1;
            if (i == Selected) return;
            Selected = i;
            if (ensureVisible) EnsureVisible(i);
            if (SelectChanged != null) SelectChanged(i);
        }

        public int RowAt(float ay)
        {
            float ly = ay - AbsY + Scroll;
            if (ly < 0) return -1;
            int i = (int)(ly / RowHeight);
            return i < N ? i : -1;
        }

        private bool HasBar { get { return MaxScroll > 0; } }

        private UiRect Thumb()
        {
            UiRect r = AbsBounds;
            float total = Math.Max(1, N * RowHeight);
            float th = Math.Max(24, r.H * r.H / total);
            float f = MaxScroll > 0 ? Scroll / MaxScroll : 0;
            return new UiRect(r.Right - BarW, r.Y + (r.H - th) * f, BarW, th);
        }

        public override bool OnPointerDown(float ax, float ay, int button)
        {
            if (button != 0) return false;
            UiRect r = AbsBounds;
            if (HasBar && ax >= r.Right - BarW - 2)
            {
                UiRect t = Thumb();
                if (t.Contains(ax, ay)) { _thumbDrag = true; _thumbGrab = ay - t.Y; }
                else ScrollTo(_target + (ay < t.Y ? -r.H : r.H));            // 트랙을 누르면 한 쪽씩
                return true;
            }
            if (Plain) return true;
            int row = RowAt(ay);
            if (row < 0) return true;
            SelectRow(row, false);
            long now = Now;
            if (row == _lastClickRow && now - _lastClickMs <= UiSlotGrid.DoubleClickMs && Activate != null) { _lastClickRow = -1; Activate(row); }
            else { _lastClickRow = row; _lastClickMs = now; }
            return true;
        }

        public override void OnPointerMove(float ax, float ay)
        {
            if (!_thumbDrag) return;
            UiRect r = AbsBounds, t = Thumb();
            float span = r.H - t.H;
            if (span <= 0) return;
            float f = (ay - _thumbGrab - r.Y) / span;
            ScrollTo(Math.Max(0, Math.Min(1, f)) * MaxScroll, true);
        }

        public override void OnPointerUp(float ax, float ay, int button) { _thumbDrag = false; }

        public override bool OnWheel(float ax, float ay, int delta)
        {
            if (MaxScroll <= 0) return false;
            ScrollTo(_target - delta / 120f * RowHeight * 1.5f);
            return true;
        }

        public override object TooltipKey(float ax, float ay)
        {
            if (RowTooltip == null) return null;
            int row = RowAt(ay);
            return row >= 0 && ax < AbsX + Bounds.W - BarW - 2 ? (object)row : null;
        }
        public override UiTooltip BuildTooltip(object key) { return RowTooltip != null ? RowTooltip((int)key) : null; }

        public override void OnUpdate(UiContext ctx)
        {
            long now = ctx.NowMs;
            float max = MaxScroll;
            if (_target > max) _target = max;
            if (_thumbDrag) _target = Scroll;
            else
            {
                float dt = _lastMs == 0 ? 16 : Math.Min(100, now - _lastMs);
                float k = 1 - (float)Math.Pow(0.5, dt / 45.0);                // 반감기 45ms 의 부드러운 따라가기
                Scroll += (_target - Scroll) * k;
                if (Math.Abs(_target - Scroll) < 0.3f) Scroll = _target;
            }
            _lastMs = now;
        }

        protected override void DrawSelf(IUiCanvas c, UiContext ctx)
        {
            UiRect r = AbsBounds;
            int n = N;
            c.PushClip(r);
            int hover = IsHover(ctx) ? RowAt(ctx.PointerY) : -1;
            if (hover >= 0 && ctx.PointerX >= r.Right - BarW - 2 && HasBar) hover = -1;
            int first = Math.Max(0, (int)(Scroll / RowHeight));
            int last = Math.Min(n - 1, (int)((Scroll + r.H) / RowHeight));
            for (int i = first; i <= last; ++i)
            {
                UiRect row = new UiRect(r.X, r.Y + i * RowHeight - Scroll, r.W - (HasBar ? BarW + 2 : 0), RowHeight - 2);
                bool sel = !Plain && i == Selected;
                if (!Plain) c.Fill(row, sel ? UiTheme.WithAlpha(UiTheme.Accent, 70) : ((i & 1) == 0 ? UiTheme.Row : UiTheme.RowAlt), 4);
                if (!Plain && i == hover && !sel) c.Fill(row, UiTheme.Hover, 4);
                if (sel) c.Stroke(row, UiTheme.WithAlpha(UiTheme.Gold, 210), 1.5f, 4);
                if (DrawRow != null) DrawRow(c, i, row, sel, i == hover);
            }
            c.PopClip();
            if (HasBar)
            {
                c.Fill(new UiRect(r.Right - BarW, r.Y, BarW, r.H), unchecked((int)0x30000000), 4);
                UiRect t = Thumb();
                c.Fill(t, _thumbDrag ? UiTheme.Gold : unchecked((int)0xFF8A8F96), 4);
            }
        }
    }
}
