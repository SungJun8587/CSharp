using Newtonsoft.Json;
using Protocol;
using SignalRChat;

namespace SignalRChatApp
{
    public class TestActionData
    {
        public long Time;
        public string PacketName;
        public string Payload;

        [JsonIgnore]
        public object Deserialized = null;
    }

    public class TestPacketData
    {
        public List<TestActionData> Actions;
    }

    public class TestPacketManager
    {
        private static TestPacketManager instance = null;
        private TestPacketManager()
        {
            Reset();
        }

        public static TestPacketManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new TestPacketManager();
                }
                return instance;
            }
        }

        private TestPacketData _packetData;

        private long _firstPacketTime = 0;


        private void Reset()
        {
            _packetData = new TestPacketData
            {
                Actions = new List<TestActionData>()
            };
            _firstPacketTime = 0;
        }


        #region Unity Client
        public void AddPacket(string packetName, long time, string payLoad)
        {
            if (_firstPacketTime == 0)
            {
                _firstPacketTime = time;
            }

            var deltaTime = time - _firstPacketTime;

            _packetData.Actions.Add(new TestActionData
            {
                PacketName = packetName,
                Time = deltaTime,
                Payload = payLoad
            });
        }

        public void SaveToFile(string path)
        {
            var str = JsonConvert.SerializeObject(_packetData);
            System.IO.File.WriteAllText(path, str);
        }

        #endregion

        #region Server 코드
        bool GetParent(string path, ref string parentPath)
        {
            try
            {
                DirectoryInfo directoryInfo = Directory.GetParent(path);
                if (directoryInfo == null)
                {
                    parentPath = path;
                    return false;
                }
                parentPath = directoryInfo.FullName;
                System.Console.WriteLine(directoryInfo.FullName);
                return true;
            }
            catch (ArgumentNullException)
            {
                System.Console.WriteLine("Path is a null reference.");
                return false;
            }
            catch (ArgumentException)
            {
                System.Console.WriteLine("Path is an empty string, " +
                    "contains only white spaces, or " +
                    "contains invalid characters.");
                return false;
            }
        }

        // Parent path를 재귀로 돌면서 찾아본다
        public bool GetFinalPath(string basePath, string path, ref string finalPath)
        {
            // 현재 Path의 파일이 존재하는지 찾는다
            {
                var isExist = File.Exists(basePath + path);
                if (isExist)
                {
                    finalPath = basePath + path;
                    return true;
                }
            }

            string parentPath = string.Empty;
            // basePath의 parent가 더이상 존재하지 않을때까지 위로 올라간다
            while (GetParent(basePath, ref parentPath))
            {
                var isExist = File.Exists(parentPath + path);
                basePath = parentPath;
                if (isExist)
                {
                    finalPath = basePath + path;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 해당 path는 base에서의 상대경로로 입력한다
        /// </summary>
        /// <param name="path"> ex) path = "/BundleResources/Table/xxxx.json"</param>
        public void LoadActionsFromJson(string path)
        {
            // basePath의 시작은 console프로그램이 시작되는 위치부터
            var basePath = System.IO.Directory.GetCurrentDirectory();
            var finalPath = string.Empty;
            if (GetFinalPath(basePath, path, ref finalPath) == false)
            {
                //Console.WriteLine("failed : " + basePath);
                return;
            }

            //Console.WriteLine("success : " + finalPath);
            string jsonStr = System.IO.File.ReadAllText(finalPath);
            //_packetData = JsonConvert.DeserializeObject<TestPacketData>(jsonStr);

            // 정제하자
            if (_packetData != null || _packetData.Actions != null)
            {
                foreach (var item in _packetData.Actions)
                {
                    item.Deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject(item.Payload);
                }

                Random rand = new Random();

                TestActionData testActionData1 = new TestActionData();
                ReqEnterChatRoom reqEnterChatRoom = new ReqEnterChatRoom() { RoomId = 0 };
                testActionData1.PacketName = "ReqEnterChatRoom";
                testActionData1.Payload = Newtonsoft.Json.JsonConvert.SerializeObject(reqEnterChatRoom);
                testActionData1.Deserialized = reqEnterChatRoom;
                _packetData.Actions.Add(testActionData1);

                // 2023.12.06 추가 : 테스트 채팅 메세지 전송 패킷 추가
                for (int i = 1; i <= 10; i++)
                {
                    ReqSendChatRoom reqSendChatRoom = new ReqSendChatRoom() { Msg = "테스트_" + i.ToString() };
                    TestActionData testActionData2 = new TestActionData();
                    testActionData2.PacketName = "ReqSendChatRoom";
                    testActionData2.Payload = Newtonsoft.Json.JsonConvert.SerializeObject(reqSendChatRoom);
                    testActionData2.Deserialized = reqSendChatRoom;
                    _packetData.Actions.Add(testActionData2);
                }

                TestActionData testActionData3 = new TestActionData();
                testActionData3.PacketName = "ReqLeaveChatRoom";
                testActionData3.Payload = null;
                testActionData3.Deserialized = null;
                _packetData.Actions.Add(testActionData3);

                string jsonAction = Newtonsoft.Json.JsonConvert.SerializeObject(_packetData);
                Console.WriteLine(jsonAction);
            }
        }

        public int GetActionSize()
        {
            if (_packetData == null || _packetData.Actions == null)
            {
                return 0;
            }

            return _packetData.Actions.Count;
        }

        public TestActionData GetActionData(int index)
        {
            if (_packetData == null || _packetData.Actions == null)
            {
                return null;
            }

            return _packetData.Actions.ElementAt(index);
        }

        #endregion
    }
}
