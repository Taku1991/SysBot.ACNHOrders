using Discord;
using Discord.WebSocket;
using NHSE.Core;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SysBot.ACNHOrders
{
    public class OrderRequest<T> : IACNHOrderNotifier<T> where T : Item, new()
    {
        public MultiItem ItemOrderData { get; }
        public ulong UserGuid { get; }
        public ulong OrderID { get; }
        public string VillagerName { get; }
        private SocketUser Trader { get; }
        private ISocketMessageChannel CommandSentChannel { get; }
        public Action<CrossBot>? OnFinish { private get; set; }
        public T[] Order { get; }
        public VillagerRequest? VillagerOrder { get; }

        public OrderRequest(MultiItem data, T[] order, ulong user, ulong orderId, SocketUser trader, ISocketMessageChannel commandSentChannel, VillagerRequest? vil)
        {
            ItemOrderData = data;
            UserGuid = user;
            OrderID = orderId;
            Trader = trader;
            CommandSentChannel = commandSentChannel;
            Order = order;
            VillagerName = trader.Username;
            VillagerOrder = vil;
        }

        public void OrderCancelled(CrossBot routine, string msg, bool faulted)
        {
            OnFinish?.Invoke(routine);
            var embed = new EmbedBuilder()
                .WithTitle("Bestellung abgebrochen")
                .WithDescription(msg)
                .WithColor(Color.Red)
                .WithCurrentTimestamp()
                .Build();
            Trader.SendMessageAsync(embed: embed);
            if (!faulted)
                CommandSentChannel.SendMessageAsync($"{Trader.Mention} - Deine Bestellung wurde abgebrochen.", embed: embed);
        }

        public void OrderInitializing(CrossBot routine, string msg)
        {
            var embed = new EmbedBuilder()
                .WithTitle("Deine Bestellung startet!")
                .WithDescription(
                    "Bitte stelle sicher, dass dein **Inventar leer** ist.\n" +
                    "Sprich dann mit **Bodo** am Flughafen und bleib auf dem **Dodo-Code-Eingabebildschirm**.\n" +
                    "Ich schicke dir gleich den Dodo-Code!")
                .WithColor(Color.Blue)
                .WithCurrentTimestamp()
                .Build();
            Trader.SendMessageAsync(embed: embed);
        }

        public void OrderReady(CrossBot routine, string msg, string dodo)
        {
            try
            {
                var builder = new EmbedBuilder()
                    .WithTitle("Dodo-Code bereit!")
                    .WithDescription($"Ich warte auf dich, {Trader.Mention}!")
                    .AddField("Dodo-Code", $"```{dodo}```", inline: true)
                    .AddField("Insel", routine.TownName, inline: true)
                    .WithColor(Color.Gold)
                    .WithCurrentTimestamp();

                var spriteData = ItemSpriteComposer.ComposeItemGrid(Order, Globals.Bot.Config.OrderConfig.SpritesPath);
                if (spriteData != null)
                {
                    builder.WithImageUrl("attachment://items.png");
                    var embed = builder.Build();
                    using var stream = new MemoryStream(spriteData);
                    Trader.SendFileAsync(stream, "items.png", embed: embed);
                }
                else
                {
                    Trader.SendMessageAsync(embed: builder.Build());
                }
            }
            catch (Exception e)
            {
                LogUtil.LogError("Dodo-Code senden fehlgeschlagen: " + e.Message + "\n" + e.StackTrace, "Discord");
            }
        }

        public void OrderFinished(CrossBot routine, string msg)
        {
            OnFinish?.Invoke(routine);
            var embed = new EmbedBuilder()
                .WithTitle("Bestellung abgeschlossen!")
                .WithDescription("Danke für deine Bestellung! Viel Spaß mit deinen Items!")
                .WithColor(Color.Green)
                .WithCurrentTimestamp()
                .Build();
            Trader.SendMessageAsync(embed: embed);
        }

        public void SendNotification(CrossBot routine, string msg)
        {
            Trader.SendMessageAsync(msg);
        }

        /// <summary>
        /// Erstellt eine formatierte Item-Liste aus den bestellten Items.
        /// </summary>
        public static string GetPokemon_ItemListString(IReadOnlyCollection<Item> items)
        {
            var deStrings = GameInfo.GetStrings("de");
            var grouped = new Dictionary<string, int>();
            foreach (var item in items)
            {
                if (item.ItemId == Item.NONE)
                    continue;
                var name = deStrings.GetItemName(item);
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                if (grouped.ContainsKey(name))
                    grouped[name]++;
                else
                    grouped[name] = 1;
            }

            if (grouped.Count == 0)
                return "Keine Items";

            var lines = grouped.Select(kv => kv.Value > 1 ? $"• {kv.Key} x{kv.Value}" : $"• {kv.Key}");
            return string.Join("\n", lines);
        }
    }
}
