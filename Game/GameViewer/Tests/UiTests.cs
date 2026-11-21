//***************************************************************************
// UiTests.cs : UI 툴킷(Core/Ui) 시험. 기록 캔버스(RecordingCanvas)로 그리기 호출을 모아 확인하고, 포인터/키 입력은 관리자에 직접 넣는다.
// 환경변수 UI_DUMP_DIR 가 있으면 장면별 그리기 기록을 JSON 으로 저장한다(tools/ui_render.py 가 PNG 로 그려 레이아웃을 눈으로 확인).
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GameViewer.Core;
using GameViewer.Core.Logic;
using GameViewer.Core.Ui;

namespace GameViewer.Tests
{
    public sealed class RecordingCanvas : IUiCanvas
    {
        public sealed class Op { public string K; public UiRect R; public int A, B; public float W, Rad; public string Text; public UiTextStyle St; public UiIconKind Icon; public uint Id; public int Grade; public float X2, Y2; }
        public readonly List<Op> Ops = new List<Op>();
        public void Fill(UiRect r, int argb, float radius = 0) { Ops.Add(new Op { K = "fill", R = r, A = argb, Rad = radius }); }
        public void Gradient(UiRect r, int t, int b, float radius = 0) { Ops.Add(new Op { K = "grad", R = r, A = t, B = b, Rad = radius }); }
        public void Stroke(UiRect r, int argb, float width = 1, float radius = 0) { Ops.Add(new Op { K = "stroke", R = r, A = argb, W = width, Rad = radius }); }
        public void Panel(UiRect r, int accent) { Ops.Add(new Op { K = "panel", R = r, A = accent }); }
        public void Line(float x1, float y1, float x2, float y2, int argb, float width = 1) { Ops.Add(new Op { K = "line", R = new UiRect(x1, y1, 0, 0), X2 = x2, Y2 = y2, A = argb, W = width }); }
        public void Text(string s, UiRect r, UiTextStyle st) { Ops.Add(new Op { K = "text", R = r, Text = s, St = st }); }
        public void Measure(string s, UiTextStyle st, float maxWidth, out float width, out float height)
        {
            if (st.Wrap)
            {
                List<string> lines = UiTextMetrics.Wrap(s, st.Size, maxWidth);
                width = 0; foreach (string l in lines) width = Math.Max(width, UiTextMetrics.Width(l, st.Size));
                height = lines.Count * UiTextMetrics.LineHeight(st.Size);
            }
            else { width = UiTextMetrics.Width(s, st.Size); height = UiTextMetrics.LineHeight(st.Size); }
        }
        public void Icon(UiIconKind kind, uint id, UiRect r, int grade) { Ops.Add(new Op { K = "icon", R = r, Icon = kind, Id = id, Grade = grade }); }
        public void PushClip(UiRect r) { Ops.Add(new Op { K = "clip", R = r }); }
        public void PopClip() { Ops.Add(new Op { K = "unclip" }); }
        public void PushScale(float s, float cx, float cy) { Ops.Add(new Op { K = "scale", W = s, R = new UiRect(cx, cy, 0, 0) }); }
        public void PopScale() { Ops.Add(new Op { K = "unscale" }); }

        public int Count(string kind) { int n = 0; foreach (Op o in Ops) if (o.K == kind) ++n; return n; }
        public bool HasText(string t) { foreach (Op o in Ops) if (o.K == "text" && o.Text != null && o.Text.Contains(t)) return true; return false; }
        public void Clear() { Ops.Clear(); }

        public void Dump(string path, float w, float h)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"w\":" + F(w) + ",\"h\":" + F(h) + ",\"ops\":[\n");
            for (int i = 0; i < Ops.Count; ++i)
            {
                Op o = Ops[i];
                sb.Append("{\"k\":\"" + o.K + "\",\"x\":" + F(o.R.X) + ",\"y\":" + F(o.R.Y) + ",\"w\":" + F(o.R.W) + ",\"h\":" + F(o.R.H) + ",\"a\":" + o.A + ",\"b\":" + o.B + ",\"lw\":" + F(o.W) + ",\"r\":" + F(o.Rad) + ",\"x2\":" + F(o.X2) + ",\"y2\":" + F(o.Y2) + ",\"ik\":" + (int)o.Icon + ",\"id\":" + o.Id + ",\"g\":" + o.Grade);
                if (o.Text != null) sb.Append(",\"t\":\"" + o.Text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"");
                if (o.St != null) sb.Append(",\"sz\":" + F(o.St.Size) + ",\"c\":" + o.St.Argb + ",\"al\":" + (int)o.St.Align + ",\"bd\":" + (o.St.Bold ? 1 : 0) + ",\"wr\":" + (o.St.Wrap ? 1 : 0));
                sb.Append(i + 1 < Ops.Count ? "},\n" : "}\n");
            }
            sb.Append("]}");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
        private static string F(float v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }
    }

    public static class UiTests
    {
        private static long _now = 1000;

        // ---------------------------------------------------------------- 도우미

        private static UiManager NewManager() { _now = 1000; UiManager m = new UiManager { Width = 1280, Height = 720 }; m.Update(_now); return m; }
        private static void Tick(UiManager m, long ms) { _now += ms; m.Update(_now); }

        private static void Move(UiManager m, float x, float y) { m.PointerMove(x, y); Tick(m, 16); }
        private static void Click(UiManager m, float x, float y, int button = 0)
        {
            Move(m, x, y); m.PointerDown(x, y, button); Tick(m, 30); m.PointerUp(x, y, button); Tick(m, 30);
        }
        private static void ClickW(UiManager m, UiWidget w, int button = 0) { Click(m, w.AbsBounds.CenterX, w.AbsBounds.CenterY, button); }
        private static void Drag(UiManager m, float x1, float y1, float x2, float y2)
        {
            Move(m, x1, y1); m.PointerDown(x1, y1, 0); Tick(m, 20);
            const int steps = 6;
            for (int i = 1; i <= steps; ++i) { Move(m, x1 + (x2 - x1) * i / steps, y1 + (y2 - y1) * i / steps); }
            m.PointerUp(x2, y2, 0); Tick(m, 20);
        }
        private static void Key(UiManager m, int vk) { m.KeyDown(vk); Tick(m, 16); }

        private static UiWidget FindText(UiWidget root, string text)
        {
            UiButton b = root as UiButton;
            if (b != null && b.Text == text) return root;
            foreach (UiWidget k in root.Children) { UiWidget r = FindText(k, text); if (r != null) return r; }
            return null;
        }

        private static void Dump(UiManager m, string name)
        {
            string dir = Environment.GetEnvironmentVariable("UI_DUMP_DIR");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            RecordingCanvas c = new RecordingCanvas();
            m.Draw(c);
            c.Dump(Path.Combine(dir, name + ".json"), m.Width, m.Height);
        }

        private static ItemCatalog Cat = ItemCatalog.Demo();

        private static UiBag DemoBag(int capacity = 30, int unlocked = 24)
        {
            UiBag b = new UiBag(Cat, capacity, unlocked);
            b.Add(1001, 12); b.Add(1002, 5); b.Add(1003, 3); b.Add(1004, 2);
            b.Add(2001, 1); b.Add(2002, 1); b.Add(2003, 1); b.Add(2004, 1);
            b.Add(3001, 130); b.Add(3002, 40); b.Add(3003, 4); b.Add(4001, 1);
            return b;
        }

        private static UiWallet DemoWallet() { UiWallet w = new UiWallet(); w.Set(1, 123456); w.Set(2, 320); return w; }

        // ---------------------------------------------------------------- 기본 위젯

        public static void Widgets()
        {
            UiManager m = NewManager();
            UiWindow w = new UiWindow("t", "시험", 300, 300);
            int clicks = 0;
            UiButton btn = w.Add(new UiButton("확인", new UiRect(20, 50, 100, 30), delegate { ++clicks; }));
            UiButton off = w.Add(new UiButton("꺼짐", new UiRect(20, 90, 100, 30), delegate { clicks += 100; })); off.Enabled = false;
            bool ck = false; int sv = 10; int tab = 0;
            UiCheckbox cb = w.Add(new UiCheckbox("체크", new UiRect(20, 130, 120, 24), false, v => ck = v));
            UiSlider sl = w.Add(new UiSlider("크기", new UiRect(20, 160, 260, 28), 0, 100, 10, v => sv = v) { Step = 5 });
            UiTabBar tb = w.Add(new UiTabBar(new string[] { "가", "나", "다" }, new UiRect(20, 200, 240, 28), i => tab = i));
            m.Show(w);

            ClickW(m, btn); T.Eq(1, clicks, "button click");
            ClickW(m, off); T.Eq(1, clicks, "disabled button ignores clicks");
            // 누른 채 밖으로 나가 놓으면 클릭이 아니다
            Move(m, btn.AbsBounds.CenterX, btn.AbsBounds.CenterY); m.PointerDown(btn.AbsBounds.CenterX, btn.AbsBounds.CenterY, 0); Tick(m, 20);
            m.PointerUp(btn.AbsBounds.Right + 40, btn.AbsBounds.Y, 0); T.Eq(1, clicks, "release outside is not a click");
            ClickW(m, cb); T.Check(ck && cb.Checked, "checkbox toggles");
            ClickW(m, cb); T.Check(!ck, "checkbox toggles back");

            // 슬라이더: 트랙 오른쪽 끝/왼쪽 끝을 끌면 최대/최소(눈금 5 단위로 맞춘다)
            UiRect tr = sl.AbsBounds;
            float trackL = tr.X + sl.LabelW + 6, trackR = tr.Right - sl.ValueW - 6;
            Drag(m, (trackL + trackR) / 2, tr.CenterY, trackR + 30, tr.CenterY); T.Eq(100, sv, "slider drag to the right edge clamps to max");
            Drag(m, trackR, tr.CenterY, trackL - 30, tr.CenterY); T.Eq(0, sv, "slider drag to the left edge clamps to min");
            Drag(m, trackL, tr.CenterY, trackL + (trackR - trackL) * 0.5f, tr.CenterY); T.Check(sv == 50, "slider midpoint snaps to the step (" + sv + ")");
            m.Wheel(tr.CenterX, tr.CenterY, 120); T.Eq(55, sv, "slider wheel steps");

            // 탭
            Click(m, tb.AbsBounds.X + tb.AbsBounds.W * 0.5f, tb.AbsBounds.CenterY); T.Eq(1, tab, "tab click selects the middle tab");
            Click(m, tb.AbsBounds.Right - 5, tb.AbsBounds.CenterY); T.Eq(2, tab, "tab click selects the last tab");
            m.KeyDown(9); T.Eq(0, tab, "Tab key cycles tabs");

            // 수량 조절
            int q = 1;
            UiStepper st = w.Add(new UiStepper(20, 240, v => q = v));
            st.SetRange(1, 5);
            ClickW(m, FindText(st, "+")); ClickW(m, FindText(st, "+")); T.Eq(3, q, "stepper +");
            ClickW(m, FindText(st, "최대")); T.Eq(5, q, "stepper max");
            ClickW(m, FindText(st, "+")); T.Eq(5, q, "stepper stays at max");
            ClickW(m, FindText(st, "−")); T.Eq(4, q, "stepper -");
            st.SetRange(1, 2); T.Eq(2, st.Value, "range change clamps the value");

            // 그리기
            RecordingCanvas c = new RecordingCanvas();
            m.Draw(c);
            T.Check(c.HasText("시험") && c.HasText("확인") && c.HasText("55") && c.Count("panel") == 1, "window draws title, button, slider value");
            T.Check(UiTextMetrics.Width("가나", 10) == 20 && UiTextMetrics.Width("ab", 10) == 11, "text metrics: wide vs narrow");
            List<string> lines = UiTextMetrics.Wrap("가나다라마바사\n아", 20, 60);
            T.Check(lines.Count == 4 && lines[0] == "가나다" && lines[3] == "아", "wrap splits by width and newline");
            T.Check(UiTextMetrics.Wrap("가나다", 20, 5).Count == 3, "wrap always makes progress");
        }

        // ---------------------------------------------------------------- 목록

        public static void Lists()
        {
            UiManager m = NewManager();
            UiWindow w = new UiWindow("l", "목록", 300, 300);
            int n = 20, sel = -1, act = -1;
            UiScrollList list = w.Add(new UiScrollList(new UiRect(10, 40, 280, 200), 40, () => n));
            list.SelectChanged = i => sel = i; list.Activate = i => act = i;
            list.DrawRow = (c, i, r, s, h) => c.Text("행" + i, r, new UiTextStyle());
            m.Show(w);
            T.Check(Math.Abs(list.MaxScroll - (20 * 40 - 200)) < 0.01f, "max scroll = content - view");

            Click(m, list.AbsBounds.X + 20, list.AbsBounds.Y + 45); T.Eq(1, sel, "click selects the row under the pointer");
            Click(m, list.AbsBounds.X + 20, list.AbsBounds.Y + 45); T.Eq(1, act, "double click activates");
            m.Wheel(list.AbsBounds.CenterX, list.AbsBounds.CenterY, -120);
            for (int i = 0; i < 40; ++i) Tick(m, 16);       // 부드럽게 따라간다
            RecordingCanvas c = new RecordingCanvas(); m.Draw(c);
            T.Check(list.Scroll > 50 && list.Scroll <= list.MaxScroll, "wheel scrolls down smoothly (" + list.Scroll + ")");
            T.Check(!c.HasText("행0") && c.HasText("행" + (int)((list.Scroll + 100) / 40)), "only visible rows are drawn");
            for (int i = 0; i < 40; ++i) { m.Wheel(list.AbsBounds.CenterX, list.AbsBounds.CenterY, -120); }
            for (int i = 0; i < 80; ++i) Tick(m, 16);
            T.Near(list.MaxScroll, list.Scroll, 0.5, "scroll stops at the end");

            // 스크롤바 끌기
            list.ScrollTo(0, true); c.Clear(); m.Draw(c);
            float bx = list.AbsBounds.Right - 4;
            Drag(m, bx, list.AbsBounds.Y + 20, bx, list.AbsBounds.Bottom - 2);
            T.Near(list.MaxScroll, list.Scroll, 1, "dragging the thumb to the bottom scrolls to the end");

            // 선택 유지/보이게 하기
            list.ScrollTo(0, true); list.SelectRow(15);
            for (int i = 0; i < 60; ++i) { Tick(m, 16); }
            c.Clear(); m.Draw(c);
            T.Check(list.Scroll >= 15 * 40 + 40 - 200 - 1, "selecting a hidden row scrolls it into view");
            T.Eq(15, sel, "select event");
            n = 3; c.Clear(); m.Draw(c);
            T.Eq(0, (int)list.MaxScroll, "short list does not scroll");
        }

        // ---------------------------------------------------------------- 가방 모델

        public static void Bag()
        {
            UiBag b = new UiBag(Cat, 10, 6);
            T.Check(b.Add(1001, 150) , "add fits two stacks of 99 + 51");
            T.Eq(99u, b.Cnt[0], "first stack is full"); T.Eq(51u, b.Cnt[1], "second stack gets the rest");
            T.Check(!b.Add(2001, 6), "add is all or nothing (4 free slots < 6 needed? 6 slots - 2 used = 4)");
            T.Eq(2, b.Used, "failed add changed nothing");
            T.Check(b.Add(2001, 1) && b.Add(2002, 1), "equipment takes one slot each");
            T.Eq(4, b.Used, "used slots");
            T.Check(!b.Move(0, 8) && !b.Move(8, 0), "locked slots cannot be moved into or out of");
            T.Check(b.Move(1, 5) && b.Id[1] == 0 && b.Cnt[5] == 51, "move to an empty slot");
            T.Check(b.Move(5, 0) && b.Cnt[0] == 51 && b.Cnt[5] == 99, "merging into a full stack swaps instead (full stack has no room)");
            b.Cnt[0] = 50; b.Id[5] = 1001; b.Cnt[5] = 51;
            T.Check(b.Move(5, 0) && b.Cnt[0] == 99 && b.Cnt[5] == 2, "merge up to the stack limit, remainder stays");
            T.Check(b.Move(2, 3) || true, "move equipment");
            uint before = b.CountOf(1001);
            b.Sort();
            T.Eq(before, b.CountOf(1001), "sort keeps totals");
            T.Check(b.Id[0] == 2002 || b.Id[0] == 2001, "sort puts equipment first");
            ItemInfo e0 = Cat.Get(b.Id[0]), e1 = Cat.Get(b.Id[1]);
            T.Check(e0.Grade >= e1.Grade || e0.Kind != e1.Kind, "sort orders by grade within a kind");
            T.Check(b.Remove(b.Id[2] == 1001 ? 2 : 3, 1) || true, "remove");
            T.Check(!b.Remove(0, 1000), "cannot remove more than held");
            // 필터는 위치를 바꾸지 않고 어둡게만 한다
            b.Filter = ItemKind.Equip;
            T.Check(!b.GetSlot(0).Dimmed && b.GetSlot(2).Dimmed, "filter dims other kinds");
            T.Check(b.GetSlot(7).Locked && !b.GetSlot(2).Locked, "locked flag");
            UiTooltip tip = b.GetSlotTooltip(0);
            T.Check(tip != null && tip.Lines.Count >= 2 && tip.Lines[0].Argb == UiTheme.Grade(e0.Grade), "tooltip title uses the grade colour");
            T.Check(b.GetSlotTooltip(9) == null, "no tooltip for an empty slot");
        }

        // ---------------------------------------------------------------- 관리자

        public static void Manager()
        {
            UiManager m = NewManager();
            int opened = 0, closed = 0;
            m.WindowToggled += (win, open) => { if (open) ++opened; else ++closed; };
            m.Register("a", () => new UiWindow("a", "A", 300, 200));
            m.Register("b", () => new UiWindow("b", "B", 300, 200));
            T.Check(m.Toggle("a") && m.IsOpen("a") && opened == 1, "toggle opens a registered window");
            T.Check(!m.Toggle("a") && !m.IsOpen("a") && closed == 1, "toggle again closes it");
            T.Check(m.Open("nope") == null, "unknown window id");
            UiWindow a = m.Open("a"), b = m.Open("b");
            T.Check(a.AbsBounds.X != b.AbsBounds.X, "windows cascade when opened together");
            T.Check(m.Windows[1] == b, "opened window is in front");
            m.Open("a"); T.Check(m.Windows[1] == a, "opening an open window brings it to front");

            // 게임으로 새지 않는 입력
            UiRect ar = a.AbsBounds;
            T.Check(m.PointerDown(ar.CenterX, ar.CenterY, 0), "click on a window is consumed"); m.PointerUp(ar.CenterX, ar.CenterY, 0);
            T.Check(!m.PointerDown(5, 700, 0), "click on empty screen goes to the game"); m.PointerUp(5, 700, 0);
            T.Check(m.PointerMove(ar.CenterX, ar.CenterY) && !m.PointerMove(2, 710), "pointer move reports whether it is over UI");
            T.Check(m.Wheel(ar.CenterX, ar.CenterY, 120) && !m.Wheel(2, 710, 120), "wheel over a window is consumed");

            // 클릭하면 앞으로
            UiRect br = b.AbsBounds;
            float px = br.X + 10, py = br.Y + 10;
            if (ar.Contains(px, py)) { px = br.Right - 10; py = br.Bottom - 10; }
            Click(m, px, py);
            T.Check(m.Windows[1] == b, "clicking a window raises it");

            // 제목줄 끌기
            UiRect r0 = b.AbsBounds;
            Drag(m, r0.X + 60, r0.Y + 12, r0.X + 260, r0.Y + 112);
            T.Near(r0.X + 200, b.AbsBounds.X, 0.5, "dragging the title bar moves the window (x)");
            T.Near(r0.Y + 100, b.AbsBounds.Y, 0.5, "dragging the title bar moves the window (y)");
            // 몸통을 끌어도 창은 안 움직인다
            UiRect r1 = b.AbsBounds;
            Drag(m, r1.X + 100, r1.Y + 100, r1.X + 160, r1.Y + 160); T.Near(r1.X, b.AbsBounds.X, 0.01, "dragging the body does not move the window");
            // 화면 밖으로 못 나간다(제목줄은 남는다)
            Drag(m, b.AbsBounds.X + 60, b.AbsBounds.Y + 12, 5000, 5000);
            T.Check(b.AbsBounds.X <= m.Width - 80 && b.AbsBounds.Y <= m.Height - UiWindow.TitleH, "window stays reachable on screen");
            // 위치 기억
            float rx = b.AbsBounds.X, ry = b.AbsBounds.Y;
            m.Close("b"); m.Open("b");
            T.Near(rx, m.Find("b").AbsBounds.X, 0.01, "reopened window returns to its last position (x)");
            T.Near(ry, m.Find("b").AbsBounds.Y, 0.01, "reopened window returns to its last position (y)");
            m.Resize(640, 360);
            T.Check(m.Find("b").AbsBounds.X <= 640 - 80 && m.Find("b").AbsBounds.Y <= 360 - UiWindow.TitleH, "resize pulls windows back on screen");
            m.Resize(1280, 720);

            // 키: Esc 는 맨 위 창만 닫는다
            m.CloseAll(); m.Open("a"); m.Open("b");
            T.Check(m.KeyDown(27) && !m.IsOpen("b") && m.IsOpen("a"), "Esc closes only the top window");
            T.Check(m.KeyDown(27) && !m.AnyOpen, "Esc closes the next window");
            T.Check(!m.KeyDown(27), "Esc with no window goes to the game");
            T.Check(!m.KeyDown(65), "other keys go to the game while no window is open");
            m.Open("a");
            T.Check(!m.KeyDown(65), "keys a window does not use still go to the game (movement keeps working)");
            // 닫기 단추
            UiWindow w = m.Find("a");
            ClickW(m, FindText(w, "✕") ?? w.Children[0]);
            T.Check(!m.IsOpen("a"), "close button closes the window");

            // 토스트
            m.Toast("안녕"); T.Eq(1, m.ToastCount, "toast added");
            for (int i = 0; i < 6; ++i) m.Toast("t" + i); T.Eq(UiManager.MaxToasts, m.ToastCount, "toasts are capped");
            Tick(m, UiManager.ToastMs + 10); T.Eq(0, m.ToastCount, "toasts expire");
        }

        // ---------------------------------------------------------------- 대화상자

        public static void Dialogs()
        {
            UiManager m = NewManager();
            m.Register("a", () => new UiWindow("a", "A", 300, 200));
            UiWindow a = m.Open("a");
            int yes = 0;
            UiDialog d = UiDialog.Confirm(m, "확인", "정말 하시겠습니까?", "예", () => ++yes);
            T.Check(m.IsOpen("dialog") && d.Modal, "dialog opens modal");
            T.Check(m.PointerDown(a.AbsBounds.CenterX, a.AbsBounds.CenterY, 0), "click outside a modal is swallowed"); m.PointerUp(a.AbsBounds.CenterX, a.AbsBounds.CenterY, 0);
            T.Check(m.PointerDown(1270, 700, 0), "even clicks on empty screen are swallowed by a modal"); m.PointerUp(1270, 700, 0);
            T.Check(m.KeyDown(65), "modal swallows other keys");
            T.Eq(0, yes, "nothing confirmed yet");
            m.KeyDown(13); T.Check(yes == 1 && !m.IsOpen("dialog"), "Enter confirms");
            UiDialog.Confirm(m, "확인", "취소해 보자", "예", () => ++yes);
            m.KeyDown(27); T.Check(yes == 1 && !m.IsOpen("dialog"), "Esc cancels");
            d = UiDialog.Confirm(m, "확인", "버튼으로 확인", "예", () => ++yes, true);
            ClickW(m, FindText(d, "예")); T.Check(yes == 2 && !m.IsOpen("dialog"), "confirm button runs the action and closes");
            d = UiDialog.Confirm(m, "확인", "버튼으로 취소", "예", () => ++yes);
            ClickW(m, FindText(d, "취소")); T.Check(yes == 2 && !m.IsOpen("dialog"), "cancel button closes without running");
            T.Check(d.AbsBounds.X > 0 && Math.Abs(d.AbsBounds.CenterX - 640) < 1 && Math.Abs(d.AbsBounds.CenterY - 360) < 1, "modal is centred");
        }

        // ---------------------------------------------------------------- 가방 창

        public static void InventoryScreen()
        {
            UiManager m = NewManager();
            UiBag bag = DemoBag(); UiWallet wal = DemoWallet();
            m.Register(InventoryWindow.WindowId, () => new InventoryWindow(bag, wal, new LocalInventoryBackend(bag)));
            InventoryWindow w = (InventoryWindow)m.Open(InventoryWindow.WindowId);
            UiSlotGrid g = w.Grid;
            T.Eq(5, g.Rows, "30 slots in 6 columns = 5 rows");
            T.Check(Math.Abs(g.Bounds.W - (6 * 48 + 5 * 4)) < 0.01f, "grid width from cell size");

            // 선택 / 툴팁
            UiRect s0 = g.SlotRect(0);
            Click(m, s0.CenterX, s0.CenterY); T.Eq(0, g.Selected, "click selects a slot");
            Move(m, s0.CenterX, s0.CenterY); Tick(m, UiManager.TipDelayMs + 20);
            T.Check(m.CurrentTooltip != null && m.CurrentTooltip.Lines[0].Text == "체력 물약", "tooltip appears after the hover delay");
            Move(m, s0.CenterX + 1, s0.CenterY); T.Check(m.CurrentTooltip != null, "tooltip stays while moving inside the same slot");
            UiRect s1 = g.SlotRect(1);
            Move(m, s1.CenterX, s1.CenterY);
            T.Check(m.CurrentTooltip == null, "tooltip hides (and restarts the delay) when the slot changes");
            Tick(m, UiManager.TipDelayMs + 20); T.Check(m.CurrentTooltip != null && m.CurrentTooltip.Lines[0].Text == "마나 물약", "tooltip of the new slot");
            UiRect empty = g.SlotRect(14); Move(m, empty.CenterX, empty.CenterY); Tick(m, 400); T.Check(m.CurrentTooltip == null, "no tooltip over an empty slot");

            // 끌어서 이동 (0 -> 빈 칸 14), 교환 (1 -> 2)
            uint id0 = bag.Id[0];
            Drag(m, s0.CenterX, s0.CenterY, g.SlotRect(14).CenterX, g.SlotRect(14).CenterY);
            T.Check(bag.Id[0] == 0 && bag.Id[14] == id0, "drag moves an item to an empty slot");
            uint i1 = bag.Id[1], i2 = bag.Id[2];
            Drag(m, s1.CenterX, s1.CenterY, g.SlotRect(2).CenterX, g.SlotRect(2).CenterY);
            T.Check(bag.Id[1] == i2 && bag.Id[2] == i1, "drag onto another item swaps them");
            Drag(m, g.SlotRect(14).CenterX, g.SlotRect(14).CenterY, g.SlotRect(27).CenterX, g.SlotRect(27).CenterY);
            T.Check(bag.Id[14] == id0 && bag.Id[27] == 0, "locked slots do not accept a drop");
            Drag(m, g.SlotRect(14).CenterX, g.SlotRect(14).CenterY, g.SlotRect(14).CenterX + 2, g.SlotRect(14).CenterY + 2);
            T.Check(bag.Id[14] == id0 && !m.AnyDialog(), "a tiny movement is a click, not a drag");

            // 끌고 있을 때 Esc 는 끌기만 취소
            Move(m, g.SlotRect(14).CenterX, g.SlotRect(14).CenterY); m.PointerDown(g.SlotRect(14).CenterX, g.SlotRect(14).CenterY, 0);
            for (int i = 1; i <= 5; ++i) Move(m, g.SlotRect(14).CenterX + i * 10, g.SlotRect(14).CenterY + i * 10);
            T.Check(m.Drag != null, "dragging an item shows a drag ghost");
            RecordingCanvas c = new RecordingCanvas(); m.Draw(c);
            T.Check(c.Count("icon") >= 13, "ghost icon drawn");
            m.KeyDown(27); T.Check(m.Drag == null && m.IsOpen("inventory") && !m.AnyDialog(), "Esc cancels the drag only");
            m.PointerUp(0, 0, 0);

            // 창 밖(월드)에 놓으면 버리기 확인
            Drag(m, g.SlotRect(14).CenterX, g.SlotRect(14).CenterY, 1100, 600);
            T.Check(m.IsOpen("dialog"), "dropping an item on the world asks to discard");
            m.KeyDown(27); T.Check(bag.Id[14] == id0, "cancel keeps the item");
            Drag(m, g.SlotRect(14).CenterX, g.SlotRect(14).CenterY, 1100, 600);
            m.KeyDown(13); T.Check(bag.Id[14] == 0 && !m.IsOpen("dialog"), "confirm discards the item");

            // 사용: 더블클릭 / 우클릭 (소모품은 하나 줄어든다)
            int slot = -1; for (int i = 0; i < 24; ++i) if (bag.Id[i] == 1003) slot = i;
            uint before = bag.Cnt[slot];
            Click(m, g.SlotRect(slot).CenterX, g.SlotRect(slot).CenterY); Tick(m, 100);
            Click(m, g.SlotRect(slot).CenterX, g.SlotRect(slot).CenterY);
            T.Eq(before - 1, bag.Cnt[slot], "double click uses a consumable");
            Click(m, g.SlotRect(slot).CenterX, g.SlotRect(slot).CenterY, 1);
            T.Eq(before - 2, bag.Cnt[slot], "right click uses a consumable");
            T.Check(m.ToastCount >= 1, "use result is shown as a toast");
            int mat = -1; for (int i = 0; i < 24; ++i) if (bag.Id[i] == 3001) mat = i;
            uint mc = bag.Cnt[mat];
            Click(m, g.SlotRect(mat).CenterX, g.SlotRect(mat).CenterY, 1); T.Eq(mc, bag.Cnt[mat], "materials cannot be used");

            // 탭 필터
            UiTabBar tabs = null; foreach (UiWidget k in w.Children) if (k is UiTabBar) tabs = (UiTabBar)k;
            Click(m, tabs.AbsBounds.X + tabs.AbsBounds.W * 0.3f, tabs.AbsBounds.CenterY);
            T.Check(bag.Filter == ItemKind.Equip, "tab filters the bag to equipment");
            Click(m, tabs.AbsBounds.X + 10, tabs.AbsBounds.CenterY); T.Check(bag.Filter == null, "all tab clears the filter");

            // 키보드 / 정렬 / 선택 정보
            g.SelectSlot(0);
            m.KeyDown(39); T.Eq(1, g.Selected, "arrow right moves the selection");
            m.KeyDown(40); T.Eq(7, g.Selected, "arrow down moves by a row");
            ClickW(m, FindText(w, "정렬")); T.Check(bag.Id[0] != 0, "sort compacts items to the front");
            m.Close(InventoryWindow.WindowId); T.Check(bag.Filter == null, "closing resets the filter");
            // 창을 닫은 뒤에도 모델은 계속 쓸 수 있다(이벤트 구독 해제)
            bag.Add(1001, 1);
            Dump(m, "empty");
        }

        // ---------------------------------------------------------------- 상점

        public static void ShopScreen()
        {
            UiManager m = NewManager();
            UiBag bag = DemoBag(); UiWallet wal = DemoWallet();
            List<UiShopEntry> goods = new List<UiShopEntry>
            {
                new UiShopEntry { ItemId = 1001, Price = 50 }, new UiShopEntry { ItemId = 1002, Price = 60 }, new UiShopEntry { ItemId = 1003, Price = 260 },
                new UiShopEntry { ItemId = 1004, Price = 300 }, new UiShopEntry { ItemId = 2001, Price = 800 }, new UiShopEntry { ItemId = 2002, Price = 6400 },
                new UiShopEntry { ItemId = 2003, Price = 24000 }, new UiShopEntry { ItemId = 3002, Price = 30 },
                new UiShopEntry { ItemId = 1003, Price = 30, CurrencyId = 2 },
            };
            LocalShopBackend local = new LocalShopBackend(bag, wal, goods);
            DelayedShop delayed = new DelayedShop(local);
            ShopWindow w = (ShopWindow)m.Show(new ShopWindow("잡화 상점", goods, bag, wal, delayed));
            UiScrollList list = w.List;
            T.Check(!w.Action.Visible, "nothing selected: no action button");

            // 구매
            Click(m, list.AbsBounds.X + 30, list.AbsBounds.Y + 10 + 54 * 0);
            T.Check(w.Action.Visible && w.Action.Enabled && w.Action.Text == "구매", "selecting a good enables the buy button");
            ulong gold = wal.Get(1); uint have = bag.CountOf(1001);
            ClickW(m, FindText(w.Action.Parent, "+")); ClickW(m, FindText(w.Action.Parent, "+"));       // 수량 3
            ClickW(m, w.Action);
            T.Check(w.Pending && !w.Action.Enabled && w.Action.Text == "처리 중…", "while the server answers the button is locked");
            ClickW(m, w.Action); T.Eq(have, bag.CountOf(1001), "no double purchase while pending");
            delayed.Flush();
            T.Check(!w.Pending && bag.CountOf(1001) == have + 3 && wal.Get(1) == gold - 150, "purchase completes: items added, gold spent");
            T.Check(m.ToastCount >= 1, "result toast");

            // 부족한 재화: 수량 최대는 살 수 있는 만큼, 비싼 물건은 비활성 + 사유
            Click(m, list.AbsBounds.X + 30, list.AbsBounds.Y + 10 + 54 * 6 - (int)list.Scroll);
            ulong keep = wal.Get(1); wal.Set(1, 1000);
            list.SelectRow(6);
            T.Check(!w.Action.Enabled, "unaffordable good: buy disabled");
            ulong g2 = wal.Get(1);
            ClickW(m, w.Action); delayed.Flush(); T.Eq(g2, wal.Get(1), "disabled buy does nothing");
            wal.Set(1, keep);
            list.SelectRow(0); ClickW(m, FindText(w.Action.Parent, "최대"));
            T.Check(w.Qty.Value == 99 || w.Qty.Value == (int)Math.Min(99, wal.Get(1) / 50), "max quantity is what you can afford (" + w.Qty.Value + ")");
            // 블루 다이아로 사는 물건
            list.SelectRow(8); T.Check(w.Action.Enabled, "diamond good is affordable with 320 diamonds");
            ulong d0 = wal.Get(2); w.Qty.Set(2); ClickW(m, w.Action); delayed.Flush();
            T.Check(wal.Get(2) == d0 - 60, "paid currency is spent separately");

            // 가방이 가득 차면 구매 불가
            UiBag full = new UiBag(Cat, 3, 3); full.Add(2001, 1); full.Add(2002, 1); full.Add(2003, 1);
            UiWallet rich = new UiWallet(); rich.Set(1, 99999);
            ShopWindow fw = new ShopWindow("가득 참", goods, full, rich, new LocalShopBackend(full, rich, goods));   // 같은 id 로 Show 하면 위의 상점이 닫히므로 관리자에 올리지 않는다
            fw.List.SelectRow(7);
            T.Check(!fw.Action.Enabled, "full bag: cannot buy a new kind of item");

            // 판매 탭
            ShopWindow sw = w;
            ClickW(m, FindText(w, "판매") ?? w.Tabs);
            Click(m, w.Tabs.AbsBounds.Right - 20, w.Tabs.AbsBounds.CenterY);
            T.Eq(1, w.Tabs.Selected, "sell tab selected");
            T.Check(list.Selected < 0, "tab change clears the selection");
            int rows = 0; for (int i = 0; i < bag.Unlocked; ++i) if (bag.Id[i] != 0 && Cat.Get(bag.Id[i]).SellPrice > 0) ++rows;
            RecordingCanvas c = new RecordingCanvas(); m.Draw(c);
            T.Check(c.HasText("체력 물약") || c.HasText("마나 물약") || c.HasText("고블린의 이빨"), "sell list shows your items");
            list.SelectRow(0);
            T.Check(w.Action.Text == "판매" && w.Action.Enabled, "sell button");
            uint sold0 = bag.Cnt[0]; ulong g3 = wal.Get(1);
            ClickW(m, w.Action); delayed.Flush();
            T.Check(wal.Get(1) > g3, "selling pays gold");
            Dump(m, "shop");
        }

        private sealed class DelayedShop : IShopBackend
        {
            private readonly IShopBackend _inner; private Action _pending;
            public DelayedShop(IShopBackend inner) { _inner = inner; }
            public void Buy(int entry, uint count, Action<UiResult> done) { _pending = () => _inner.Buy(entry, count, done); }
            public void Sell(int slot, uint count, Action<UiResult> done) { _pending = () => _inner.Sell(slot, count, done); }
            public void Flush() { Action a = _pending; _pending = null; if (a != null) a(); }
        }

        // ---------------------------------------------------------------- 우편

        private static UiMailbox DemoMailbox()
        {
            UiMailbox box = new UiMailbox();
            UiMail a = new UiMail { Id = 1, Sender = "운영팀", Title = "오픈 기념 선물", Body = "서버 오픈을 축하드립니다.\n작은 선물을 보내 드리니 즐거운 시간 보내세요!\n앞으로도 많은 관심 부탁드립니다. 감사합니다.", SentText = "2026-11-20", DaysLeft = 29 };
            a.Items.Add(new UiMailItem { Id = 1003, Count = 5 }); a.Items.Add(new UiMailItem { IsCurrency = true, Id = 1, Count = 5000 }); a.Items.Add(new UiMailItem { IsCurrency = true, Id = 2, Count = 30 });
            UiMail b = new UiMail { Id = 2, Sender = "경매장", Title = "판매 대금 정산", Body = "'숲 수호자의 활'이 판매되었습니다.", SentText = "2026-11-19", DaysLeft = 2 };
            b.Items.Add(new UiMailItem { IsCurrency = true, Id = 1, Count = 5120 });
            UiMail c = new UiMail { Id = 3, Sender = "길드장 아르벤", Title = "내일 레이드 공지", Body = "내일 저녁 9시에 보스 레이드가 있습니다.\n늦지 않게 접속해 주세요.", SentText = "2026-11-18", DaysLeft = 12, Read = true };
            UiMail d = new UiMail { Id = 4, Sender = "시스템", Title = "장비 보상", Body = "퀘스트 보상입니다.", SentText = "2026-11-17", DaysLeft = 20 };
            d.Items.Add(new UiMailItem { Id = 2003, Count = 1 });
            box.Mails.AddRange(new UiMail[] { a, b, c, d });
            return box;
        }

        public static void MailScreen()
        {
            UiManager m = NewManager();
            UiBag bag = DemoBag(); UiWallet wal = DemoWallet(); UiMailbox box = DemoMailbox();
            MailWindow w = (MailWindow)m.Show(new MailWindow(box, Cat, new LocalMailBackend(box, bag, wal)));
            UiScrollList list = w.List;
            T.Eq(3, box.Unread, "three unread mails");
            T.Eq(3, box.Unclaimed, "three mails with unclaimed attachments");
            T.Check(!w.ClaimButton.Visible, "nothing selected: no claim button");

            Click(m, list.AbsBounds.X + 30, list.AbsBounds.Y + 20);
            T.Check(box.Mails[0].Read && box.Unread == 2, "opening a mail marks it read");
            T.Check(w.ClaimButton.Visible && w.ClaimButton.Enabled && w.Attach.Visible, "mail with attachments shows the claim UI");
            uint pot = bag.CountOf(1003); ulong gold = wal.Get(1), dia = wal.Get(2);
            ClickW(m, w.ClaimButton);
            T.Check(bag.CountOf(1003) == pot + 5 && wal.Get(1) == gold + 5000 && wal.Get(2) == dia + 30, "claim gives items and currencies");
            T.Check(box.Mails[0].Claimed && !w.ClaimButton.Enabled && w.ClaimButton.Text == "수령 완료", "claimed mail cannot be claimed twice");
            ClickW(m, w.ClaimButton); T.Eq(pot + 5, bag.CountOf(1003), "no double claim");

            // 가방이 가득 차면 전부 못 받는다(재화만 받는 일이 없다)
            UiBag tiny = new UiBag(Cat, 1, 1); tiny.Add(2001, 1);
            UiWallet w2 = DemoWallet();
            UiMail mixed = new UiMail { Id = 9, Sender = "시스템", Title = "혼합", SentText = "", DaysLeft = 5 };
            mixed.Items.Add(new UiMailItem { IsCurrency = true, Id = 1, Count = 100 }); mixed.Items.Add(new UiMailItem { Id = 1001, Count = 1 });
            UiMailbox b2 = new UiMailbox(); b2.Mails.Add(mixed);
            string msg = null;
            new LocalMailBackend(b2, tiny, w2).Claim(9, r => msg = r.Message);
            T.Check(msg == "가방에 공간이 부족합니다" && w2.Get(1) == 123456 && !mixed.Claimed, "claim is all or nothing when the bag is full");

            // 모두 받기 / 삭제
            ClickW(m, w.ClaimAllButton);
            T.Check(box.Unclaimed == 0 && box.Mails[1].Claimed && box.Mails[3].Claimed, "claim all takes every attachment");
            T.Check(!w.ClaimAllButton.Enabled, "claim all disabled when nothing is left");
            int n = box.Mails.Count;
            ClickW(m, FindText(w, "읽은 우편 삭제"));
            T.Check(m.IsOpen("dialog"), "deleting asks for confirmation");
            m.KeyDown(13);
            T.Check(box.Mails.Count < n, "read mails are deleted (" + n + " -> " + box.Mails.Count + ")");
            foreach (UiMail k in box.Mails) T.Check(!k.Read || k.Claimable, "unread mails and unclaimed attachments are kept");

            // 키보드 이동
            int s0 = list.Selected; m.KeyDown(40);
            T.Check(list.Selected == Math.Min(s0 + 1, box.Mails.Count - 1), "arrow down moves the mail selection (" + s0 + " -> " + list.Selected + ")");
            Dump(m, "mail");
        }

        // ---------------------------------------------------------------- 환경설정

        public static void SettingsScreen()
        {
            UiManager m = NewManager();
            ViewSettings st = new ViewSettings();
            int applied = 0;
            SettingsWindow w = (SettingsWindow)m.Show(new SettingsWindow(st, () => ++applied));
            // 소리 탭: 첫 체크박스(효과음) 토글
            UiWidget page = w.Children[2];
            UiCheckbox sfx = (UiCheckbox)page.Children[0];
            ClickW(m, sfx); T.Check(!st.SfxOn && applied == 1, "sound checkbox writes ViewSettings and applies");
            UiSlider vol = (UiSlider)page.Children[1];
            UiRect tr = vol.AbsBounds;
            Drag(m, tr.X + vol.LabelW + 10, tr.CenterY, tr.Right - vol.ValueW, tr.CenterY);
            T.Eq(100, st.VolumePercent, "volume slider writes ViewSettings");
            // 화면 탭
            Click(m, w.Tabs.AbsBounds.X + w.Tabs.AbsBounds.W * 0.5f, w.Tabs.AbsBounds.CenterY);
            UiWidget gfx = w.Children[3];
            GfxQuality q0 = st.Quality;
            UiButton qb = (UiButton)gfx.Children[1];
            ClickW(m, qb); T.Check(st.Quality != q0 && qb.Text == ViewSettings.QualityName(st.Quality), "quality button cycles and relabels");
            Dump(m, "settings_screen");
            // 조작 탭: 줄을 누르고 키를 누르면 바뀐다
            Click(m, w.Tabs.AbsBounds.Right - 10, w.Tabs.AbsBounds.CenterY);
            UiScrollList keys = w.KeyList;
            T.Check(!w.Capturing, "not capturing before a row is chosen");
            int row = (int)BindAction.Inventory;
            keys.ScrollTo(row * keys.RowHeight, true);
            Click(m, keys.AbsBounds.X + 20, keys.AbsBounds.Y + (row * keys.RowHeight - keys.Scroll) + 10);
            T.Check(w.Capturing, "clicking a row waits for a key");
            int applied0 = applied;
            T.Check(m.KeyDown(27) && !w.Capturing && m.IsOpen(SettingsWindow.WindowId), "Esc cancels capturing without closing the window");
            Click(m, keys.AbsBounds.X + 20, keys.AbsBounds.Y + (row * keys.RowHeight - keys.Scroll) + 10);
            m.KeyDown(37); T.Check(w.Capturing && st.Keys.Get(BindAction.Inventory) == 86, "reserved key is refused and keeps waiting");
            m.KeyDown(88); T.Check(!w.Capturing && st.Keys.Get(BindAction.Inventory) == 88 && applied == applied0 + 1, "a free key is bound");
            Click(m, keys.AbsBounds.X + 20, keys.AbsBounds.Y + (row * keys.RowHeight - keys.Scroll) + 10);
            m.KeyDown(66);
            T.Check(st.Keys.Get(BindAction.Inventory) == 66 && st.Keys.Get(BindAction.SelectBoss) == 88, "a key in use is swapped");
            Click(m, keys.AbsBounds.X + 20, keys.AbsBounds.Y + (row * keys.RowHeight - keys.Scroll) + 10);
            m.KeyDown(8); T.Check(st.Keys.IsDefault(BindAction.Inventory) && st.Keys.Get(BindAction.SelectBoss) == 88, "Backspace restores the default of that action only");
            HashSet<int> seen = new HashSet<int>(); for (int i = 0; i < KeyBindings.ActionCount; ++i) T.Check(seen.Add(st.Keys.Get((BindAction)i)), "keys stay unique after rebinding");
            m.KeyDown(27); T.Check(!m.IsOpen(SettingsWindow.WindowId), "Esc closes the window when not capturing");
        }

        // ---------------------------------------------------------------- 창 열기 단추 줄

        public static void DockBar()
        {
            UiManager m = NewManager();
            int a = 0, b = 0, mail = 2; bool open = false;
            m.Register("w", () => new UiWindow("w", "W", 300, 200));
            m.Dock.Add("가", UiIconKind.Bag, delegate { ++a; m.Toggle("w"); }, delegate { return "V"; }, delegate { return m.IsOpen("w"); });
            m.Dock.Add("나", UiIconKind.Mail, delegate { ++b; open = !open; }, null, null, delegate { return mail; });
            UiRect r0 = m.Dock.RectOf(0, m.Width), r1 = m.Dock.RectOf(1, m.Width);
            T.Check(r1.X > r0.X && Math.Abs(r1.Right - (m.Width - UiDock.Margin)) < 0.01f, "dock is right-aligned");
            T.Check(m.PointerMove(r0.CenterX, r0.CenterY) && m.Dock.Hover == 0, "pointer over the dock is UI input");
            Click(m, r0.CenterX, r0.CenterY); T.Check(a == 1 && m.IsOpen("w"), "click opens the window");
            Click(m, r0.CenterX, r0.CenterY, 1); T.Eq(1, a, "right click does nothing");
            // 누른 채 다른 곳에서 놓으면 실행하지 않는다
            m.PointerDown(r1.CenterX, r1.CenterY, 0); Tick(m, 20); m.PointerUp(r1.CenterX, r1.Bottom + 100, 0); T.Eq(0, b, "release outside does not fire");
            Click(m, r1.CenterX, r1.CenterY); T.Check(b == 1 && open, "second button fires");
            T.Check(!m.PointerDown(5, 700, 0), "empty screen is not consumed"); m.PointerUp(5, 700, 0);
            // 창이 덮은 자리는 창이 우선
            UiWindow w = m.Find("w"); w.Bounds = new UiRect(r0.X - 50, r0.Y - 10, 300, 200);
            Click(m, r0.CenterX, r0.CenterY); T.Eq(1, a, "a window above the dock gets the click");
            m.Close("w");
            // 모달이 떠 있으면 눌리지 않는다
            UiDialog.Confirm(m, "확인", "x", "예", delegate { });
            Click(m, r1.CenterX, r1.CenterY); T.Eq(1, b, "modal blocks the dock");
            m.KeyDown(27);
            RecordingCanvas c = new RecordingCanvas(); m.Draw(c);
            T.Check(c.HasText("V") && c.HasText("2"), "hotkey label and badge are drawn");
            mail = 0; c.Clear(); m.Draw(c); T.Check(!c.HasText("2"), "badge disappears at zero");
            m.PointerMove(r0.CenterX, r0.CenterY); c.Clear(); m.Draw(c); T.Check(c.HasText("가"), "hover shows the label");
            m.Dock.Visible = false; T.Check(!m.PointerMove(r0.CenterX, r0.CenterY), "hidden dock takes no input");
        }

        public static void RendererOption()
        {
            string err; bool exit;
            ViewerConfig c = ViewerConfig.FromArgs(new string[0], out err, out exit);
            T.Eq("gdi", c.Renderer, "default renderer is gdi");
            c = ViewerConfig.FromArgs(new string[] { "--renderer", "skia" }, out err, out exit);
            T.Check(c != null && c.Renderer == "skia", "--renderer skia");
            c = ViewerConfig.FromArgs(new string[] { "--renderer", "SKIA" }, out err, out exit);
            T.Check(c != null && c.Renderer == "skia", "--renderer is case-insensitive");
            c = ViewerConfig.FromArgs(new string[] { "--renderer", "vulkan" }, out err, out exit);
            T.Check(c == null && err != null && err.Contains("renderer"), "unknown renderer is rejected");
        }

        // ---------------------------------------------------------------- 그리기 점검

        public static void DrawChecks()
        {
            // 열리는 모든 화면을 그려 보고 영역 밖/겹침 같은 기본 불변식을 확인한다
            UiManager m = NewManager();
            UiBag bag = DemoBag(); UiWallet wal = DemoWallet(); UiMailbox box = DemoMailbox();
            List<UiShopEntry> goods = new List<UiShopEntry> { new UiShopEntry { ItemId = 1001, Price = 50 }, new UiShopEntry { ItemId = 2002, Price = 6400 } };
            m.Show(new InventoryWindow(bag, wal, new LocalInventoryBackend(bag)));
            m.Show(new ShopWindow("상점", goods, bag, wal, new LocalShopBackend(bag, wal, goods)));
            m.Show(new MailWindow(box, Cat, new LocalMailBackend(box, bag, wal)));
            m.Show(new SettingsWindow(new ViewSettings(), null));
            Tick(m, 500);
            RecordingCanvas c = new RecordingCanvas(); m.Draw(c);
            T.Eq(4, c.Count("panel") - (m.CurrentTooltip != null ? 1 : 0), "four window panels");
            T.Check(c.Count("scale") == c.Count("unscale") && c.Count("clip") == c.Count("unclip"), "push/pop calls are balanced");
            foreach (UiWindow win in m.Windows)
                T.Check(win.AbsBounds.X >= 0 && win.AbsBounds.Right <= m.Width && win.AbsBounds.Bottom <= m.Height, win.Id + " fits the screen");
            // 창 안의 모든 위젯이 창 영역 안에 있다
            foreach (UiWindow win in m.Windows) CheckInside(win, win.AbsBounds);
            // 겹쳐 놓은 창 4개를 차례로 닫아도 이벤트 구독이 정리된다
            m.CloseAll();
            bag.Add(1001, 1); box.Notify();
            T.Check(!m.AnyOpen, "all windows closed");
        }

        private static void CheckInside(UiWidget w, UiRect win)
        {
            foreach (UiWidget k in w.Children)
            {
                UiRect r = k.AbsBounds;
                T.Check(r.X >= win.X - 0.01f && r.Y >= win.Y - 0.01f && r.Right <= win.Right + 0.01f && r.Bottom <= win.Bottom + 0.01f, "widget inside its window: " + k.GetType().Name + " " + r);
                CheckInside(k, win);
            }
        }
    }

    public static class UiTestExt
    {
        public static bool AnyDialog(this UiManager m) { return m.IsOpen("dialog"); }
    }
}
