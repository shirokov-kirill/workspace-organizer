using WorkspaceOrganizer.Core.Models;
using WorkspaceOrganizer.Core.Subscriptions;

namespace WorkspaceOrganizer.Core.Tests;

public class SubscriptionMathTests
{
    [Theory]
    [InlineData(BillingCycle.Weekly, 12, 52)]
    [InlineData(BillingCycle.Monthly, 10, 10)]
    [InlineData(BillingCycle.Quarterly, 30, 10)]
    [InlineData(BillingCycle.Yearly, 120, 10)]
    public void MonthlyCost_normalizes_cycles(BillingCycle cycle, decimal cost, decimal expected)
    {
        Assert.Equal(expected, SubscriptionMath.MonthlyCost(new Subscription { Cost = cost, Cycle = cycle }));
    }

    [Fact]
    public void NextOccurrence_does_not_drift_after_short_months()
    {
        var anchor = new DateOnly(2026, 1, 31);

        Assert.Equal(new DateOnly(2026, 3, 31), SubscriptionMath.NextOccurrence(anchor, BillingCycle.Monthly, new DateOnly(2026, 3, 1)));
    }

    [Fact]
    public void NextOccurrence_keeps_future_and_today_dates()
    {
        var today = new DateOnly(2026, 10, 23);

        Assert.Equal(today, SubscriptionMath.NextOccurrence(today, BillingCycle.Yearly, today));
        Assert.Equal(new DateOnly(2027, 1, 1), SubscriptionMath.NextOccurrence(new DateOnly(2027, 1, 1), BillingCycle.Monthly, today));
    }

    [Theory]
    [InlineData(BillingCycle.Weekly, "2026-10-23")]
    [InlineData(BillingCycle.Quarterly, "2026-12-01")]
    [InlineData(BillingCycle.Yearly, "2027-09-01")]
    public void NextOccurrence_rolls_past_dates_forward(BillingCycle cycle, string expected)
    {
        var anchor = cycle == BillingCycle.Weekly ? new DateOnly(2026, 10, 2) : new DateOnly(2025, 9, 1);

        Assert.Equal(DateOnly.Parse(expected), SubscriptionMath.NextOccurrence(anchor, cycle, new DateOnly(2026, 10, 23)));
    }

    [Fact]
    public void MonthlyTotals_groups_by_currency_and_skips_inactive()
    {
        Subscription[] subs =
        [
            new() { Cost = 10, Currency = "eur" },
            new() { Cost = 120, Currency = "EUR ", Cycle = BillingCycle.Yearly },
            new() { Cost = 5, Currency = "USD" },
            new() { Cost = 99, Currency = "USD", Active = false },
        ];

        var totals = SubscriptionMath.MonthlyTotals(subs);

        Assert.Equal([new CurrencyTotal("EUR", 20), new CurrencyTotal("USD", 5)], totals);
        Assert.Equal(240, totals[0].Yearly);
    }

    [Fact]
    public void RenewingWithin_uses_next_occurrence()
    {
        var today = new DateOnly(2026, 10, 23);
        var soon = new Subscription { Name = "soon", NextRenewal = new DateOnly(2026, 9, 30) }; // → Oct 30
        var later = new Subscription { Name = "later", NextRenewal = new DateOnly(2026, 9, 10) }; // → Nov 10
        var undated = new Subscription { Name = "undated" };

        Assert.Equal([soon], SubscriptionMath.RenewingWithin([soon, later, undated], today, 14));
    }
}
