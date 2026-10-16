using Hifc.Web.Contracts;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hifc.Web.Server;

public static class AccountPolicy
{
    public static string Phone(string value)
    {
        if (value == null || value.Any(char.IsControl)) throw new InvalidOperationException("请填写有效的大陆手机号码。");
        var normalized = value.Trim().Replace(" ", "");
        if (normalized.StartsWith("+86", StringComparison.Ordinal)) normalized = normalized.Substring(3);
        if (!Regex.IsMatch(normalized, "^1[3-9][0-9]{9}$", RegexOptions.CultureInvariant)) throw new InvalidOperationException("请填写有效的大陆手机号码。");
        return normalized;
    }
    public static string RealName(string value)
    {
        var normalized = value?.Trim();
        if (normalized == null || normalized.Any(char.IsControl) || normalized.EnumerateRunes().Count() < 2 || normalized.EnumerateRunes().Count() > 40)
            throw new InvalidOperationException("姓名应为 2–40 个字符，不能包含控制字符。");
        return normalized;
    }
    public static DateTime InitialExpiry(DateTime registeredUtc) => registeredUtc.AddHours(168);
    public static bool IsActive(AccountInfo account, DateTime databaseNowUtc) => account.IsAdmin || databaseNowUtc < account.ExpiresAtUtc;
    public static DateTime Extend(DateTime oldExpiryUtc, DateTime databaseNowUtc, int? days, string untilDate)
    {
        if (days.HasValue == (untilDate != null)) throw new InvalidOperationException("请选择按天延期或指定日期中的一种。");
        DateTime result;
        if (days.HasValue)
        {
            if (days.Value < 1 || days.Value > 36500) throw new InvalidOperationException("延期天数须为 1–36500 的整数。");
            result = (oldExpiryUtc > databaseNowUtc ? oldExpiryUtc : databaseNowUtc).AddDays(days.Value);
        }
        else
        {
            if (!DateTime.TryParseExact(untilDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new InvalidOperationException("请选择有效的北京时间日期。");
            result = DateTime.SpecifyKind(date.AddDays(1).AddHours(-8), DateTimeKind.Utc);
        }
        if (result <= oldExpiryUtc) throw new InvalidOperationException("延期必须使到期时间晚于原期限。");
        return result;
    }
}
