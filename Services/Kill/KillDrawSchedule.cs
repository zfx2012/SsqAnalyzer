namespace SsqAnalyzer.Services.Kill;

internal static class KillDrawSchedule
{
    internal static readonly TimeZoneInfo China = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
    internal static DateTime NextDate(DateTime latest)
    {
        var next = latest.Date.AddDays(1);
        while (next.DayOfWeek is not (DayOfWeek.Tuesday or DayOfWeek.Thursday or DayOfWeek.Sunday)) next = next.AddDays(1);
        return next;
    }
    internal static int NextPeriod(int latestPeriod, DateTime latestDate)
    {
        var next = NextDate(latestDate);
        return next.Year > latestDate.Year ? next.Year * 1000 + 1 : latestPeriod + 1;
    }
    internal static DateTime ChinaTime(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, China);
}
