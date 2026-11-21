//***************************************************************************
// UiWidget.cs : 위젯 기반 클래스, 창(UiWindow), 툴팁/끌어놓기 자료형.
// 좌표 규칙: 위젯의 Bounds 는 부모 왼쪽 위 기준이고, 포인터/그리기는 화면 절대 좌표를 쓴다(AbsBounds).
// 입력 이벤트는 가장 깊은 위젯부터 부모 쪽으로 올라가며 true 를 돌려준 위젯이 처리한 것으로 본다(누른 위젯이 놓을 때까지 포인터를 붙잡는다).
//***************************************************************************
using System;
using System.Collections.Generic;

namespace GameViewer.Core.Ui
{
    public sealed class UiContext
    {
        public long NowMs;
        public UiWidget Hover;          // 포인터 아래 위젯
        public UiWidget Pressed;        // 누르고 있는 위젯(포인터를 붙잡은 위젯)
        public float PointerX, PointerY;
    }

    public sealed class UiTipLine
    {
        public string Text; public int Argb; public float Size; public bool Wrap;
        public UiTipLine(string text, int argb, float size, bool wrap) { Text = text; Argb = argb; Size = size; Wrap = wrap; }
    }

    public sealed class UiTooltip
    {
        public readonly List<UiTipLine> Lines = new List<UiTipLine>();
        public UiTooltip Add(string text, int argb = UiTheme.Text, float size = 12, bool wrap = false) { Lines.Add(new UiTipLine(text, argb, size, wrap)); return this; }
    }

    /// <summary>끌고 있는 내용물. Source 는 시작한 위젯.</summary>
    public sealed class UiDragPayload
    {
        public UiWidget Source; public int Index; public uint ItemId; public uint Count; public UiIconKind Icon; public int Grade;
    }

    public interface IUiDropTarget
    {
        bool CanDrop(UiDragPayload p, float ax, float ay);
        void Drop(UiDragPayload p, float ax, float ay);
    }

    public abstract class UiWidget
    {
        public UiRect Bounds;
        public UiWidget Parent;
        public bool Visible = true;
        public bool Enabled = true;
        public string Name;
        public object Tag;
        private readonly List<UiWidget> _kids = new List<UiWidget>();

        public IList<UiWidget> Children { get { return _kids.AsReadOnly(); } }

        public T Add<T>(T w) where T : UiWidget
        {
            if (w.Parent != null) w.Parent.Remove(w);
            w.Parent = this; _kids.Add(w); return w;
        }

        public void Remove(UiWidget w) { if (_kids.Remove(w)) w.Parent = null; }
        public void Clear() { foreach (UiWidget k in _kids) k.Parent = null; _kids.Clear(); }

        public float AbsX { get { return Bounds.X + (Parent != null ? Parent.AbsX : 0); } }
        public float AbsY { get { return Bounds.Y + (Parent != null ? Parent.AbsY : 0); } }
        public UiRect AbsBounds { get { return new UiRect(AbsX, AbsY, Bounds.W, Bounds.H); } }

        public virtual UiManager Manager { get { return Parent != null ? Parent.Manager : null; } }

        /// <summary>이 위젯 자체가 포인터를 받는가(받지 않으면 클릭이 부모로 간다).</summary>
        public virtual bool HitSelf { get { return false; } }

        public virtual UiWidget HitTest(float ax, float ay)
        {
            if (!Visible || !AbsBounds.Contains(ax, ay)) return null;
            for (int i = _kids.Count - 1; i >= 0; --i)
            {
                UiWidget r = _kids[i].HitTest(ax, ay);
                if (r != null) return r;
            }
            return HitSelf ? this : null;
        }

        /// <summary>프레임마다 한 번(그리기 전에) 불린다. 부드러운 스크롤 같은 시간에 따른 갱신을 여기서 한다(그리기는 상태를 바꾸지 않는다).</summary>
        public virtual void OnUpdate(UiContext ctx) { }

        public void UpdateTree(UiContext ctx)
        {
            if (!Visible) return;
            OnUpdate(ctx);
            for (int i = 0; i < _kids.Count; ++i) _kids[i].UpdateTree(ctx);
        }

        public void Draw(IUiCanvas c, UiContext ctx)
        {
            if (!Visible) return;
            DrawSelf(c, ctx);
            for (int i = 0; i < _kids.Count; ++i) _kids[i].Draw(c, ctx);
            DrawOver(c, ctx);
        }

        protected virtual void DrawSelf(IUiCanvas c, UiContext ctx) { }
        /// <summary>자식을 그린 뒤 위에 덧그릴 것(스크롤바 등).</summary>
        protected virtual void DrawOver(IUiCanvas c, UiContext ctx) { }

        public virtual bool OnPointerDown(float ax, float ay, int button) { return false; }
        public virtual void OnPointerMove(float ax, float ay) { }
        public virtual void OnPointerUp(float ax, float ay, int button) { }
        public virtual bool OnWheel(float ax, float ay, int delta) { return false; }
        public virtual bool OnKeyDown(int vk) { return false; }

        /// <summary>끌어 놓기가 끝났을 때 시작한 위젯에게 알린다(dropped=false 면 받을 곳이 없었다).</summary>
        public virtual void OnDragEnd(UiDragPayload p, bool dropped, float ax, float ay) { }

        /// <summary>포인터 위치의 툴팁 키(없으면 null). 키가 바뀌면 지연 시간을 다시 센다. 싼 계산이어야 한다.</summary>
        public virtual object TooltipKey(float ax, float ay) { return null; }
        public virtual UiTooltip BuildTooltip(object key) { return null; }

        protected bool IsHover(UiContext ctx) { return ctx.Hover == this; }
        protected bool IsPressed(UiContext ctx) { return ctx.Pressed == this; }
    }

    /// <summary>제목줄이 있는 창. 제목줄을 끌어 옮기고, 오른쪽 위 X 로 닫는다. 자식은 창 왼쪽 위 기준으로 놓는다(내용은 TitleH 아래부터).</summary>
    public class UiWindow : UiWidget
    {
        public const float TitleH = 30;
        public string Id;
        public string Title;
        public bool Closable = true;
        public bool Draggable = true;
        public bool Modal;
        public int Accent = UiTheme.Accent;
        public long OpenedMs;
        private UiManager _manager;
        private readonly UiButton _close;

        public UiWindow(string id, string title, float w, float h)
        {
            Id = id; Title = title;
            Bounds = new UiRect(0, 0, w, h);
            _close = Add(new UiButton("✕", null) { Style = UiButtonStyle.Flat, Name = "close" });
            _close.Click = delegate { Close(); };
            PlaceClose();
        }

        public override UiManager Manager { get { return _manager; } }
        internal void SetManager(UiManager m) { _manager = m; }
        public override bool HitSelf { get { return true; } }

        public void SetSize(float w, float h) { Bounds = new UiRect(Bounds.X, Bounds.Y, w, h); PlaceClose(); }
        private void PlaceClose() { _close.Visible = Closable; _close.Bounds = new UiRect(Bounds.W - 28, 4, 22, 22); }

        public UiRect TitleRect { get { return new UiRect(AbsX, AbsY, Bounds.W, TitleH); } }
        /// <summary>창 안쪽(제목줄 아래) 영역. 자식 배치용 로컬 좌표.</summary>
        public UiRect Client { get { return new UiRect(0, TitleH, Bounds.W, Bounds.H - TitleH); } }

        /// <summary>열릴 때마다 한 번 불린다(데이터를 새로 읽는 곳).</summary>
        public virtual void OnOpen() { }
        public virtual void OnClose() { }
        public void Close() { UiManager m = _manager; if (m != null) m.Close(this); }

        public override bool OnPointerDown(float ax, float ay, int button) { return true; }       // 창 위의 클릭은 게임으로 새지 않는다

        protected override void DrawSelf(IUiCanvas c, UiContext ctx)
        {
            UiRect r = AbsBounds;
            c.Panel(r, Accent);
            UiRect tb = new UiRect(r.X + 3, r.Y + 3, r.W - 6, TitleH - 4);
            c.Gradient(tb, UiTheme.WithAlpha(Accent, 90), UiTheme.WithAlpha(Accent, 20), 6);
            c.Text(Title, new UiRect(r.X + 14, r.Y + 3, r.W - 50, TitleH - 4), new UiTextStyle(14, UiTheme.TextTitle, UiAlign.Left, true));
            c.Line(r.X + 6, r.Y + TitleH, r.Right - 6, r.Y + TitleH, UiTheme.WithAlpha(Accent, 140), 1);
        }
    }
}
