namespace Common.Lib
{
    public class SgTime
    {
        private static readonly Lazy<SgTime> instanceHolder =
            new Lazy<SgTime>(() => new SgTime());

        public SgTime()
        {
            //Timezone = TimeZoneInfo.FindSystemTimeZoneById("UTC");
            //Timezone = TimeZoneConverter.TZConvert.GetTimeZoneInfo("UTC");
        }

        public static SgTime I
        {
            get { return instanceHolder.Value; }
        }

        // 타임존
        public TimeZoneInfo Timezone { get; set; }

        // 서버 시간과의 차이
        public TimeSpan DiffSpan { get; private set; }
        public long DiffTick { get; private set; }

        // (todo) 평균 Round Trip Time(현재 besthttp의 응답 처리가 느린게 있어서(json파싱등) 한번의 http로는 roundTrip 찾기가 힘들다)
        public long AverageRTT { get; private set; }
        public TimeSpan AverageRTTSpan { get; private set; }

        public static readonly long T_SECOND = 1000;
        public static readonly long T_MINUTE = T_SECOND * 60;
        public static readonly long T_HOUR = T_MINUTE * 60;
        public static readonly long T_DAY = T_HOUR * 24;
        public static readonly long T_WEEK = T_DAY * 7;

        public void SetRTT(long beforeTime)
        {
            var clientNow = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            AverageRTT = (clientNow - beforeTime);
            AverageRTTSpan = new TimeSpan(AverageRTT);
        }

        public DateTime NowDateTime
        {
            get
            {
                return ConvertDateTime(Now);
            }
        }

        public long Now
        {
            get
            {
                return DateTimeOffset.Now.ToUnixTimeMilliseconds() + DiffTick;
            }
        }

        #region Helpers
        
        public long ConvertTimestamp(DateTime dateTime)
        {
            return (long)(dateTime.Subtract(new DateTime(1970, 1, 1).ToLocalTime())).TotalMilliseconds;
        }

        public DateTime ConvertDateTime(long millisecond)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(millisecond).DateTime.ToLocalTime();
        }

        #endregion
    }
}

