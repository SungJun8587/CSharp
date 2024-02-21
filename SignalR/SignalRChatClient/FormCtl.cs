namespace SignalRChatClient
{
    public class ListBoxItemColorMessageSet
    {
        public ListBoxItemColorMessageSet(Color c, string m)
        {
            ItemColor = c;
            Message = m;
        }

        public Color ItemColor { get; set; }
        public string Message { get; set; }
    }

    public partial class Form1 : Form
    {
        private void listBoxChat_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index == -1) return;    //아이템이 없는 경우 는 할 일이 없습니다.
            ListBoxItemColorMessageSet item = listBoxChat.Items[e.Index] as ListBoxItemColorMessageSet;

            if (item != null)
            {
                e.DrawBackground();  // 포커스 라인의 배경을 지우려면 이 라인을 코멘트 처리
                e.Graphics.DrawString(item.Message, e.Font, new SolidBrush(item.ItemColor), e.Bounds, StringFormat.GenericDefault);
            }
            else
            {
                // The item isn't a ListBoxItemColorMessageSet, do something about it
            }
        }

        private void listBoxMsg_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index == -1) return;    //아이템이 없는 경우 는 할 일이 없습니다.
            ListBoxItemColorMessageSet item = listBoxMsg.Items[e.Index] as ListBoxItemColorMessageSet;

            if (item != null)
            {
                e.DrawBackground();  // 포커스 라인의 배경을 지우려면 이 라인을 코멘트 처리
                e.Graphics.DrawString(item.Message, e.Font, new SolidBrush(item.ItemColor), e.Bounds, StringFormat.GenericDefault);
            }
            else
            {
                // The item isn't a ListBoxItemColorMessageSet, do something about it
            }
        }
    }
}