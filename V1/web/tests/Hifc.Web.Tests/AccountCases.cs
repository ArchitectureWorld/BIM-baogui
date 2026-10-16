using Hifc.Web.Contracts;
using Hifc.Web.Server;

internal static class AccountCases
{
    internal static void Run(Action<string, Action> test)
    {
        void Equal<T>(T actual, T expected) { if (!Equals(actual, expected)) throw new Exception($"expected {expected}; got {actual}"); }
        void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Invalid account input was accepted."); }
        var now = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        test("phone normalization reconciles plus86 and spaces", () => Equal(AccountPolicy.Phone(" +86 138 0013 8000 "), "13800138000"));
        test("invalid mainland phone cannot create another identity", () => Reject(() => AccountPolicy.Phone("12345")));
        test("name accepts international Unicode and trims edges", () => Equal(AccountPolicy.RealName("  阿依·陈 Jean  "), "阿依·陈 Jean"));
        test("control characters in real name are refused", () => Reject(() => AccountPolicy.RealName("陈\n明")));
        test("first registration expires at exactly 168 hours", () => Equal(AccountPolicy.InitialExpiry(now), new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc)));
        test("a user is expired at the exact deadline", () => Equal(AccountPolicy.IsActive(new AccountInfo { ExpiresAtUtc = now }, now), false));
        test("administrator is not subject to ordinary user deadline", () => Equal(AccountPolicy.IsActive(new AccountInfo { IsAdmin = true, ExpiresAtUtc = now.AddDays(-1) }, now), true));
        test("active-user extension starts from existing expiry", () => Equal(AccountPolicy.Extend(now.AddDays(2), now, 3, null), now.AddDays(5)));
        test("expired-user extension starts from database time", () => Equal(AccountPolicy.Extend(now.AddDays(-2), now, 3, null), now.AddDays(3)));
        test("specified date includes the entire Beijing day", () => Equal(AccountPolicy.Extend(now, now, null, "2026-10-18"), new DateTime(2026, 10, 18, 16, 0, 0, DateTimeKind.Utc)));
        test("an extension cannot shorten the existing deadline", () => Reject(() => AccountPolicy.Extend(now.AddDays(10), now, null, "2026-10-18")));
        test("extension requires one positive duration or date", () => { Reject(() => AccountPolicy.Extend(now, now, 0, null)); Reject(() => AccountPolicy.Extend(now, now, 1, "2026-11-01")); });
    }
}
