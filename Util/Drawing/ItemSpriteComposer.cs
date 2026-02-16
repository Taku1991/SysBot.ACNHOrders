using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NHSE.Core;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SysBot.ACNHOrders
{
    public static class ItemSpriteComposer
    {
        private const int IconSize = 48;
        private const int Padding = 6;
        private const int CellSize = IconSize + Padding;
        private const int MaxColumns = 8;

        public static byte[]? ComposeItemGrid(Item[] items, string spritesPath)
        {
            if (string.IsNullOrWhiteSpace(spritesPath) || !Directory.Exists(spritesPath))
                return null;

            var grouped = GroupItems(items);
            if (grouped.Count == 0)
                return null;

            var font = LoadFont();

            int columns = Math.Min(grouped.Count, MaxColumns);
            int rows = (int)Math.Ceiling(grouped.Count / (double)MaxColumns);
            int width = columns * CellSize + Padding;
            int height = rows * CellSize + Padding;

            using var canvas = new Image<Rgba32>(width, height, new Rgba32(47, 49, 54)); // Discord dark bg

            int index = 0;
            foreach (var (itemId, count) in grouped)
            {
                int col = index % MaxColumns;
                int row = index / MaxColumns;
                int x = Padding + col * CellSize;
                int y = Padding + row * CellSize;

                var spritePath = GetSpritePath(itemId, spritesPath);
                DrawIcon(canvas, spritePath, x, y);

                if (count > 1 && font != null)
                {
                    try { DrawBadge(canvas, $"x{count}", x, y, font); }
                    catch { /* DrawText incompatible with current lib versions, skip badge */ }
                }

                index++;
            }

            using var ms = new MemoryStream();
            canvas.SaveAsPng(ms);
            return ms.ToArray();
        }

        private static List<(ushort ItemId, int Count)> GroupItems(Item[] items)
        {
            var dict = new Dictionary<ushort, int>();
            foreach (var item in items)
            {
                if (item.ItemId == Item.NONE)
                    continue;
                if (dict.ContainsKey(item.ItemId))
                    dict[item.ItemId]++;
                else
                    dict[item.ItemId] = 1;
            }
            return dict.Select(kv => (kv.Key, kv.Value)).ToList();
        }

        private static string GetSpritePath(ushort itemId, string spritesPath)
        {
            var iconType = ItemInfo.GetMenuIcon(itemId);
            if (iconType != ItemMenuIconType.Unknown)
            {
                var path = Path.Combine(spritesPath, $"{iconType}.png");
                if (File.Exists(path))
                    return path;
            }

            // Fallback: leaf.png from Misc subfolder
            var miscDir = Path.Combine(Path.GetDirectoryName(spritesPath) ?? spritesPath, "Misc");
            var leafPath = Path.Combine(miscDir, "leaf.png");
            if (File.Exists(leafPath))
                return leafPath;

            // Fallback: leaf.png in sprites path itself
            leafPath = Path.Combine(spritesPath, "leaf.png");
            if (File.Exists(leafPath))
                return leafPath;

            return string.Empty;
        }

        private static void DrawIcon(Image<Rgba32> canvas, string spritePath, int x, int y)
        {
            if (string.IsNullOrEmpty(spritePath) || !File.Exists(spritePath))
                return;

            try
            {
                using var icon = Image.Load<Rgba32>(spritePath);
                icon.Mutate(ctx => ctx.Resize(IconSize, IconSize));
                canvas.Mutate(ctx => ctx.DrawImage(icon, new Point(x, y), 1f));
            }
            catch
            {
                // Skip icons that fail to load
            }
        }

        private static void DrawBadge(Image<Rgba32> canvas, string text, int x, int y, Font font)
        {
            var shadowColor = Color.Black;
            var textColor = Color.White;

            // Position: bottom-right of the icon cell
            var pos = new PointF(x + IconSize - 2, y + IconSize - 2);
            var options = new TextGraphicsOptions
            {
                TextOptions = new TextOptions
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom
                }
            };

            // Shadow offset
            var shadowPos = new PointF(pos.X + 1, pos.Y + 1);
            canvas.Mutate(ctx => ctx.DrawText(options, text, font, shadowColor, shadowPos));
            canvas.Mutate(ctx => ctx.DrawText(options, text, font, textColor, pos));
        }

        private static Font? LoadFont()
        {
            try
            {
                var collection = new FontCollection();
                // Try Windows font paths
                var arialPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
                if (File.Exists(arialPath))
                {
                    var family = collection.Install(arialPath);
                    return family.CreateFont(12, FontStyle.Bold);
                }

                // Try Linux font paths
                var linuxFonts = new[]
                {
                    "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
                    "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf"
                };
                foreach (var fontPath in linuxFonts)
                {
                    if (File.Exists(fontPath))
                    {
                        var family = collection.Install(fontPath);
                        return family.CreateFont(12, FontStyle.Bold);
                    }
                }
            }
            catch
            {
                // Font loading failed, badges will be skipped
            }
            return null;
        }
    }
}
