using System;
using System.Linq;
using System.Text.Json;
using NHSE.Core;
using SysBot.ACNHOrders;

[SocketAPIController]
public class OrderEndpoints
{
    private class PlaceOrderArgs
    {
        public ulong userId { get; set; }
        public string userName { get; set; } = string.Empty;
        public string[] items { get; set; } = Array.Empty<string>();
    }

    private class GetQueuePositionArgs
    {
        public ulong userId { get; set; }
    }

    [SocketAPIEndpoint]
    public static object? placeOrder(string argsJson)
    {
        var args = JsonSerializer.Deserialize<PlaceOrderArgs>(argsJson);
        if (args == null)
            throw new Exception("Invalid arguments for placeOrder.");

        if (string.IsNullOrWhiteSpace(args.userName))
            throw new Exception("userName is required.");

        if (args.items == null || args.items.Length == 0)
            throw new Exception("At least one item is required.");

        // Parse hex item codes into Item objects
        var hexString = string.Join(" ", args.items);
        var cfg = Globals.Bot.Config;
        var parsedItems = ItemParser.GetItemsFromUserInput(hexString, cfg.DropConfig, ItemDestination.FieldItemDropped).ToArray();

        if (parsedItems.Length == 0)
            throw new Exception("No valid items could be parsed.");

        // Check item sanity
        if (!InternalItemTool.CurrentInstance.IsSaneAfterCorrection(parsedItems, cfg.DropConfig))
            throw new Exception("One or more items would damage the save. Order not accepted.");

        if (parsedItems.Length > MultiItem.MaxOrder)
            parsedItems = parsedItems.Take(MultiItem.MaxOrder).ToArray();

        // Create MultiItem and order notifier
        var multiOrder = new MultiItem(parsedItems, false, true, true);
        var orderId = QueueExtensions.GetNextID();
        var notifier = new SocketAPIOrderNotifier(
            multiOrder,
            multiOrder.ItemArray.Items.ToArray(),
            args.userId,
            orderId,
            args.userName,
            null
        );

        // Add to queue
        var success = QueueExtensions.AddToQueueSync(notifier, args.userName, args.userName, out var msg);

        var position = success ? Globals.Hub.Orders.Count : -1;
        var eta = position > 1 ? QueueExtensions.GetETA(position) : (position == 1 ? "Next up!" : "");

        return new
        {
            success,
            message = msg,
            position,
            orderId,
            eta
        };
    }

    [SocketAPIEndpoint]
    public static object? getQueuePosition(string argsJson)
    {
        var args = JsonSerializer.Deserialize<GetQueuePositionArgs>(argsJson);
        if (args == null)
            throw new Exception("Invalid arguments for getQueuePosition.");

        var position = QueueExtensions.GetPosition(args.userId, out _);
        var eta = position > 1 ? QueueExtensions.GetETA(position) : (position == 1 ? "Next up!" : "");

        return new
        {
            position,
            eta
        };
    }

    [SocketAPIEndpoint]
    public static object? getQueueCount(string argsJson)
    {
        var count = Globals.Hub.Orders.Count;
        var bot = Globals.Bot;
        var dodoCode = bot.DodoCode ?? "";
        var islandName = bot.TownName ?? "";

        return new
        {
            count,
            dodoCode,
            islandName
        };
    }
}
