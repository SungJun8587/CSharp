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
            if (e.Index == -1) return;    //�������� ���� ��� �� �� ���� �����ϴ�.
            ListBoxItemColorMessageSet item = listBoxChat.Items[e.Index] as ListBoxItemColorMessageSet;

            if (item != null)
            {
                e.DrawBackground();  // ��Ŀ�� ������ ����� ������� �� ������ �ڸ�Ʈ ó��
                e.Graphics.DrawString(item.Message, e.Font, new SolidBrush(item.ItemColor), e.Bounds, StringFormat.GenericDefault);
            }
            else
            {
                // The item isn't a ListBoxItemColorMessageSet, do something about it
            }
        }

        private void listBoxMsg_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index == -1) return;    //�������� ���� ��� �� �� ���� �����ϴ�.
            ListBoxItemColorMessageSet item = listBoxMsg.Items[e.Index] as ListBoxItemColorMessageSet;

            if (item != null)
            {
                e.DrawBackground();  // ��Ŀ�� ������ ����� ������� �� ������ �ڸ�Ʈ ó��
                e.Graphics.DrawString(item.Message, e.Font, new SolidBrush(item.ItemColor), e.Bounds, StringFormat.GenericDefault);
            }
            else
            {
                // The item isn't a ListBoxItemColorMessageSet, do something about it
            }
        }
    }
}