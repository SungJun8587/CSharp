using Google.Protobuf.WellKnownTypes;

public static class DateTimeExtensions
{
    public static Timestamp ToTimestampUtc(this DateTime dateTime)
    {
        if (dateTime.Kind != DateTimeKind.Utc)
        {
            dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        }

        return Timestamp.FromDateTime(dateTime);
    }
}