//***************************************************************************
// InstanceSlot.cs : 같은 PC 에서 뷰어를 여러 개 띄울 때 실행마다 다른 번호(슬롯)를 나눠 준다.
//   임시 폴더의 슬롯별 잠금 파일의 첫 바이트를 잠가 차지한다. 프로세스가 끝나면(비정상 종료 포함) 운영체제가
//   핸들을 닫아 잠금이 풀리므로 번호가 새지 않는다. 접속할 서버(host:port)마다 따로 센다.
//   번호는 CharId 에 더해 쓴다(서버는 같은 캐릭터 ID 의 중복 입장을 거부한다).
//***************************************************************************
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GameViewer.App
{
    public static class InstanceSlot
    {
        public const int MaxSlots = 64;

        // 프로세스가 끝날 때까지 잡고 있어야 하므로 참조를 유지한다.
        private static readonly List<FileStream> Held = new List<FileStream>();

        /// <summary>비어 있는 가장 작은 슬롯(0부터)을 잡는다. 모두 차 있으면 -1.</summary>
        public static int Acquire(string host, int port)
        {
            string dir = Path.Combine(Path.GetTempPath(), "GameViewerSlots");
            try { Directory.CreateDirectory(dir); }
            catch (Exception) { return -1; }

            string key = Sanitize(host) + "_" + port;
            for (int i = 0; i < MaxSlots; ++i)
            {
                try
                {
                    FileStream fs = new FileStream(Path.Combine(dir, key + "_" + i + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                    try
                    {
                        fs.Lock(0, 1);                      // 바이트 범위 잠금: Windows 와 Linux(mono) 모두 프로세스 사이에서 지켜진다
                    }
                    catch (IOException) { fs.Dispose(); continue; }
                    Held.Add(fs);
                    return i;
                }
                catch (IOException) { }                     // 다른 뷰어가 쥐고 있다
                catch (UnauthorizedAccessException) { }     // 다른 사용자가 만든 파일
            }
            return -1;
        }

        private static string Sanitize(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in s ?? "")
                sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            return sb.ToString();
        }
    }
}
