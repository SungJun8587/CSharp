using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SignalRChatClient
{
    public class GlobalValues
    {
        public static string URL = string.Empty;
        public static string FpID = "";
        public static ulong PlayerNo = 0;
        public static string PlayerName = string.Empty;
        public static int RoomId = 0;
    }

    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            Application.EnableVisualStyles();
            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }
    }
}