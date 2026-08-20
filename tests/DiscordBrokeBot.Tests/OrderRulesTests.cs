using DiscordBrokeBot.Features.Orders.Models;

namespace DiscordBrokeBot.Tests;

public sealed class OrderRulesTests
{
    [Fact]
    public void Financials_support_positive_zero_and_negative_balances()
    {
        var positive = OrderRules.CalculateFinancials(120, 2, 100, null);
        var zero = OrderRules.CalculateFinancials(120, 2, 240, null);
        var negative = OrderRules.CalculateFinancials(120, 2, 300, null);

        Assert.Equal(240, positive.OrderTotal);
        Assert.Equal(140, positive.Balance);
        Assert.False(positive.IsSettlementComplete);
        Assert.Equal(0, zero.Balance);
        Assert.True(zero.IsSettlementComplete);
        Assert.Equal(-60, negative.Balance);
        Assert.True(negative.IsSettlementComplete);
    }

    [Fact]
    public void Settlement_override_wins_over_automatic_amount_check()
    {
        var forcedComplete = OrderRules.CalculateFinancials(100, 1, 0, SettlementModes.ForceCompleted);
        var forcedPending = OrderRules.CalculateFinancials(100, 1, 100, SettlementModes.ForcePending);

        Assert.True(forcedComplete.IsSettlementComplete);
        Assert.False(forcedPending.IsSettlementComplete);
    }

    [Fact]
    public void Invalid_order_and_payment_values_are_rejected()
    {
        Assert.Throws<OrderRuleException>(() => OrderRules.CalculateTotal(0, 1));
        Assert.Throws<OrderRuleException>(() => OrderRules.CalculateTotal(1, 0));
        Assert.Throws<OrderRuleException>(() => OrderRules.ValidatePayment(0, "zero"));
        Assert.Throws<OrderRuleException>(() => OrderRules.ValidatePayment(1, " "));
    }

    [Fact]
    public void Money_format_is_invariant_and_keeps_negative_sign()
    {
        Assert.Equal("123456", OrderRules.Money(123456));
        Assert.Equal("-9", OrderRules.Money(-9));
    }
}
