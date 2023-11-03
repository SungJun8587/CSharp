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

        // 서버시간과의 차이
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
            //ALog.Log("AverageRTT: " + AverageRTT);
        }

        public DateTime NowDateTime
        {
            get
            {
                //var adjustedUTCTime = DateTime.UtcNow.Add(DiffSpan);
                //return TimeZoneInfo.ConvertTimeFromUtc(adjustedUTCTime, Timezone);
                //return DateTime.Now;
                //var adjustedUTCTime = DateTime.UtcNow.Add(DiffSpan);
                //return adjustedUTCTime.ToLocalTime();

                // 이미 now가 DiffTick이 반영되어있으므로
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

        public int DailyResetHour
        {
            get
            {
                return 0;
            }
        }

        public DayOfWeek WeekResetDay
        {
            get
            {
                return 0;
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

        // lastTime날짜의 StandardHour시간의 timestamp를 구한다
        public long GetStandardTimeLastDate(long lastTime)
        {
            var standardHour = DailyResetHour;
            var receivedDate = ConvertDateTime(lastTime);
            // receivedDate.Date : 그날의 자정을 구하게된다
            // standardHour시간만큼 진전시킨다
            var standardDate = receivedDate.Date.Add(new TimeSpan(standardHour, 0, 0));
            var standardTime = ConvertTimestamp(standardDate);

            // 만약 standardTime보다 lastTime이 작다면 하루전으로 돌린다
            if(standardTime > lastTime)
            {
                return standardTime - SgTime.T_DAY;
            }

            return standardTime;

        }

        /// <summary>
        /// timestamp가 기준이 되는 오늘의 특정시간(hour)를 지났는지
        /// ex) timestamp가 오늘 새벽5시를 지났는지
        /// </summary>
        /// <param name="standardHour"></param>
        /// <returns></returns>
        public bool IsPassTodayStandardHour(int standardHour, long timestamp)
        {
            // now기준 standardHour의 datetime을 구한다
            var standardDate = NowDateTime.Date.Add(new TimeSpan(standardHour, 0, 0));
            var standardTick = ConvertTimestamp(standardDate);
            return timestamp > standardTick;
        }

        //--------------------------------------------------------------------------
        // MonthFormat(ex - 202207) 을 다루는 헬퍼
        //--------------------------------------------------------------------------
        public int GetMonthFormat(long timestamp)
        {
            var standardHour = DailyResetHour;

            var date = ConvertDateTime(timestamp);
            // 만약 date가 첫째날 새벽5시(standardHour) 이전이라면 전의 달로 간주해야한다
            var firstDay = new DateTime(date.Year, date.Month, 1).ToLocalTime();
            var standardDate = firstDay.Date.Add(new TimeSpan(standardHour, 0, 0));
            if (ConvertTimestamp(standardDate) > timestamp)
            {
                var preMonthDate = date.AddMonths(-1);
                return (preMonthDate.Month) + (preMonthDate.Year * 100);
            }
            else
            {
                return date.Month + (date.Year * 100);
            }
        }

        public int GetBeforeMonthFormat(int monthFormat)
        {
            var year = monthFormat / 100;
            var month = monthFormat % 100;
            var curMonthDate = new DateTime(year, month, 1).ToLocalTime();
            var beforeMonth = curMonthDate.AddMonths(-1);
            return beforeMonth.Month + (beforeMonth.Year * 100);
        }

        public long GetMonthLastTime(int monthFormat)
        {
            var standardHour = DailyResetHour;

            // monthFormat 다음달의 기준시간(StandardHour)을 찾는다
            var year = monthFormat / 100;
            var month = monthFormat % 100;
            var curMonthDate = new DateTime(year, month, 1).ToLocalTime();
            var nextMonth = curMonthDate.AddMonths(1);
            var standardDate = nextMonth.Date.Add(new TimeSpan(standardHour, 0, 0));
            return ConvertTimestamp(standardDate);
        }

        public long GetNextMonthStandardTime(long timestamp)
        {
            var curMonth = GetMonthFormat(timestamp);
            return GetMonthLastTime(curMonth);
        }

        //--------------------------------------------------------------------------
        // WeekFormat(ex - ) 을 다루는 헬퍼
        //--------------------------------------------------------------------------
        public int GetWeekFormat(long timestamp)
        {
            var standardDayOfWeek = WeekResetDay;
            var standardHour = DailyResetHour;

            var date = ConvertDateTime(timestamp);

            // UnixTime기준(1970.1.1)의 기준요일을 찾아본다
            var unixDay = new DateTime(1970, 1, 1).ToLocalTime();
            var delta = standardDayOfWeek - unixDay.DayOfWeek;
            var weekDate = unixDay.AddDays(delta);
            // 기준요일의 기준시간을 구한다
            var firstDay = new DateTime(weekDate.Year, weekDate.Month, weekDate.Day).ToLocalTime();
            var standardDate = firstDay.Date.Add(new TimeSpan(standardHour, 0, 0));

            // 해당 UnixTime시작의 요일을 기준으로 현재까지 얼마나 지났는지 diff
            var diff = date - standardDate;
            var weekNum = (long)diff.TotalMilliseconds / T_WEEK;
            return (int)weekNum;
        }

        public long GetWeekEndCoolTime(int week)
        {
            var standardDayOfWeek = WeekResetDay;
            var standardHour = DailyResetHour;

            // UnixTime기준(1970.1.1)의 기준요일을 찾아본다
            var unixDay = new DateTime(1970, 1, 1).ToLocalTime();
            var delta = standardDayOfWeek - unixDay.DayOfWeek;
            var weekDate = unixDay.AddDays(delta);
            // 기준요일의 기준시간을 구한다
            var firstDay = new DateTime(weekDate.Year, weekDate.Month, weekDate.Day).ToLocalTime();
            var standardDate = firstDay.Date.Add(new TimeSpan(standardHour, 0, 0));

            long nextWeek = (long)week + 1;

            var duration = ConvertTimestamp(standardDate) + (nextWeek * T_WEEK);
            return duration;
        }

        public long GetWeekEndCoolTime(long timestamp)
        {
            // 현재시간기준
            var weekPeriod = GetWeekFormat(timestamp);
            return GetWeekEndCoolTime(weekPeriod);
        }


        //--------------------------------------------------------------------------
        // HourFormat(ex - ) 을 다루는 헬퍼
        //--------------------------------------------------------------------------
        public int GetHourFormat(long now)
        {
            return (int)(now / SgTime.T_HOUR);
        }

        //--------------------------------------------------------------------------
        // DailyFormat(ex - ) 을 다루는 헬퍼
        //--------------------------------------------------------------------------
        public int GetDailyFormat(long now)
        {
            // UnixTime기준(1970.1.1)의 기준시간을 찾아본다
            var startTime = GetStandardTimeLastDate(0);
            // now는 얼마나 흘렀는지
            var delta = now - startTime;
            return (int)(delta / SgTime.T_DAY);
        }

        public long GetTimeFromDailyFormat(int dailyFormat)
        {
            // UnixTime기준(1970.1.1)의 기준시간을 찾아본다
            var startTime = GetStandardTimeLastDate(0);
            var result = startTime + (long)dailyFormat * SgTime.T_DAY;
            return result;
        }



        //--------------------------------------------------------------------------
        // 분기(Quarter)
        //--------------------------------------------------------------------------
        public int GetQuarterFormat(long now)
        {
            var standardHour = DailyResetHour;

            var date = ConvertDateTime(now);

            // 0,1,2,3
            var quarterIndex = (date.Month - 1) / 3;

            // 만약 date가 이번쿼터 첫째날 새벽5시(standardHour) 이전이라면 전의 쿼터로 간주해야한다
            var firstDay = new DateTime(date.Year, (quarterIndex * 3) + 1, 1).ToLocalTime();
            var standardDate = firstDay.Date.Add(new TimeSpan(standardHour, 0, 0));
            if (ConvertTimestamp(standardDate) > now)
            {
                var preMonthDate = date.AddMonths(-1);
                var preQuarter = (preMonthDate.Month - 1) / 3 + 1;
                return (preQuarter) + (preMonthDate.Year * 100);
            }
            else
            {
                return (quarterIndex + 1) + (date.Year * 100);
            }
        }
        
        public long GetQuarterEndCoolTime(int quarterFormat)
        {
            
            var standardHour = DailyResetHour;

            var year = quarterFormat / 100;
            var quarterIndex = quarterFormat % 100 - 1;

            // 현재쿼터의 마지막달 및 date를 구한다(StandardTime 기준시로 보정은한다)
            var lastMonth = (quarterIndex * 3) + 3;
            var lastMonthDate = new DateTime(year, lastMonth, 1).ToLocalTime();
            var standardDate = lastMonthDate.Date.Add(new TimeSpan(standardHour, 0, 0));

            // GetNextMonthStandardTime으로 다음달의 기준시각을 구한다
            var nextStartStandardTime = GetNextMonthStandardTime(ConvertTimestamp(standardDate));
            return nextStartStandardTime;
            
        }


        //--------------------------------------------------------------------------
        // 반기(Half of the year)
        //--------------------------------------------------------------------------
        public int GetHalfYearFormat(long now)
        {
            var standardHour = DailyResetHour;

            var date = ConvertDateTime(now);

            // 0,1
            var halfIndex = (date.Month - 1) / 6;

            // 만약 date가 이번반기 첫째날 새벽5시(standardHour) 이전이라면 전의 반기로 간주해야한다
            var firstDay = new DateTime(date.Year, (halfIndex * 6) + 1, 1).ToLocalTime();
            var standardDate = firstDay.Date.Add(new TimeSpan(standardHour, 0, 0));
            if (ConvertTimestamp(standardDate) > now)
            {
                var preMonthDate = date.AddMonths(-1);
                var preQuarter = (preMonthDate.Month - 1) / 6 + 1;
                return (preQuarter) + (preMonthDate.Year * 100);
            }
            else
            {
                return (halfIndex + 1) + (date.Year * 100);
            }
        }

        public long GetHalfYearEndCoolTime(int halfYearFormat)
        {

            var standardHour = DailyResetHour;

            var year = halfYearFormat / 100;
            var quarterIndex = halfYearFormat % 100 - 1;

            // 현재쿼터의 마지막달 및 date를 구한다(StandardTime 기준시로 보정은한다)
            var lastMonth = (quarterIndex * 6) + 6;
            var lastMonthDate = new DateTime(year, lastMonth, 1).ToLocalTime();
            var standardDate = lastMonthDate.Date.Add(new TimeSpan(standardHour, 0, 0));

            // GetNextMonthStandardTime으로 다음달의 기준시각을 구한다
            var nextStartStandardTime = GetNextMonthStandardTime(ConvertTimestamp(standardDate));
            return nextStartStandardTime;

        }


        //--------------------------------------------------------------------------
        // BiWeekFormat(ex - ) 을 다루는 헬퍼
        //--------------------------------------------------------------------------
        public int GetBiWeekFormat(long timestamp)
        {
            var standardDayOfWeek = WeekResetDay;
            var standardHour = DailyResetHour;

            var date = ConvertDateTime(timestamp);

            // UnixTime기준(1970.1.1)의 기준요일을 찾아본다
            var unixDay = new DateTime(1970, 1, 1).ToLocalTime();
            var delta = standardDayOfWeek - unixDay.DayOfWeek;
            var weekDate = unixDay.AddDays(delta);
            // 기준요일의 기준시간을 구한다
            var firstDay = new DateTime(weekDate.Year, weekDate.Month, weekDate.Day).ToLocalTime();
            var standardDate = firstDay.Date.Add(new TimeSpan(standardHour, 0, 0));

            // 해당 UnixTime시작의 요일을 기준으로 현재까지 얼마나 지났는지 diff
            var diff = date - standardDate;
            var weekNum = (long)diff.TotalMilliseconds / (T_WEEK * 2);
            return (int)weekNum;
        }

        public long GetBiWeekEndCoolTime(int week)
        {
            var standardDayOfWeek = WeekResetDay;
            var standardHour = DailyResetHour;

            // UnixTime기준(1970.1.1)의 기준요일을 찾아본다
            var unixDay = new DateTime(1970, 1, 1).ToLocalTime();
            var delta = standardDayOfWeek - unixDay.DayOfWeek;
            var weekDate = unixDay.AddDays(delta);
            // 기준요일의 기준시간을 구한다
            var firstDay = new DateTime(weekDate.Year, weekDate.Month, weekDate.Day).ToLocalTime();
            var standardDate = firstDay.Date.Add(new TimeSpan(standardHour, 0, 0));

            long nextWeek = (long)week + 1;

            var duration = ConvertTimestamp(standardDate) + (nextWeek * (T_WEEK * 2));
            return duration;
        }

        public string ConvertDateTimeToString(DateTime dateTime)
        {
            return dateTime.ToString("yyyy-MM-dd HH:mm:ss");
        }

        #endregion
    }
}

