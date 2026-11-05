// The demoscene-style show behind the arcade's game list: stars, drifting items, a snake of coins and a text scroller.
// Everything moves as a function of the clock, so it needs no per-frame state and can stop and resume at any time.
// The art is optional. Without assets\arcade the same show runs with simple built-in shapes.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CustomWidgets
{
    public class ArcadeDemo
    {
        const double W = ArcadeGame.ViewWidth, H = ArcadeGame.ViewHeight;
        const double FramesPerSecond = 20, WrapMargin = 40;

        const int StarCount = 44, SparkleCount = 5;
        const int SnakeLength = 11;
        const double SnakeSpeed = 34, SnakeSpacing = 15, SnakeCenterY = 62, SnakeHeight = 104, SnakeWaves = 2.2;
        const double ScrollerSpeed = 42, ScrollerY = 128, ScrollerHeight = 5, ScrollerSize = 12, ScrollerAdvance = 6.6;
        const double TitleSize = 18, TitleAdvance = 9.9, TitleY = 5, TitleHeight = 2.5;

        const string CoinGroup = "coin_gold_small/outline_black_outside";
        static readonly string[] DriftGroups =
        {
            "gem_A_red_small/outline_black_outside", "gem_B_blue_small/outline_black_outside",
            "gem_C_green_small/outline_black_outside", "coin_blue_small/outline_black_outside",
            "coin_red_small/outline_black_outside", "gem_A_yellow_large/outline_black_outside",
            "spike_ball_A/outline_black_outside", "star_large/outline_black_outside"
        };
        const string SparkleGroup = "sparkle_A_small";

        static readonly Brush[] Rainbow =
        {
            ArcadePalette.Red, ArcadePalette.Orange, ArcadePalette.Yellow,
            ArcadePalette.Green, ArcadePalette.Cyan, ArcadePalette.Magenta
        };
        static readonly ArcadeSprite PlainCoin = new ArcadeSprite(
            "...XXXXXX...", ".XXXXXXXXXX.", ".XXXXXXXXXX.", "XXXXXXXXXXXX", "XXXXXXXXXXXX",
            "XXXXXXXXXXXX", "XXXXXXXXXXXX", ".XXXXXXXXXX.", ".XXXXXXXXXX.", "...XXXXXX...");

        class Star { public double X, Y, Speed; public Brush Brush; }

        class Drift { public string Group; public double X, Y, Speed, Bob, Phase; }

        public bool HasArt { get { return frames.Count > 0; } }
        public double Time { get; private set; }

        readonly Dictionary<string, ImageSource[]> frames = new Dictionary<string, ImageSource[]>();
        readonly List<Star> stars = new List<Star>();
        readonly List<Drift> drifts = new List<Drift>();
        readonly List<Drift> sparkles = new List<Drift>();
        readonly string message;

        public ArcadeDemo(string assetFolder, IEnumerable<string> gameTitles, int seed)
        {
            LoadArt(assetFolder);

            var random = new Random(seed);
            var dim = new[] { ArcadePalette.Grey, ArcadePalette.White, ArcadePalette.Cyan };
            for (int i = 0; i < StarCount; i++)
            {
                int layer = i % dim.Length;
                stars.Add(new Star { X = random.NextDouble() * W, Y = random.NextDouble() * H, Speed = 6 + layer * 12, Brush = dim[layer] });
            }

            int count = DriftGroups.Length;
            for (int i = 0; i < count; i++)
            {
                drifts.Add(new Drift
                {
                    Group = DriftGroups[i],
                    X = (W + WrapMargin) * i / count,
                    Y = 18 + random.NextDouble() * 90,
                    Speed = 12 + random.NextDouble() * 18,
                    Bob = 6 + random.NextDouble() * 12,
                    Phase = random.NextDouble() * Math.PI * 2
                });
            }

            for (int i = 0; i < SparkleCount; i++)
            {
                sparkles.Add(new Drift { X = random.NextDouble() * W, Y = random.NextDouble() * (H - 40) + 10, Phase = random.NextDouble() * 10, Speed = 0.7 + random.NextDouble() });
            }

            message = "WELCOME TO THE MINI ARCADE ... " + string.Join(" * ", gameTitles) +
                      " ... PICK A GAME AND PRESS SPACE ... " +
                      (HasArt ? "PIXEL ART BY WILL TICE / UNTIED GAMES ... " : "") +
                      "NOW SCROLLING ON A DESKTOP NEAR YOU ...      ";
        }

        void LoadArt(string folder)
        {
            string sheetPath = Path.Combine(folder, "spritesheet.png"), indexPath = Path.Combine(folder, "spritesheet.txt");
            if (!File.Exists(sheetPath) || !File.Exists(indexPath)) return;

            try
            {
                var sheet = new BitmapImage();
                sheet.BeginInit();
                sheet.CacheOption = BitmapCacheOption.OnLoad; // leaves the file unlocked
                sheet.UriSource = new Uri(Path.GetFullPath(sheetPath));
                sheet.EndInit();
                sheet.Freeze();

                var wanted = new HashSet<string>(DriftGroups) { CoinGroup, SparkleGroup };
                var found = new Dictionary<string, List<KeyValuePair<string, ImageSource>>>();
                foreach (var line in File.ReadLines(indexPath))
                {
                    // Each line reads: group/name = x y width height
                    var parts = line.Split(new[] { " = " }, StringSplitOptions.None);
                    if (parts.Length != 2) continue;
                    int slash = parts[0].LastIndexOf('/');
                    if (slash < 0) continue;
                    string group = parts[0].Substring(0, slash);
                    if (!wanted.Contains(group)) continue;

                    var box = parts[1].Split(' ').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                    var frame = new CroppedBitmap(sheet, new Int32Rect(box[0], box[1], box[2], box[3]));
                    frame.Freeze();
                    if (!found.ContainsKey(group)) found[group] = new List<KeyValuePair<string, ImageSource>>();
                    found[group].Add(new KeyValuePair<string, ImageSource>(parts[0], frame));
                }
                foreach (var group in found)
                    frames[group.Key] = group.Value.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => f.Value).ToArray();
            }
            catch (Exception e)
            {
                if (e is OutOfMemoryException) throw;
                frames.Clear(); // a damaged art file shouldn't stop the arcade
                Host.Log("Arcade demo art: " + e.Message);
            }
        }

        public void Update(double seconds) { Time += seconds; }

        static double Wrap(double x, double span)
        {
            x %= span;
            return x < 0 ? x + span : x;
        }

        void DrawFrame(DrawingContext dc, string group, double x, double y, double phase)
        {
            ImageSource[] set;
            if (!frames.TryGetValue(group, out set) || set.Length == 0) return;
            var frame = set[(int)((Time + phase) * FramesPerSecond) % set.Length];
            dc.DrawImage(frame, new Rect(Math.Round(x), Math.Round(y), frame.Width, frame.Height));
        }

        /// <summary>Everything behind the game list.</summary>
        public void DrawBackground(DrawingContext dc)
        {
            foreach (var star in stars)
                dc.DrawRectangle(star.Brush, null, new Rect(Math.Round(Wrap(star.X - star.Speed * Time, W)), Math.Round(star.Y), 1, 1));

            if (HasArt)
            {
                foreach (var drift in drifts)
                {
                    double x = Wrap(drift.X - drift.Speed * Time, W + WrapMargin) - WrapMargin;
                    double y = drift.Y + Math.Sin(Time * 1.3 + drift.Phase) * drift.Bob;
                    DrawFrame(dc, drift.Group, x, y, drift.Phase);
                }
            }

            for (int i = 0; i < SnakeLength; i++)
            {
                double x = W - Wrap(Time * SnakeSpeed + i * SnakeSpacing, W + WrapMargin) + WrapMargin / 2 - 10;
                double y = SnakeCenterY + Math.Sin(x / W * Math.PI * 2 * SnakeWaves + Time * 2) * SnakeHeight / 2 - 9;
                if (HasArt) DrawFrame(dc, CoinGroup, x, y, i * 0.1);
                else PlainCoin.Draw(dc, x, y, ArcadePalette.Yellow);
            }

            if (HasArt)
            {
                ImageSource[] set;
                if (frames.TryGetValue(SparkleGroup, out set))
                {
                    foreach (var sparkle in sparkles)
                    {
                        // Each sparkle stays in one place and plays at its own pace.
                        var frame = set[(int)((Time * sparkle.Speed + sparkle.Phase) * FramesPerSecond) % set.Length];
                        dc.DrawImage(frame, new Rect(Math.Round(sparkle.X), Math.Round(sparkle.Y), frame.Width, frame.Height));
                    }
                }
            }
        }

        public void DrawTitle(DrawingContext dc, ArcadeText text, string title)
        {
            double left = (W - title.Length * TitleAdvance) / 2;
            for (int i = 0; i < title.Length; i++)
            {
                if (title[i] == ' ') continue;
                double y = TitleY + Math.Sin(Time * 3 + i * 0.6) * TitleHeight;
                var brush = Rainbow[(int)(Time * 3 + i) % Rainbow.Length];
                text.Draw(dc, title[i].ToString(), left + i * TitleAdvance, y, TitleSize, brush, TextAlign.Left);
            }
        }

        /// <summary>The rainbow message that waves its way across the bottom of the screen.</summary>
        public void DrawScroller(DrawingContext dc, ArcadeText text)
        {
            double travelled = Time * ScrollerSpeed;
            int first = (int)Math.Floor(travelled / ScrollerAdvance);
            int shown = (int)(W / ScrollerAdvance) + 2;
            for (int n = first; n < first + shown; n++)
            {
                char letter = message[n % message.Length];
                if (letter == ' ') continue;
                double x = n * ScrollerAdvance - travelled;
                double y = ScrollerY + Math.Sin(x * 0.06 + Time * 3) * ScrollerHeight;
                text.Draw(dc, letter.ToString(), x, y, ScrollerSize, Rainbow[n % Rainbow.Length], TextAlign.Left);
            }
        }
    }
}
