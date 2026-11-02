using Visionary.Sim;
using Visionary.Sim.Commands;

namespace Visionary.Presentation.Tests;

public class NotesAndItemNameTests
{
    [Fact]
    public void ItemName_ZeroToEight_AreTheExpectedDistinctNames()
    {
        string[] expected = ["穀物", "木材", "鉄鉱石", "木炭", "小麦粉", "薪", "パン", "ビール", "工具"];

        for (int itemId = 0; itemId < expected.Length; itemId++)
        {
            Assert.Equal(expected[itemId], Labels.ItemName(itemId));
        }

        Assert.Equal(expected.Length, expected.Distinct().Count());
    }

    [Fact]
    public void ItemName_OutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Labels.ItemName(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Labels.ItemName(9));
    }

    [Fact]
    public void Inventory_ListsOnlyHeldItemsInIdOrder_OrNashiWhenEmpty()
    {
        Assert.Equal("小麦粉 3、薪 5", Notes.Inventory([0, 0, 0, 0, 3, 5, 0, 0, 0]));
        Assert.Equal("なし", Notes.Inventory([0, 0, 0, 0, 0, 0, 0, 0, 0]));
    }

    [Fact]
    public void Trade_Accepted_WritesItemQuantityPriceAndVerbPerDirection()
    {
        var result = new Result
        {
            Status = CommandStatus.Accepted,
            Quantity = 19,
            UnitPrice = 1,
            Payment = 19,
            Gratitude = Gratitude.DeepGratitude,
        };

        Assert.Equal("薪 × 19 を単価 1 で売った(計 19)", Notes.Trade(PlayerTradeKind.SellToHousehold, 5, result));
        Assert.Equal("薪 × 19 を単価 1 で売った(計 19)", Notes.Trade(PlayerTradeKind.SellToWindow, 5, result));
        Assert.Equal("薪 × 19 を単価 1 で買った(計 19)", Notes.Trade(PlayerTradeKind.BuyFromHousehold, 5, result));
        Assert.Equal("薪 × 19 を単価 1 で買った(計 19)", Notes.Trade(PlayerTradeKind.BuyFromWindow, 5, result));
    }

    [Fact]
    public void Trade_GratitudeIsNotRead()
    {
        var deep = new Result { Status = CommandStatus.Accepted, Quantity = 2, UnitPrice = 3, Payment = 6, Gratitude = Gratitude.DeepGratitude };
        var none = deep with { Gratitude = null };

        Assert.Equal(
            Notes.Trade(PlayerTradeKind.SellToHousehold, 5, none),
            Notes.Trade(PlayerTradeKind.SellToHousehold, 5, deep));
    }

    [Fact]
    public void Trade_Rejected_WritesOnlyTheStatusLabel_EvenForOutOfRangeItem()
    {
        var over = new Result { Status = CommandStatus.OverBudget };
        var invalid = new Result { Status = CommandStatus.InvalidArgument };

        Assert.Equal(Labels.Of(CommandStatus.OverBudget), Notes.Trade(PlayerTradeKind.SellToHousehold, 5, over));
        Assert.Equal(Labels.Of(CommandStatus.InvalidArgument), Notes.Trade(PlayerTradeKind.BuyFromWindow, 99, invalid));
    }

    [Fact]
    public void Trade_UnknownKind_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Notes.Trade((PlayerTradeKind)99, 5, new Result { Status = CommandStatus.Accepted }));
    }
}
