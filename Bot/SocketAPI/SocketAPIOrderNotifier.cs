using NHSE.Core;
using System;

namespace SysBot.ACNHOrders
{
    public class SocketAPIOrderNotifier : IACNHOrderNotifier<Item>
    {
        public Item[] Order { get; }
        public VillagerRequest? VillagerOrder { get; }
        public ulong UserGuid { get; }
        public ulong OrderID { get; }
        public string VillagerName { get; }
        public Action<CrossBot>? OnFinish { private get; set; }

        public MultiItem ItemOrderData { get; }

        // Status properties that can be read by the API
        public string? LastDodoCode { get; private set; }
        public string? LastStatusMessage { get; private set; }
        public string OrderStatus { get; private set; } = "queued";

        public SocketAPIOrderNotifier(MultiItem data, Item[] order, ulong userId, ulong orderId, string userName, VillagerRequest? vil)
        {
            ItemOrderData = data;
            Order = order;
            UserGuid = userId;
            OrderID = orderId;
            VillagerName = userName;
            VillagerOrder = vil;
        }

        public void OrderInitializing(CrossBot routine, string msg)
        {
            OrderStatus = "initializing";
            LastStatusMessage = msg;
            BroadcastEvent("orderInitializing", new
            {
                userId = UserGuid,
                orderId = OrderID,
                message = $"Your order is starting! Please ensure your inventory is empty, then go talk to Orville and stay on the Dodo code entry screen. {msg}"
            });
        }

        public void OrderReady(CrossBot routine, string msg, string dodo)
        {
            OrderStatus = "ready";
            LastDodoCode = dodo;
            LastStatusMessage = msg;
            BroadcastEvent("orderReady", new
            {
                userId = UserGuid,
                orderId = OrderID,
                dodoCode = dodo,
                message = $"Your Dodo code is: {dodo}. {msg}"
            });
        }

        public void OrderCancelled(CrossBot routine, string msg, bool faulted)
        {
            OrderStatus = "cancelled";
            LastStatusMessage = msg;
            OnFinish?.Invoke(routine);
            BroadcastEvent("orderCancelled", new
            {
                userId = UserGuid,
                orderId = OrderID,
                message = msg,
                faulted
            });
        }

        public void OrderFinished(CrossBot routine, string msg)
        {
            OrderStatus = "finished";
            LastStatusMessage = msg;
            OnFinish?.Invoke(routine);
            BroadcastEvent("orderFinished", new
            {
                userId = UserGuid,
                orderId = OrderID,
                message = msg
            });
        }

        public void SendNotification(CrossBot routine, string msg)
        {
            LastStatusMessage = msg;
            BroadcastEvent("orderNotification", new
            {
                userId = UserGuid,
                orderId = OrderID,
                message = msg
            });
        }

        private static void BroadcastEvent(string eventName, object data)
        {
            try
            {
                var message = SocketAPI.SocketAPIMessage.FromValue(new { @event = eventName, data });
                SocketAPI.SocketAPIServer.shared.BroadcastEvent(message);
            }
            catch (Exception ex)
            {
                SocketAPI.Logger.LogError($"Failed to broadcast event {eventName}: {ex.Message}");
            }
        }
    }
}
