using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord.Net;
using NHSE.Core;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SysBot.ACNHOrders
{
    public static class QueueExtensions
    {
        const int ArriveTime = 90;
        const int SetupTime = 95;

        public static async Task AddToQueueAsync(this SocketCommandContext Context, OrderRequest<Item> itemReq, string player, SocketUser trader)
        {
            IUserMessage test;
            try
            {
                var dmEmbed = new EmbedBuilder()
                    .WithTitle("In die Warteschlange aufgenommen!")
                    .WithDescription("Ich schreibe dir hier, sobald deine Bestellung bereit ist.")
                    .WithColor(Color.Blue)
                    .Build();
                test = await trader.SendMessageAsync(embed: dmEmbed).ConfigureAwait(false);
            }
            catch (HttpException ex)
            {
                await Context.Channel.SendMessageAsync($"{ex.HttpCode}: {ex.Reason}!").ConfigureAwait(false);
                var noAccessMsg = Context.User == trader
                    ? "Du musst Direktnachrichten aktivieren, um in die Warteschlange zu kommen!"
                    : $"{player} muss Direktnachrichten aktivieren!";
                await Context.Channel.SendMessageAsync(noAccessMsg).ConfigureAwait(false);
                return;
            }

            var result = AttemptAddToQueue(itemReq, trader.Mention, trader.Username, out var msg, out int position, out string eta);

            if (result)
            {
                // Bestellungs-Embed mit Item-Liste erstellen
                var itemList = OrderRequest<Item>.GetPokemon_ItemListString(itemReq.Order);
                var spriteData = ItemSpriteComposer.ComposeItemGrid(itemReq.Order, Globals.Bot.Config.OrderConfig.SpritesPath);
                bool hasImage = spriteData != null;
                var embed = BuildOrderEmbed(itemReq, itemList, position, eta, trader, hasImage);

                if (hasImage)
                {
                    using var channelStream = new MemoryStream(spriteData!);
                    await Context.Channel.SendFileAsync(channelStream, "items.png", embed: embed).ConfigureAwait(false);
                    using var dmStream = new MemoryStream(spriteData!);
                    await trader.SendFileAsync(dmStream, "items.png", embed: embed).ConfigureAwait(false);
                }
                else
                {
                    await Context.Channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
                    await trader.SendMessageAsync(embed: embed).ConfigureAwait(false);
                }

                if (!Context.IsPrivate)
                    await Context.Message.DeleteAsync(RequestOptions.Default).ConfigureAwait(false);
            }
            else
            {
                await Context.Channel.SendMessageAsync(msg).ConfigureAwait(false);
                await test.DeleteAsync().ConfigureAwait(false);
            }
        }

        private static Embed BuildOrderEmbed(OrderRequest<Item> itemReq, string itemList, int position, string eta, SocketUser trader, bool hasImage = false)
        {
            var builder = new EmbedBuilder()
                .WithTitle("Bestellung aufgenommen!")
                .WithColor(Color.Green)
                .WithCurrentTimestamp();

            builder.AddField("Besteller", trader.Mention, inline: true);
            builder.AddField("Position", $"**{position}**", inline: true);

            if (!string.IsNullOrEmpty(eta))
                builder.AddField("Wartezeit", eta, inline: true);

            if (Globals.Bot.Config.OrderConfig.ShowIDs)
                builder.AddField("Bestell-ID", $"#{itemReq.OrderID}", inline: true);

            // Item-Liste (max 1024 Zeichen für ein Embed-Field)
            if (itemList.Length > 1024)
                itemList = itemList[..1020] + "\n...";
            builder.AddField($"Bestellte Items", itemList, inline: false);

            if (itemReq.VillagerOrder != null)
            {
                var villagerName = GameInfo.Strings.GetVillager(itemReq.VillagerOrder.GameName);
                builder.AddField("Bewohner", $"{villagerName} wartet auf der Insel auf dich!", inline: false);
            }

            if (hasImage)
                builder.WithImageUrl("attachment://items.png");

            builder.WithFooter("Du erhältst eine DM mit dem Dodo-Code, sobald du dran bist.");

            return builder.Build();
        }

        public static bool AddToQueueSync(IACNHOrderNotifier<Item> itemReq, string playerMention, string playerNameId, out string msg)
        {
            var result = AttemptAddToQueue(itemReq, playerMention, playerNameId, out var msge, out _, out _);
            msg = msge;
            return result;
        }

        private static bool AttemptAddToQueue(IACNHOrderNotifier<Item> itemReq, string traderMention, string traderDispName, out string msg, out int position, out string eta)
        {
            var orders = Globals.Hub.Orders;
            position = -1;
            eta = string.Empty;

            var existingOrder = orders.GetByUserId(itemReq.UserGuid);
            if (existingOrder != null)
            {
                msg = $"{traderMention} - Du bist bereits in der Warteschlange.";
                return false;
            }

            if(Globals.Bot.CurrentUserName == traderDispName)
            {
                msg = $"{traderMention} - Deine Bestellung wird gerade bearbeitet. Bitte warte einen Moment.";
                return false;
            }

            position = orders.Count + 1;
            var idToken = Globals.Bot.Config.OrderConfig.ShowIDs ? $" (ID {itemReq.OrderID})" : string.Empty;
            msg = $"{traderMention} - Zur Warteschlange hinzugefügt{idToken}. Position: **{position}**";

            if (position > 1)
            {
                eta = GetETA(position);
                msg += $". Voraussichtliche Wartezeit: {eta}";
            }
            else
            {
                eta = "Du bist als Nächstes dran!";
                msg += ". Deine Bestellung startet nach der aktuellen!";
            }

            if (itemReq.VillagerOrder != null)
                msg += $". {GameInfo.Strings.GetVillager(itemReq.VillagerOrder.GameName)} wartet auf der Insel auf dich.";

            Globals.Hub.Orders.Enqueue(itemReq);

            return true;
        }

        public static int GetPosition(ulong id, out OrderRequest<Item>? order)
        {
            var orders = Globals.Hub.Orders;
            var position = orders.GetPosition(id);

            if (position > 0)
            {
                var found = orders.GetByUserId(id);
                if (found is OrderRequest<Item> oreq)
                {
                    order = oreq;
                    return position;
                }
            }

            order = null;
            return -1;
        }

        public static string GetETA(int pos)
        {
            int minSeconds = ArriveTime + SetupTime + Globals.Bot.Config.OrderConfig.UserTimeAllowed + Globals.Bot.Config.OrderConfig.WaitForArriverTime;
            int addSeconds = ArriveTime + Globals.Bot.Config.OrderConfig.UserTimeAllowed + Globals.Bot.Config.OrderConfig.WaitForArriverTime;
            var timeSpan = TimeSpan.FromSeconds(minSeconds + (addSeconds * (pos-1)));
            if (timeSpan.Hours > 0)
                return string.Format("{0:D2}h:{1:D2}m:{2:D2}s", timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds);
            else
                return string.Format("{0:D2}m:{1:D2}s", timeSpan.Minutes, timeSpan.Seconds);
        }

        private static ulong ID = 0;
        private static object IDAccessor = new();
        public static ulong GetNextID()
        {
            lock(IDAccessor)
            {
                return ID++;
            }
        }

        public static void ClearQueue<T>(this ConcurrentQueue<T> queue)
        {
            T item;
#pragma warning disable CS8600
            while (queue.TryDequeue(out item)) { }
#pragma warning restore CS8600
        }

        public static string GetQueueString()
        {
            var orders = Globals.Hub.Orders;
            var orderArray = orders.ToArray();
            string orderString = string.Empty;
            foreach (var ord in orderArray)
                orderString += $"{ord.VillagerName} \r\n";

            return orderString;
        }
    }
}
