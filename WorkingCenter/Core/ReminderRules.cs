namespace WorkingCenter.Core;

using WorkingCenter.Models;

/// <summary>提醒调度规则：时间/日期解析、下次触发点推算、人话描述。
/// 所有推算基于本地墙钟（DateTime.Now），符合用户直觉。月末钳制：如每月 31 日
/// 在 2 月取当月最后一天。</summary>
public static class ReminderRules
{
    /// <summary>错过补发窗口：触发点已过但不超过这个时长的仍补发通知。</summary>
    public static readonly TimeSpan CatchUpWindow = TimeSpan.FromMinutes(5);

    public static bool TryParseTime(string? text, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out int h) || !int.TryParse(parts[1], out int m)) return false;
        if (h < 0 || h > 23 || m < 0 || m > 59) return false;
        time = new TimeSpan(h, m, 0);
        return true;
    }

    /// <summary>防呆归一化时间：容忍中文冒号、缺前导零（9:1）、无冒号 3-4 位数字（930/0930）。
    /// 成功时输出规范 "HH:mm" 与对应 TimeSpan。</summary>
    public static bool TryNormalizeTime(string? text, out string normalized, out TimeSpan time)
    {
        normalized = "";
        time = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().Replace('：', ':');
        string hPart, mPart;
        if (s.Contains(':'))
        {
            var parts = s.Split(':');
            if (parts.Length != 2) return false;
            hPart = parts[0];
            mPart = parts[1];
        }
        else if (s.All(char.IsDigit) && s.Length is 3 or 4)
        {
            hPart = s[..^2];     // 最后两位是分钟
            mPart = s[^2..];
        }
        else return false;
        if (!int.TryParse(hPart, out int h) || !int.TryParse(mPart, out int m)) return false;
        if (h is < 0 or > 23 || m is < 0 or > 59) return false;
        time = new TimeSpan(h, m, 0);
        normalized = $"{h:D2}:{m:D2}";
        return true;
    }

    /// <summary>防呆归一化日期：容忍 年/月/日、2026.11.1、20261101、不带前导零等，
    /// 成功时输出规范 "yyyy-MM-dd" 与对应 DateTime。</summary>
    public static bool TryNormalizeDate(string? text, out string normalized, out DateTime date)
    {
        normalized = "";
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim()
            .Replace('年', '-').Replace('月', '-').Replace("日", "")
            .Replace('.', '-').Replace('/', '-');
        // 8 位纯数字 → yyyyMMdd
        if (s.Length == 8 && s.All(char.IsDigit)
            && DateTime.TryParseExact(s, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d8))
        {
            date = d8;
        }
        else if (!DateTime.TryParse(s, out date))
        {
            return false;
        }
        if (date.Date < new DateTime(2000, 1, 1)) return false;
        normalized = date.ToString("yyyy-MM-dd");
        return true;
    }

    public static bool TryParseDate(string? text, out DateTime date)
    {
        return DateTime.TryParseExact((text ?? "").Trim(), "yyyy-MM-dd",
            null, System.Globalization.DateTimeStyles.None, out date)
            && date.Date >= new DateTime(2000, 1, 1);
    }

    /// <summary>1..7 的周几（周一=1，周日=7）→ .NET DayOfWeek。</summary>
    public static DayOfWeek ToDayOfWeek(int day1To7)
        => day1To7 is >= 1 and <= 7 ? (DayOfWeek)(day1To7 % 7) : DayOfWeek.Monday;

    /// <summary>循环提醒的下次触发点（不处理 Once——它有固定的日期时间）。</summary>
    public static DateTime NextTrigger(Reminder r, DateTime after)
    {
        var t = ParseTimeOr(r.Time);
        return r.Recurrence switch
        {
            ReminderRecurrence.Daily => NextDaily(after, t),
            ReminderRecurrence.Weekly => NextWeekly(r, after, t),
            ReminderRecurrence.Monthly => NextMonthly(r, after, t),
            _ => NextOnce(r, after),   // Once: 到期即结束，理论上只有创建时计算
        };
    }

    /// <summary>Once 的固定触发时间（日期+时刻）。</summary>
    public static DateTime NextOnce(Reminder r, DateTime after)
    {
        DateTime.TryParse(r.OnceDate, out var d);
        return d.Date + ParseTimeOr(r.Time);
    }

    private static TimeSpan ParseTimeOr(string time)
        => TryParseTime(time, out var t) ? t : new TimeSpan(9, 0, 0);

    private static DateTime NextDaily(DateTime after, TimeSpan t)
    {
        var candidate = after.Date + t;
        return candidate <= after ? candidate.AddDays(1) : candidate;
    }

    private static DateTime NextWeekly(Reminder r, DateTime after, TimeSpan t)
    {
        var want = ToDayOfWeek(r.Day);
        for (int i = 0; i <= 7; i++)
        {
            var d = after.Date.AddDays(i);
            if (d.DayOfWeek != want) continue;
            var candidate = d + t;
            if (candidate > after) return candidate;
        }
        return after.Date.AddDays(7) + t;   // fallback，不可达
    }

    private static DateTime NextMonthly(Reminder r, DateTime after, TimeSpan t)
    {
        // 本月内找：几号超过当月天数时钳到月末
        for (int m = 0; m <= 1; m++)
        {
            var cursor = after.AddMonths(m);
            int days = DateTime.DaysInMonth(cursor.Year, cursor.Month);
            int day = Math.Min(r.Day, days);
            var candidate = new DateTime(cursor.Year, cursor.Month, day) + t;
            if (candidate > after) return candidate;
        }
        return after.AddMonths(2);   // fallback，不可达
    }

    /// <summary>行内的人话描述，如 "2026-11-01 09:00" / "每天 09:00" / "每周三 09:30" / "每月 15日 10:00"。</summary>
    public static string Describe(Reminder r)
    {
        var time = (TryParseTime(r.Time, out var t) ? t.ToString(@"hh\:mm") : r.Time);
        return r.Recurrence switch
        {
            ReminderRecurrence.Once => $"{r.OnceDate} {time}",
            ReminderRecurrence.Daily => $"每天 {time}",
            ReminderRecurrence.Weekly => $"每周{WeekDayName(r.Day)} {time}",
            ReminderRecurrence.Monthly => $"每月 {r.Day}日 {time}",
            _ => time,
        };
    }

    public static string WeekDayName(int day1To7)
        => day1To7 switch
        {
            1 => "一", 2 => "二", 3 => "三", 4 => "四", 5 => "五", 6 => "六",
            7 => "日", _ => "?",
        };
}
