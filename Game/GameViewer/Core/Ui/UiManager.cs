//***************************************************************************
// UiManager.cs : 창 목록, 포커스/앞뒤 순서, 입력 전달, 창 끌기, 아이템 끌어 놓기, 툴팁, 알림(토스트)을 맡는다.
// 게임 입력(GameCanvas)은 먼저 이 관리자에게 넘기고, true 를 돌려받으면 게임은 그 입력을 쓰지 않는다.
//   PointerDown/Up/Wheel: 창 위에서 일어났으면 true.   PointerMove: 포인터가 창 위에 있으면 true.
//   KeyDown: 맨 위 창이 처리했거나(Esc 로 닫기 포함) 모달 창이 있으면 true.
//***************************************************************************
using System;
using System.Collections.Generic;

namespace GameViewer.Core.Ui
{
    public struct UiPoint { public float X, Y; public UiPoint(float x, float y) { X = x; Y = y; } }

    public sealed class UiManager
    {
        public const long TipDelayMs = 280;
        public const long OpenAnimMs = 110;
        public const long ToastMs = 2600;
        public const int MaxToasts = 4;

        public float Width = 1280, Height = 720;
        public readonly UiContext Ctx = new UiContext();
        /// <summary>창 id 별로 기억한 위치(창을 끌어 옮긴 뒤 다시 열면 그 자리에 열린다). 설정 파일에 저장해 둘 수 있다.</summary>
        public readonly Dictionary<string, UiPoint> WindowPositions = new Dictionary<string, UiPoint>();
        public event Action<UiWindow, bool> WindowToggled;       // (창, 열림 여부) - 효과음 등을 붙인다

        private readonly List<UiWindow> _wins = new List<UiWindow>();             // 뒤 -> 앞
        private readonly Dictionary<string, Func<UiWindow>> _factories = new Dictionary<string, Func<UiWindow>>();
        private UiWidget _capture;
        private UiWindow _dragWin;
        private float _dragDx, _dragDy;
        private UiDragPayload _drag;
        private object _tipKey;
        private long _tipSince;
        private UiTooltip _tip;
        private UiWidget _tipWidget;
        private readonly List<ToastItem> _toasts = new List<ToastItem>();

        private sealed class ToastItem { public string Text; public int Argb; public long Start; }

        public UiDragPayload Drag { get { return _drag; } }
        public bool AnyOpen { get { return _wins.Count > 0; } }
        public int OpenCount { get { return _wins.Count; } }
        public IList<UiWindow> Windows { get { return _wins.AsReadOnly(); } }

        public void Resize(float w, float h)
        {
            Width = w; Height = h;
            for (int i = 0; i < _wins.Count; ++i) Clamp(_wins[i]);
        }

        // ------------------------------------------------------------------ 창 열고 닫기

        public void Register(string id, Func<UiWindow> factory) { _factories[id] = factory; }

        public UiWindow Find(string id)
        {
            for (int i = 0; i < _wins.Count; ++i) if (_wins[i].Id == id) return _wins[i];
            return null;
        }

        public bool IsOpen(string id) { return Find(id) != null; }

        /// <summary>등록된 창을 연다. 이미 열려 있으면 맨 앞으로 올린다.</summary>
        public UiWindow Open(string id)
        {
            UiWindow w = Find(id);
            if (w != null) { BringToFront(w); return w; }
            Func<UiWindow> f;
            if (!_factories.TryGetValue(id, out f)) return null;
            return Show(f());
        }

        /// <summary>만들어 둔 창을 연다(대화상자 등).</summary>
        public UiWindow Show(UiWindow w)
        {
            UiWindow old = Find(w.Id);
            if (old != null) Close(old);
            w.SetManager(this);
            w.OpenedMs = Ctx.NowMs;
            UiPoint p;
            if (WindowPositions.TryGetValue(w.Id, out p) && !w.Modal) w.Bounds = new UiRect(p.X, p.Y, w.Bounds.W, w.Bounds.H);
            else
            {
                float off = Math.Min(5, _wins.Count) * 26;                // 겹쳐 열릴 때 조금씩 비켜 놓는다
                w.Bounds = new UiRect((Width - w.Bounds.W) / 2 + (w.Modal ? 0 : off - 40), (Height - w.Bounds.H) / 2 + (w.Modal ? 0 : off - 40), w.Bounds.W, w.Bounds.H);
            }
            Clamp(w);
            _wins.Add(w);
            w.OnOpen();
            if (WindowToggled != null) WindowToggled(w, true);
            return w;
        }

        public void Close(string id) { UiWindow w = Find(id); if (w != null) Close(w); }

        public void Close(UiWindow w)
        {
            if (!_wins.Remove(w)) return;
            if (_capture != null && IsInside(_capture, w)) { _capture = null; Ctx.Pressed = null; }
            if (_dragWin == w) _dragWin = null;
            if (_drag != null && IsInside(_drag.Source, w)) _drag = null;
            if (Ctx.Hover != null && IsInside(Ctx.Hover, w)) Ctx.Hover = null;
            if (!w.Modal) WindowPositions[w.Id] = new UiPoint(w.Bounds.X, w.Bounds.Y);
            w.OnClose();
            w.SetManager(null);
            if (WindowToggled != null) WindowToggled(w, false);
        }

        /// <summary>열려 있으면 닫고, 닫혀 있으면 연다. 닫은 경우 false.</summary>
        public bool Toggle(string id)
        {
            UiWindow w = Find(id);
            if (w != null) { Close(w); return false; }
            return Open(id) != null;
        }

        public void CloseAll() { for (int i = _wins.Count - 1; i >= 0; --i) Close(_wins[i]); }

        public void BringToFront(UiWindow w)
        {
            int i = _wins.IndexOf(w);
            if (i < 0 || i == _wins.Count - 1) return;
            _wins.RemoveAt(i); _wins.Add(w);
        }

        private static bool IsInside(UiWidget w, UiWindow win)
        {
            for (UiWidget p = w; p != null; p = p.Parent) if (p == win) return true;
            return false;
        }

        private void Clamp(UiWindow w)
        {
            float x = w.Bounds.X, y = w.Bounds.Y;
            x = Math.Max(-w.Bounds.W + 80, Math.Min(Width - 80, x));            // 제목줄이 화면 안에 조금은 남게
            y = Math.Max(0, Math.Min(Height - UiWindow.TitleH, y));
            w.Bounds = new UiRect(x, y, w.Bounds.W, w.Bounds.H);
        }

        /// <summary>이 좌표가 열린 창 위인가(게임 월드를 가리키는지 판단할 때).</summary>
        public bool IsOverWindow(float x, float y) { return WindowAt(x, y) != null; }

        private UiWindow Modal()
        {
            for (int i = _wins.Count - 1; i >= 0; --i) if (_wins[i].Modal) return _wins[i];
            return null;
        }

        private UiWindow WindowAt(float x, float y)
        {
            for (int i = _wins.Count - 1; i >= 0; --i) if (_wins[i].AbsBounds.Contains(x, y)) return _wins[i];
            return null;
        }

        // ------------------------------------------------------------------ 입력

        /// <summary>화면 한쪽의 창 열기 단추 줄(클릭으로 창을 연다).</summary>
        public readonly UiDock Dock = new UiDock();

        public bool PointerDown(float x, float y, int button)
        {
            Ctx.PointerX = x; Ctx.PointerY = y;
            UiWindow modal = Modal();
            UiWindow top = WindowAt(x, y);
            if (modal != null && top != modal) return true;                   // 모달 밖 클릭은 삼킨다
            if (top == null)
            {
                int d = button == 0 ? Dock.IndexAt(x, y, Width) : -1;
                if (d >= 0) { Dock.Down = d; return true; }
                return Dock.IndexAt(x, y, Width) >= 0;
            }
            BringToFront(top);
            UiWidget target = top.HitTest(x, y) ?? top;
            for (UiWidget w = target; w != null; w = w.Parent)
            {
                if (!w.Enabled) continue;
                if (w.OnPointerDown(x, y, button))
                {
                    _capture = w; Ctx.Pressed = w;
                    if (w == top && top.Draggable && !top.Modal && button == 0 && top.TitleRect.Contains(x, y))
                    {
                        _dragWin = top; _dragDx = x - top.Bounds.X; _dragDy = y - top.Bounds.Y;
                    }
                    break;
                }
            }
            return true;
        }

        public bool PointerMove(float x, float y)
        {
            Ctx.PointerX = x; Ctx.PointerY = y;
            if (_dragWin != null)
            {
                _dragWin.Bounds = new UiRect(x - _dragDx, y - _dragDy, _dragWin.Bounds.W, _dragWin.Bounds.H);
                Clamp(_dragWin);
                return true;
            }
            if (_capture != null) _capture.OnPointerMove(x, y);
            UiWindow modal = Modal();
            UiWindow top = WindowAt(x, y);
            if (modal != null && top != modal) top = null;
            Ctx.Hover = top == null ? null : (top.HitTest(x, y) ?? top);
            Dock.Hover = top == null && modal == null && _drag == null ? Dock.IndexAt(x, y, Width) : -1;
            return top != null || _drag != null || _capture != null || Dock.Hover >= 0 || Dock.Down >= 0;
        }

        public bool PointerUp(float x, float y, int button)
        {
            Ctx.PointerX = x; Ctx.PointerY = y;
            if (_dragWin != null)
            {
                UiWindow w = _dragWin; _dragWin = null; _capture = null; Ctx.Pressed = null;
                WindowPositions[w.Id] = new UiPoint(w.Bounds.X, w.Bounds.Y);
                return true;
            }
            if (Dock.Down >= 0)
            {
                int d = Dock.Down; Dock.Down = -1;
                if (Modal() == null && WindowAt(x, y) == null && Dock.IndexAt(x, y, Width) == d && Dock.Items[d].Click != null) Dock.Items[d].Click();
                return true;
            }
            UiWidget cap = _capture;
            if (_drag != null)
            {
                UiDragPayload p = _drag; _drag = null;
                bool dropped = false;
                UiWindow top = WindowAt(x, y);
                UiWindow modal = Modal();
                if (top != null && (modal == null || top == modal))
                {
                    for (UiWidget w = top.HitTest(x, y) ?? top; w != null && !dropped; w = w.Parent)
                    {
                        IUiDropTarget t = w as IUiDropTarget;
                        if (t != null && w.Enabled && t.CanDrop(p, x, y)) { t.Drop(p, x, y); dropped = true; }
                    }
                }
                _capture = null; Ctx.Pressed = null;
                if (p.Source != null) p.Source.OnDragEnd(p, dropped, x, y);
                return true;
            }
            if (cap != null)
            {
                _capture = null; Ctx.Pressed = null;
                cap.OnPointerUp(x, y, button);
                return true;
            }
            return WindowAt(x, y) != null || Modal() != null;
        }

        public bool Wheel(float x, float y, int delta)
        {
            UiWindow modal = Modal();
            UiWindow top = WindowAt(x, y);
            if (modal != null && top != modal) return true;
            if (top == null) return false;
            for (UiWidget w = top.HitTest(x, y) ?? top; w != null; w = w.Parent)
                if (w.Enabled && w.OnWheel(x, y, delta)) break;
            return true;
        }

        public bool KeyDown(int vk)
        {
            if (_wins.Count == 0) return false;
            UiWindow top = _wins[_wins.Count - 1];
            UiWindow modal = Modal();
            if (modal != null) top = modal;
            if (_drag != null && vk == 27) { CancelDrag(); return true; }
            // 포커스된 창의 자식(탭 등)에게도 키를 보인다
            if (OfferKey(top, vk)) return true;
            if (vk == 27 && top.Closable) { Close(top); return true; }
            return modal != null;
        }

        private bool OfferKey(UiWidget w, int vk)
        {
            if (!w.Enabled || !w.Visible) return false;
            IList<UiWidget> kids = w.Children;
            for (int i = kids.Count - 1; i >= 0; --i) if (OfferKey(kids[i], vk)) return true;
            return w.OnKeyDown(vk);
        }

        // ------------------------------------------------------------------ 끌어 놓기

        public void BeginDrag(UiWidget source, UiDragPayload p)
        {
            _drag = p;
        }

        public void CancelDrag()
        {
            if (_drag == null) return;
            UiDragPayload p = _drag; _drag = null; _capture = null; Ctx.Pressed = null;
            if (p.Source != null) p.Source.OnDragEnd(p, true, Ctx.PointerX, Ctx.PointerY);       // 취소는 "밖에 놓음"이 아니다
        }

        // ------------------------------------------------------------------ 갱신/그리기

        public void Toast(string text, int argb = UiTheme.Text)
        {
            if (_toasts.Count >= MaxToasts) _toasts.RemoveAt(0);
            _toasts.Add(new ToastItem { Text = text, Argb = argb, Start = Ctx.NowMs });
        }

        public int ToastCount { get { return _toasts.Count; } }

        public void Update(long nowMs)
        {
            Ctx.NowMs = nowMs;
            for (int i = 0; i < _wins.Count; ++i) _wins[i].UpdateTree(Ctx);
            for (int i = _toasts.Count - 1; i >= 0; --i) if (nowMs - _toasts[i].Start > ToastMs) _toasts.RemoveAt(i);

            UiWidget h = Ctx.Hover;
            object key = null;
            if (h != null && _drag == null && _capture == null && _dragWin == null) key = h.TooltipKey(Ctx.PointerX, Ctx.PointerY);
            object full = key == null ? null : new KeyValuePair<UiWidget, object>(h, key);
            if (!Equals(full, _tipKey) || (h != _tipWidget))
            {
                _tipKey = full; _tipSince = nowMs; _tip = null; _tipWidget = h;
            }
            if (key != null && _tip == null && nowMs - _tipSince >= TipDelayMs) _tip = h.BuildTooltip(key);
            if (key == null) _tip = null;
        }

        public UiTooltip CurrentTooltip { get { return _tip; } }

        public void Draw(IUiCanvas c)
        {
            long now = Ctx.NowMs;
            Dock.Draw(c, Width);
            for (int i = 0; i < _wins.Count; ++i)
            {
                UiWindow w = _wins[i];
                if (w.Modal) c.Fill(new UiRect(0, 0, Width, Height), UiTheme.Backdrop);
                float t = Math.Min(1f, (now - w.OpenedMs) / (float)OpenAnimMs);
                bool anim = t < 1f;
                if (anim)
                {
                    float e = 1 - (1 - t) * (1 - t);
                    UiRect r = w.AbsBounds;
                    c.PushScale(0.94f + 0.06f * e, r.CenterX, r.CenterY);
                }
                w.Draw(c, Ctx);
                if (anim) c.PopScale();
            }
            DrawDragGhost(c);
            DrawTooltip(c);
            DrawToasts(c);
        }

        private void DrawDragGhost(IUiCanvas c)
        {
            if (_drag == null) return;
            UiRect r = new UiRect(Ctx.PointerX - 22, Ctx.PointerY - 22, 44, 44);
            c.Fill(r, unchecked((int)0xB0101418), 6);
            c.Stroke(r, UiTheme.WithAlpha(UiTheme.Grade(_drag.Grade), 230), 2, 6);
            c.Icon(_drag.Icon, _drag.ItemId, r.Inset(5, 5, 5, 5), _drag.Grade);
        }

        private void DrawTooltip(IUiCanvas c)
        {
            UiTooltip tip = _tip;
            if (tip == null || tip.Lines.Count == 0) return;
            const float pad = 9, maxW = 260;
            float w = 0, h = 0;
            float[] hs = new float[tip.Lines.Count];
            for (int i = 0; i < tip.Lines.Count; ++i)
            {
                UiTipLine l = tip.Lines[i];
                float lw, lh;
                c.Measure(l.Text, new UiTextStyle(l.Size, l.Argb, UiAlign.Left, i == 0, l.Wrap), maxW, out lw, out lh);
                if (lw > maxW) lw = maxW;
                w = Math.Max(w, lw); hs[i] = lh + 2; h += hs[i];
            }
            w += pad * 2; h += pad * 2 - 2;
            float x = Ctx.PointerX + 18, y = Ctx.PointerY + 20;
            if (x + w > Width - 4) x = Ctx.PointerX - w - 12;
            if (y + h > Height - 4) y = Ctx.PointerY - h - 12;
            x = Math.Max(4, x); y = Math.Max(4, y);
            UiRect r = new UiRect(x, y, w, h);
            c.Fill(r.Offset(3, 4), unchecked((int)0x60000000), 8);
            c.Panel(r, UiTheme.Accent);
            float cy = y + pad;
            for (int i = 0; i < tip.Lines.Count; ++i)
            {
                UiTipLine l = tip.Lines[i];
                c.Text(l.Text, new UiRect(x + pad, cy, w - pad * 2 + 2, hs[i]), new UiTextStyle(l.Size, l.Argb, UiAlign.Left, i == 0, l.Wrap));
                cy += hs[i];
            }
        }

        private void DrawToasts(IUiCanvas c)
        {
            float y = Height - 150;
            for (int i = _toasts.Count - 1; i >= 0; --i)
            {
                ToastItem t = _toasts[i];
                long age = Ctx.NowMs - t.Start;
                float a = age < 150 ? age / 150f : (age > ToastMs - 400 ? Math.Max(0, (ToastMs - age) / 400f) : 1f);
                int al = (int)(255 * a);
                float tw = UiTextMetrics.Width(t.Text, 14) + 40;
                UiRect r = new UiRect((Width - tw) / 2, y - (1 - a) * 6, tw, 30);
                c.Fill(r, UiTheme.WithAlpha(unchecked((int)0x101418), (int)(215 * a)), 15);
                c.Stroke(r, UiTheme.WithAlpha(UiTheme.Accent, (int)(180 * a)), 1.2f, 15);
                c.Text(t.Text, r, new UiTextStyle(14, UiTheme.WithAlpha(t.Argb, al), UiAlign.Center, true));
                y -= 36;
            }
        }
    }
}
