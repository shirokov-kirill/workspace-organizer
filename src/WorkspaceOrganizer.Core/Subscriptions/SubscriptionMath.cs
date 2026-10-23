using WorkspaceOrganizer.Core.Models;

namespace WorkspaceOrganizer.Core.Subscriptions;

public sealed record CurrencyTotal(string Currency, decimal Monthly)
{
    public decimal Yearly => Monthly * 12;
}

public static class SubscriptionMath
{
    public static decimal MonthlyCost(Subscription s) => s.Cycle switch
    {
        BillingCycle.Weekly => s.Cost * 52 / 12,
        BillingCycle.Monthly => s.Cost,
        BillingCycle.Quarterly => s.Cost / 3,
        BillingCycle.Yearly => s.Cost / 12,
        _ => throw new ArgumentOutOfRangeException(nameof(s), s.Cycle, null),
    };

    /// <summary>
    /// First renewal on or after <paramref name="today"/>. Steps are taken from the original
    /// anchor so a renewal on the 31st doesn't drift to the 28th after February.
    /// </summary>
    public static DateOnly NextOccurrence(DateOnly anchor, BillingCycle cycle, DateOnly today)
    {
        if (anchor >= today)
            return anchor;

        if (cycle == BillingCycle.Weekly)
        {
            var weeks = (today.DayNumber - anchor.DayNumber + 6) / 7;
            return anchor.AddDays(weeks * 7);
        }

        var monthsPerStep = cycle switch
        {
            BillingCycle.Monthly => 1,
            BillingCycle.Quarterly => 3,
            _ => 12,
        };
        for (var step = 1; ; step++)
        {
            var candidate = anchor.AddMonths(step * monthsPerStep);
            if (candidate >= today)
                return candidate;
        }
    }

    public static IReadOnlyList<CurrencyTotal> MonthlyTotals(IEnumerable<Subscription> subscriptions) =>
        subscriptions
            .Where(s => s.Active)
            .GroupBy(s => s.Currency.Trim().ToUpperInvariant())
            .Select(g => new CurrencyTotal(g.Key, g.Sum(MonthlyCost)))
            .OrderBy(t => t.Currency, StringComparer.Ordinal)
            .ToList();

    public static IReadOnlyList<Subscription> RenewingWithin(IEnumerable<Subscription> subscriptions, DateOnly today, int days) =>
        subscriptions
            .Where(s => s.Active && s.NextRenewal is { } d && NextOccurrence(d, s.Cycle, today) <= today.AddDays(days))
            .ToList();
}
