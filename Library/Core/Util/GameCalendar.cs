// GameCalendar.cs : 시간대를 지정한 날짜 경계 계산(일일/주간 초기화, 기간 판정).
using System;

namespace Core.Util;

/// <summary>
/// 시간대(<see cref="TimeZoneInfo"/>)를 기준으로 "하루가 바뀌는 시각"과 "기간 안인지"를 계산한다. 시각은 unix ms(UTC)로 주고받고,
/// 현재 시각을 직접 읽지 않으므로 <see cref="ITimeProvider"/>의 값을 넘겨 시험할 수 있다. 상태가 없어 어느 스레드에서나 부를 수 있다.
/// <para>
/// 날짜와 시각은 모두 그 시간대의 벽시계 기준이다. 서머타임이 있는 시간대에서 존재하지 않는 시각(시계가 건너뛰는 구간)은
/// 그 구간이 끝난 직후로, 두 번 나타나는 시각은 첫 번째로 해석한다. 지원 범위는 1970년 이후의 날짜다.
/// </para>
/// </summary>
public static class GameCalendar
{
    private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EpochDate = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>unix ms를 그 시간대의 현지 시각(Kind=Unspecified)으로 바꾼다.</summary>
    public static DateTime ToLocal(long unixMs, TimeZoneInfo tz)
    {
        if (tz == null) throw new ArgumentNullException(nameof(tz));
        DateTime utc = Epoch.AddMilliseconds(unixMs);
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc, tz), DateTimeKind.Unspecified);
    }

    /// <summary>그 시간대의 현지 시각을 unix ms로 바꾼다. 서머타임 경계의 해석은 클래스 설명을 따른다.</summary>
    public static long ToUnixMs(DateTime local, TimeZoneInfo tz)
    {
        if (tz == null) throw new ArgumentNullException(nameof(tz));
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        // 건너뛰는 구간이면 구간이 끝날 때까지 1분씩 민다(구간은 보통 1시간, 최대 하루를 넘지 않는다).
        for (int i = 0; i < 24 * 60 && tz.IsInvalidTime(local); ++i)
            local = local.AddMinutes(1);

        TimeSpan offset;
        if (tz.IsAmbiguousTime(local))
        {
            // 두 번 나타나는 시각: 오프셋이 큰 쪽이 먼저 오는 순간이다.
            offset = TimeSpan.MinValue;
            foreach (TimeSpan o in tz.GetAmbiguousTimeOffsets(local))
                if (o > offset) offset = o;
        }
        else
        {
            offset = tz.GetUtcOffset(local);
        }

        return (local.Ticks - offset.Ticks - Epoch.Ticks) / TimeSpan.TicksPerMillisecond;
    }

    /// <summary>nowMs가 속한 날(현지 기준)이 시작된 시각(현지 0시).</summary>
    public static long StartOfDayMs(long nowMs, TimeZoneInfo tz) => ToUnixMs(ToLocal(nowMs, tz).Date, tz);

    /// <summary>nowMs 이후(같은 시각은 제외)로 처음 오는 현지 hour:minute:second. 일일 초기화 예약에 쓴다.</summary>
    public static long NextDailyMs(long nowMs, int hour, int minute, int second, TimeZoneInfo tz)
    {
        TimeSpan t = TimeOfDay(hour, minute, second);
        DateTime date = ToLocal(nowMs, tz).Date;
        long candidate = ToUnixMs(date + t, tz);
        return candidate > nowMs ? candidate : ToUnixMs(date.AddDays(1) + t, tz);
    }

    /// <summary>nowMs 이전(같은 시각 포함)으로 가장 최근에 지난 현지 hour:minute:second. "마지막 초기화 시각"을 구할 때 쓴다.</summary>
    public static long PrevDailyMs(long nowMs, int hour, int minute, int second, TimeZoneInfo tz)
    {
        TimeSpan t = TimeOfDay(hour, minute, second);
        DateTime date = ToLocal(nowMs, tz).Date;
        long candidate = ToUnixMs(date + t, tz);
        return candidate <= nowMs ? candidate : ToUnixMs(date.AddDays(-1) + t, tz);
    }

    /// <summary>nowMs 이후(같은 시각은 제외)로 처음 오는 현지 day 요일의 hour:minute:second. 주간 초기화 예약에 쓴다.</summary>
    public static long NextWeeklyMs(long nowMs, DayOfWeek day, int hour, int minute, int second, TimeZoneInfo tz)
    {
        TimeSpan t = TimeOfDay(hour, minute, second);
        DateTime today = ToLocal(nowMs, tz).Date;
        int ahead = ((int)day - (int)today.DayOfWeek + 7) % 7;
        long candidate = ToUnixMs(today.AddDays(ahead) + t, tz);
        return candidate > nowMs ? candidate : ToUnixMs(today.AddDays(ahead + 7) + t, tz);
    }

    /// <summary>
    /// "게임 날짜" 번호. 하루가 자정이 아니라 현지 resetHour시에 바뀌는 날짜 체계다(예: 5시 초기화면 04:59는 전날).
    /// 같은 번호끼리만 같은 날이다. 1970-01-01부터 센 일수다.
    /// </summary>
    public static long GameDayIndex(long nowMs, int resetHour, TimeZoneInfo tz)
    {
        if (resetHour < 0 || resetHour > 23) throw new ArgumentOutOfRangeException(nameof(resetHour));
        DateTime shifted = ToLocal(nowMs, tz).AddHours(-resetHour);
        return (long)(shifted.Date - EpochDate).TotalDays;
    }

    /// <summary>두 시각이 같은 게임 날짜인지(<see cref="GameDayIndex"/> 기준).</summary>
    public static bool IsSameGameDay(long aMs, long bMs, int resetHour, TimeZoneInfo tz)
        => GameDayIndex(aMs, resetHour, tz) == GameDayIndex(bMs, resetHour, tz);

    /// <summary>
    /// nowMs의 현지 시각이 [start, end) 하루 중 구간 안인지. end가 start보다 앞이면 자정을 넘는 구간(예: 23:00~02:00)이고,
    /// start와 end가 같으면 빈 구간(항상 false)이다. 하루 종일이 필요하면 호출하지 않는다.
    /// </summary>
    public static bool IsInDailyWindow(long nowMs, TimeSpan start, TimeSpan end, TimeZoneInfo tz)
    {
        CheckTimeOfDay(start, nameof(start));
        CheckTimeOfDay(end, nameof(end));
        TimeSpan tod = ToLocal(nowMs, tz).TimeOfDay;
        if (start == end) return false;
        return start < end ? (tod >= start && tod < end) : (tod >= start || tod < end);
    }

    private static TimeSpan TimeOfDay(int hour, int minute, int second)
    {
        if (hour < 0 || hour > 23) throw new ArgumentOutOfRangeException(nameof(hour));
        if (minute < 0 || minute > 59) throw new ArgumentOutOfRangeException(nameof(minute));
        if (second < 0 || second > 59) throw new ArgumentOutOfRangeException(nameof(second));
        return new TimeSpan(hour, minute, second);
    }

    private static void CheckTimeOfDay(TimeSpan t, string name)
    {
        if (t < TimeSpan.Zero || t >= TimeSpan.FromDays(1)) throw new ArgumentOutOfRangeException(name);
    }
}
