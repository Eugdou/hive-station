namespace WorkingCenter.Models;

/// <summary>提醒重复类型：单次 / 每日 / 每周 / 每月。</summary>
public enum ReminderRecurrence { Once, Daily, Weekly, Monthly }

/// <summary>一条提醒。数据 POCO，无序列化特性（沿用 AppData 快照风格）。
/// Time/OnceDate 存字符串（"HH:mm"/"yyyy-MM-dd"），便于编辑与 JSON 可读；
/// NextTriggerAt 为调度用的本地时间，由 ReminderRules 推算并随模型持久化。</summary>
public sealed class Reminder
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>提醒内容。单次=日期+时间；每日=时间；每周=周几+时间；每月=几号+时间。</summary>
    public ReminderRecurrence Recurrence { get; set; } = ReminderRecurrence.Daily;

    /// <summary>"HH:mm"（24 小时制），如 "09:30"。</summary>
    public string Time { get; set; } = "09:00";

    /// <summary>仅 Recurrence==Once 使用，"yyyy-MM-dd"。</summary>
    public string OnceDate { get; set; } = "";

    /// <summary>每周：1..7（周一=1，周日=7）；每月：1..31（超长月份自动钳到当月最后一天）。</summary>
    public int Day { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    /// <summary>下一次应触发的本地时间；调度器以此为准。DateTime == default 表示尚未计算。</summary>
    public DateTime NextTriggerAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
