// The mini arcade: ArcadeView hosts a game picker and the games, and each game derives from ArcadeGame.
// The view only runs its game loop during play, so an idle arcade uses no CPU.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CustomWidgets
{
    public class ArcadeInput
    {
        public bool Left, Right, Up, Down, Fire;
        /// <summary>True for one update after Space or Enter is pressed.</summary>
        public bool Action;

        public void Clear() { Left = Right = Up = Down = Fire = Action = false; }
    }

    public enum ArcadeState { Title, Playing, PlayerHit, WaveClear, GameOver }

    /// <summary>A game draws in game pixels on a 200 by 150 screen, updating 60 times a second.</summary>
    public abstract class ArcadeGame
    {
        public const double ViewWidth = 200, ViewHeight = 150;

        public abstract string Id { get; }
        public abstract void Update(double seconds, ArcadeInput input);
        public abstract void Draw(DrawingContext dc, ArcadeText text);

        public ArcadeState State { get; protected set; }
        public int Score { get; protected set; }
        public int Lives { get; protected set; }
        public int HighScore { get; set; }

        /// <summary>Raised when a finished game beats the high score.</summary>
        public event Action<int> HighScoreBeaten;

        protected void RecordHighScore()
        {
            if (Score <= HighScore) return;
            HighScore = Score;
            var handler = HighScoreBeaten;
            if (handler != null) handler(Score);
        }
    }

    public static class ArcadePalette
    {
        public static readonly Brush White = Make(Colors.White);
        public static readonly Brush Black = Make(Colors.Black);
        public static readonly Brush Dim = Make(Color.FromArgb(0xB0, 0, 0, 0));
        public static readonly Brush Green = Make(Color.FromRgb(0x4C, 0xFF, 0x6A));
        public static readonly Brush Red = Make(Color.FromRgb(0xFF, 0x4C, 0x4C));
        public static readonly Brush Grey = Make(Color.FromRgb(0x9A, 0xA1, 0xB2));
        public static readonly Brush Cyan = Make(Color.FromRgb(0x5C, 0xE1, 0xFF));
        public static readonly Brush Yellow = Make(Color.FromRgb(0xFF, 0xE0, 0x4C));
        public static readonly Brush Orange = Make(Color.FromRgb(0xFF, 0x9A, 0x3C));
        public static readonly Brush Magenta = Make(Color.FromRgb(0xFF, 0x5C, 0xD6));

        static Brush Make(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    public static class ArcadeMath
    {
        public static bool Overlaps(double ax, double ay, double aw, double ah, double bx, double by, double bw, double bh)
        {
            return ax < bx + bw && bx < ax + aw && ay < by + bh && by < ay + ah;
        }
    }

    /// <summary>Small square sparks for explosions.</summary>
    public class ArcadeSparks
    {
        class Spark { public double X, Y, Vx, Vy, Left; public Brush Brush; }

        readonly List<Spark> sparks = new List<Spark>();
        readonly Random random;

        public ArcadeSparks(Random random) { this.random = random; }

        public void Burst(double x, double y, Brush brush, int count)
        {
            for (int i = 0; i < count; i++)
            {
                double angle = random.NextDouble() * Math.PI * 2, speed = 15 + random.NextDouble() * 45;
                sparks.Add(new Spark { X = x, Y = y, Vx = Math.Cos(angle) * speed, Vy = Math.Sin(angle) * speed, Left = 0.25 + random.NextDouble() * 0.3, Brush = brush });
            }
        }

        public void Update(double seconds)
        {
            foreach (var s in sparks)
            {
                s.X += s.Vx * seconds;
                s.Y += s.Vy * seconds;
                s.Left -= seconds;
            }
            sparks.RemoveAll(s => s.Left <= 0);
        }

        public void Clear() { sparks.Clear(); }

        /// <summary>offsetX moves sparks from world to screen in scrolling games.</summary>
        public void Draw(DrawingContext dc, Func<double, double> toScreenX)
        {
            foreach (var s in sparks) dc.DrawRectangle(s.Brush, null, new Rect(Math.Round(toScreenX(s.X)), Math.Round(s.Y), 1, 1));
        }
    }

    public enum TextAlign { Left, Center, Right }

    /// <summary>Draws text, reusing layouts because a game draws the same strings every frame.</summary>
    public class ArcadeText
    {
        const int MaxCached = 200;
        static readonly Typeface Face = new Typeface("Consolas");
        readonly Dictionary<string, FormattedText> cache = new Dictionary<string, FormattedText>();
        readonly double pixelsPerDip;

        public ArcadeText(double pixelsPerDip) { this.pixelsPerDip = pixelsPerDip; }

        public void Draw(DrawingContext dc, string s, double x, double y, double size, Brush brush, TextAlign align)
        {
            var key = s + "|" + size + "|" + brush.GetHashCode();
            FormattedText layout;
            if (!cache.TryGetValue(key, out layout))
            {
                if (cache.Count >= MaxCached) cache.Clear();
                layout = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush, pixelsPerDip);
                cache[key] = layout;
            }
            if (align == TextAlign.Center) x -= layout.Width / 2;
            else if (align == TextAlign.Right) x -= layout.Width;
            dc.DrawText(layout, new Point(x, y));
        }
    }

    /// <summary>A pixel-art image built from rows of text, where X is a filled pixel.</summary>
    public class ArcadeSprite
    {
        public readonly int Width, Height;
        readonly Geometry geometry;

        public ArcadeSprite(params string[] rows)
        {
            Width = rows[0].Length;
            Height = rows.Length;
            geometry = ToGeometry(Width, Height, (x, y) => rows[y][x] == 'X');
        }

        public void Draw(DrawingContext dc, double x, double y, Brush brush)
        {
            dc.PushTransform(new TranslateTransform(Math.Round(x), Math.Round(y)));
            dc.DrawGeometry(brush, null, geometry);
            dc.Pop();
        }

        /// <summary>The same picture facing the other way.</summary>
        public static ArcadeSprite Flipped(params string[] rows)
        {
            return new ArcadeSprite(rows.Select(r => new string(r.Reverse().ToArray())).ToArray());
        }

        public static Geometry ToGeometry(int width, int height, Func<int, int, bool> filled)
        {
            var result = new StreamGeometry();
            using (var g = result.Open())
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (!filled(x, y)) continue;
                        int start = x;
                        while (x + 1 < width && filled(x + 1, y)) x++;
                        g.BeginFigure(new Point(start, y), true, true);
                        g.PolyLineTo(new[] { new Point(x + 1, y), new Point(x + 1, y + 1), new Point(start, y + 1) }, false, false);
                    }
                }
            }
            result.Freeze();
            return result;
        }
    }

    /// <summary>Use in a widget as &lt;arcade:ArcadeView Width="400" Height="300"/&gt;.</summary>
    public class ArcadeView : FrameworkElement
    {
        const double StepSeconds = 1.0 / 60;
        const double LongestFrame = 0.1;

        class MenuEntry
        {
            public string Title, Id;
            public Func<int, ArcadeGame> Make;
        }

        /// <summary>Add a line here to put a new game in the picker.</summary>
        static readonly MenuEntry[] Menu =
        {
            new MenuEntry { Title = "INVADERS", Id = "invaders", Make = seed => new InvadersGame(seed) },
            new MenuEntry { Title = "DEFENDER", Id = "defender", Make = seed => new DefenderGame(seed) },
            new MenuEntry { Title = "CATERPILLAR", Id = "caterpillar", Make = seed => new CaterpillarGame(seed) }
        };

        readonly ArcadeInput input = new ArcadeInput();
        readonly Stopwatch clock = new Stopwatch();
        ArcadeGame game; // null while the picker is showing
        ArcadeText text;
        Settings settings;
        int selected;
        double pending;
        bool paused, looping;

        public ArcadeView()
        {
            Focusable = true;
            FocusVisualStyle = null;
            ClipToBounds = true;
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
            Cursor = Cursors.Hand;
            Loaded += OnLoaded;
            Unloaded += delegate { SetLooping(false); };
            IsVisibleChanged += delegate { UpdateLoop(); };
        }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            var data = DataContext as LiveData;
            if (data != null) settings = data.Settings;
            InvalidateVisual();
        }

        int LoadHighScore(string id)
        {
            int saved;
            return settings != null && int.TryParse(settings.Get("arcade." + id + ".high", "0"), out saved) ? saved : 0;
        }

        void SaveHighScore(string id, int score)
        {
            if (settings != null) settings.Set("arcade." + id + ".high", score.ToString(CultureInfo.InvariantCulture));
        }

        void StartSelectedGame()
        {
            var entry = Menu[selected];
            game = entry.Make(Environment.TickCount);
            game.HighScore = LoadHighScore(entry.Id);
            game.HighScoreBeaten += score => SaveHighScore(entry.Id, score);
            paused = false;
            input.Clear();
            UpdateLoop();
            InvalidateVisual();
        }

        void ReturnToMenu()
        {
            // Leaving part-way through a game still counts its score.
            if (game.Score > game.HighScore) SaveHighScore(game.Id, game.Score);
            game = null;
            paused = false;
            input.Clear();
            UpdateLoop();
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            // Clicking the screen takes the keyboard. It must not start dragging the widget.
            var window = Window.GetWindow(this);
            if (window != null) window.Activate();
            Focus();
            Keyboard.Focus(this);
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (game == null) OnMenuKey(e);
            else OnGameKey(e);
        }

        void OnMenuKey(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Up: case Key.W: selected = (selected + Menu.Length - 1) % Menu.Length; break;
                case Key.Down: case Key.S: selected = (selected + 1) % Menu.Length; break;
                case Key.Space: case Key.Enter:
                    if (!e.IsRepeat) StartSelectedGame();
                    break;
                default: return;
            }
            InvalidateVisual();
            e.Handled = true;
        }

        void OnGameKey(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left: case Key.A: input.Left = true; break;
                case Key.Right: case Key.D: input.Right = true; break;
                case Key.Up: case Key.W: input.Up = true; break;
                case Key.Down: case Key.S: input.Down = true; break;
                case Key.Space: input.Fire = true; if (!e.IsRepeat) input.Action = true; break;
                case Key.Enter: if (!e.IsRepeat) input.Action = true; break;
                case Key.P: case Key.Escape:
                    if (!e.IsRepeat) { paused = !paused; input.Clear(); UpdateLoop(); InvalidateVisual(); }
                    break;
                case Key.M:
                    if (!e.IsRepeat) ReturnToMenu();
                    break;
                default: return;
            }
            e.Handled = true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left: case Key.A: input.Left = false; break;
                case Key.Right: case Key.D: input.Right = false; break;
                case Key.Up: case Key.W: input.Up = false; break;
                case Key.Down: case Key.S: input.Down = false; break;
                case Key.Space: input.Fire = false; break;
                case Key.Enter: case Key.P: case Key.Escape: case Key.M: break;
                default: return;
            }
            e.Handled = true;
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            UpdateLoop();
            InvalidateVisual();
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            input.Clear(); // a key released while away would otherwise stay held
            UpdateLoop();
            InvalidateVisual();
        }

        void UpdateLoop() { SetLooping(game != null && IsKeyboardFocused && IsVisible && !paused); }

        void SetLooping(bool on)
        {
            if (on == looping) return;
            looping = on;
            if (on)
            {
                pending = 0;
                clock.Restart();
                CompositionTarget.Rendering += OnFrame;
            }
            else CompositionTarget.Rendering -= OnFrame;
        }

        void OnFrame(object sender, EventArgs e)
        {
            double elapsed = clock.Elapsed.TotalSeconds;
            if (elapsed < 0.001) return; // WPF can raise this more than once per frame
            clock.Restart();
            pending += Math.Min(elapsed, LongestFrame);
            while (pending >= StepSeconds)
            {
                game.Update(StepSeconds, input);
                input.Action = false;
                pending -= StepSeconds;
            }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(ArcadePalette.Black, null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (text == null) text = new ArcadeText(VisualTreeHelper.GetDpi(this).PixelsPerDip);

            const double W = ArcadeGame.ViewWidth, H = ArcadeGame.ViewHeight;
            double scale = Math.Min(ActualWidth / W, ActualHeight / H);
            dc.PushTransform(new ScaleTransform(scale, scale));
            if (game != null) game.Draw(dc, text);
            else DrawMenu(dc);
            if (!IsKeyboardFocused || paused)
            {
                dc.DrawRectangle(ArcadePalette.Dim, null, new Rect(0, 0, W, H));
                text.Draw(dc, IsKeyboardFocused ? "PAUSED" : "CLICK TO PLAY", W / 2, H / 2 - 6, 11, ArcadePalette.White, TextAlign.Center);
            }
            dc.Pop();
        }

        void DrawMenu(DrawingContext dc)
        {
            const double Center = ArcadeGame.ViewWidth / 2, Left = 32, Right = 168, RowHeight = 18, FirstRow = 52;
            text.Draw(dc, "MINI ARCADE", Center, 14, 18, ArcadePalette.Green, TextAlign.Center);
            for (int i = 0; i < Menu.Length; i++)
            {
                double y = FirstRow + i * RowHeight;
                bool isSelected = i == selected;
                var brush = isSelected ? ArcadePalette.Yellow : ArcadePalette.White;
                if (isSelected) text.Draw(dc, ">", Left - 10, y, 10, brush, TextAlign.Left);
                text.Draw(dc, Menu[i].Title, Left, y, 10, brush, TextAlign.Left);
                text.Draw(dc, LoadHighScore(Menu[i].Id).ToString("00000"), Right, y, 10, ArcadePalette.Grey, TextAlign.Right);
            }
            text.Draw(dc, "UP / DOWN TO CHOOSE", Center, 118, 8, ArcadePalette.Grey, TextAlign.Center);
            text.Draw(dc, "SPACE TO SELECT", Center, 129, 8, ArcadePalette.White, TextAlign.Center);
        }
    }

    /// <summary>A basic Space Invaders clone. All measurements are in game pixels (200 by 150).</summary>
    public class InvadersGame : ArcadeGame
    {
        const int Rows = 4, Columns = 9, ColumnStep = 16, RowStep = 12, CellWidth = 12, AlienHeight = 8;
        const int FormationCount = Rows * Columns;
        const int StartLives = 3, MaxAlienShots = 3;
        const double ScreenWidth = ViewWidth;
        const double PlayerY = 134, GroundY = 144, PlayerSpeed = 90, ShotSpeed = 170;
        const double EdgeMargin = 3, DropDistance = 5, StepDistance = 2, FormationTop = 18, WaveShift = 5;
        const double UfoY = 9, UfoSpeed = 30;
        const double HitPause = 1.2, WavePause = 1.5, RestartDelay = 1, PopSeconds = 0.25;
        const int ShieldCount = 4, ShieldWidth = 16, ShieldHeight = 12;
        const double ShieldY = 108;

        static readonly Brush WhiteBrush = ArcadePalette.White;
        static readonly Brush GreenBrush = ArcadePalette.Green;
        static readonly Brush RedBrush = ArcadePalette.Red;
        static readonly Brush GreyBrush = ArcadePalette.Grey;

        static readonly ArcadeSprite[] Squid =
        {
            new ArcadeSprite("...XX...", "..XXXX..", ".XXXXXX.", "XX.XX.XX", "XXXXXXXX", "..X..X..", ".X.XX.X.", "X.X..X.X"),
            new ArcadeSprite("...XX...", "..XXXX..", ".XXXXXX.", "XX.XX.XX", "XXXXXXXX", ".X.XX.X.", "X......X", ".X....X.")
        };
        static readonly ArcadeSprite[] Crab =
        {
            new ArcadeSprite("..X.....X..", "...X...X...", "..XXXXXXX..", ".XX.XXX.XX.", "XXXXXXXXXXX", "X.XXXXXXX.X", "X.X.....X.X", "...XX.XX..."),
            new ArcadeSprite("..X.....X..", "X..X...X..X", "X.XXXXXXX.X", "XXX.XXX.XXX", "XXXXXXXXXXX", ".XXXXXXXXX.", "..X.....X..", ".X.......X.")
        };
        static readonly ArcadeSprite[] Octopus =
        {
            new ArcadeSprite("....XXXX....", ".XXXXXXXXXX.", "XXXXXXXXXXXX", "XXX..XX..XXX", "XXXXXXXXXXXX", "...XX..XX...", "..XX.XX.XX..", "XX........XX"),
            new ArcadeSprite("....XXXX....", ".XXXXXXXXXX.", "XXXXXXXXXXXX", "XXX..XX..XXX", "XXXXXXXXXXXX", "...XXXXXX...", "..XX.XX.XX..", ".XX......XX.")
        };
        static readonly ArcadeSprite PlayerSprite = new ArcadeSprite(
            "......X......", ".....XXX.....", ".....XXX.....", ".XXXXXXXXXXX.",
            "XXXXXXXXXXXXX", "XXXXXXXXXXXXX", "XXXXXXXXXXXXX", "XXXXXXXXXXXXX");
        static readonly ArcadeSprite UfoSprite = new ArcadeSprite(
            ".....XXXXXX.....", "...XXXXXXXXXX...", "..XXXXXXXXXXXX..", ".XX.XX.XX.XX.XX.",
            "XXXXXXXXXXXXXXXX", "..XXX..XX..XXX..", "...X........X...");
        static readonly ArcadeSprite PopSprite = new ArcadeSprite(
            "X..X...X..X", ".X..X.X..X.", "..X.....X..", "XX.......XX", "..X.....X..", ".X..X.X..X.", "X..X...X..X", "...........");
        static readonly int[] RowScores = { 30, 20, 20, 10 };

        class Shot { public double X, Y; }
        class Pop { public double X, Y, Left; }

        class Shield
        {
            public int X;
            public bool[,] Pixels;
            public Geometry Cached;
        }

        public override string Id { get { return "invaders"; } }

        public int Wave { get; private set; }
        public int AliensAlive { get; private set; }
        public double PlayerX { get; private set; }
        public bool Invaded { get; private set; }

        readonly Random random;
        readonly bool[,] alive = new bool[Rows, Columns];
        readonly List<Shot> alienShots = new List<Shot>();
        readonly List<Pop> pops = new List<Pop>();
        readonly List<Shield> shields = new List<Shield>();
        Shot playerShot;
        double formationX, formationY, direction, stepTimer, fireTimer, stateTimer;
        double ufoX, ufoDirection, ufoTimer;
        bool ufoActive, frame;

        public InvadersGame(int seed)
        {
            random = new Random(seed);
            State = ArcadeState.Title;
            ResetGame();
            State = ArcadeState.Title;
        }

        void ResetGame()
        {
            Score = 0;
            Lives = StartLives;
            Wave = 1;
            Invaded = false;
            StartWave();
        }

        void StartWave()
        {
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Columns; c++)
                    alive[r, c] = true;
            AliensAlive = FormationCount;
            formationX = (ScreenWidth - ((Columns - 1) * ColumnStep + CellWidth)) / 2;
            formationY = FormationTop + Math.Min(Wave - 1, 3) * WaveShift;
            direction = 1;
            stepTimer = StepInterval();
            ClearShots();
            pops.Clear();
            ufoActive = false;
            ufoTimer = NextUfoDelay();
            fireTimer = NextFireDelay();
            PlayerX = (ScreenWidth - PlayerSprite.Width) / 2;
            BuildShields();
            State = ArcadeState.Playing;
        }

        void ClearShots()
        {
            alienShots.Clear();
            playerShot = null;
        }

        void BuildShields()
        {
            shields.Clear();
            double gap = (ScreenWidth - ShieldCount * ShieldWidth) / (ShieldCount + 1);
            for (int i = 0; i < ShieldCount; i++)
            {
                var shield = new Shield { X = (int)(gap + i * (ShieldWidth + gap)), Pixels = new bool[ShieldWidth, ShieldHeight] };
                for (int x = 0; x < ShieldWidth; x++)
                {
                    for (int y = 0; y < ShieldHeight; y++)
                    {
                        bool corner = y < 3 && (x < 3 - y || x >= ShieldWidth - (3 - y));
                        bool notch = y >= 8 && x >= 5 && x <= 10;
                        shield.Pixels[x, y] = !corner && !notch;
                    }
                }
                shields.Add(shield);
            }
        }

        double StepInterval()
        {
            double interval = 0.02 + 0.6 * AliensAlive / FormationCount;
            return Math.Max(0.03, interval / (1 + 0.1 * (Wave - 1)));
        }

        double NextFireDelay() { return (0.5 + random.NextDouble()) / (1 + 0.15 * (Wave - 1)); }

        double NextUfoDelay() { return 15 + random.NextDouble() * 10; }

        public override void Update(double seconds, ArcadeInput input)
        {
            UpdatePops(seconds);
            switch (State)
            {
                case ArcadeState.Title:
                    if (input.Action) ResetGame();
                    break;
                case ArcadeState.Playing:
                    UpdatePlayer(seconds, input);
                    UpdatePlayerShot(seconds);
                    UpdateUfo(seconds);
                    UpdateAliens(seconds);
                    UpdateAlienShots(seconds);
                    if (State == ArcadeState.Playing && AliensAlive == 0)
                    {
                        State = ArcadeState.WaveClear;
                        stateTimer = WavePause;
                        ClearShots();
                    }
                    break;
                case ArcadeState.PlayerHit:
                    stateTimer -= seconds;
                    if (stateTimer <= 0)
                    {
                        if (Lives > 0)
                        {
                            PlayerX = (ScreenWidth - PlayerSprite.Width) / 2;
                            State = ArcadeState.Playing;
                        }
                        else EndGame();
                    }
                    break;
                case ArcadeState.WaveClear:
                    stateTimer -= seconds;
                    if (stateTimer <= 0)
                    {
                        Wave++;
                        StartWave();
                    }
                    break;
                case ArcadeState.GameOver:
                    stateTimer -= seconds;
                    if (stateTimer <= 0 && input.Action) ResetGame();
                    break;
            }
        }

        void EndGame()
        {
            State = ArcadeState.GameOver;
            stateTimer = RestartDelay;
            ClearShots();
            RecordHighScore();
        }

        void UpdatePops(double seconds)
        {
            foreach (var pop in pops) pop.Left -= seconds;
            pops.RemoveAll(p => p.Left <= 0);
        }

        void AddPop(double x, double y) { pops.Add(new Pop { X = x, Y = y, Left = PopSeconds }); }

        void UpdatePlayer(double seconds, ArcadeInput input)
        {
            if (input.Left) PlayerX -= PlayerSpeed * seconds;
            if (input.Right) PlayerX += PlayerSpeed * seconds;
            PlayerX = Math.Max(EdgeMargin, Math.Min(ScreenWidth - EdgeMargin - PlayerSprite.Width, PlayerX));
            if (input.Fire && playerShot == null)
                playerShot = new Shot { X = PlayerX + PlayerSprite.Width / 2, Y = PlayerY - 3 };
        }

        void UpdatePlayerShot(double seconds)
        {
            if (playerShot == null) return;
            playerShot.Y -= ShotSpeed * seconds;
            if (playerShot.Y < 8) { playerShot = null; return; }

            if (HitShields(playerShot.X, playerShot.Y, 1, 3)) { playerShot = null; return; }

            if (ufoActive && Overlaps(playerShot.X, playerShot.Y, 1, 3, ufoX, UfoY, UfoSprite.Width, UfoSprite.Height))
            {
                Score += 50 * random.Next(1, 7);
                AddPop(ufoX + 2, UfoY);
                ufoActive = false;
                playerShot = null;
                return;
            }

            for (int r = 0; r < Rows; r++)
            {
                for (int c = 0; c < Columns; c++)
                {
                    if (!alive[r, c]) continue;
                    if (!Overlaps(playerShot.X, playerShot.Y, 1, 3, AlienX(c), AlienY(r), CellWidth, AlienHeight)) continue;
                    alive[r, c] = false;
                    AliensAlive--;
                    Score += RowScores[r];
                    AddPop(AlienX(c), AlienY(r));
                    playerShot = null;
                    return;
                }
            }

            foreach (var shot in alienShots)
            {
                if (!Overlaps(playerShot.X, playerShot.Y, 1, 3, shot.X, shot.Y, 1, 4)) continue;
                alienShots.Remove(shot);
                playerShot = null;
                return;
            }
        }

        double AlienX(int column) { return formationX + column * ColumnStep; }

        double AlienY(int row) { return formationY + row * RowStep; }

        void UpdateAliens(double seconds)
        {
            if (AliensAlive == 0) return;
            stepTimer -= seconds;
            if (stepTimer <= 0)
            {
                stepTimer = StepInterval();
                frame = !frame;
                StepFormation();
            }

            fireTimer -= seconds;
            if (fireTimer <= 0)
            {
                fireTimer = NextFireDelay();
                if (alienShots.Count < MaxAlienShots) FireFromRandomColumn();
            }
        }

        void StepFormation()
        {
            double left = double.MaxValue, right = double.MinValue, bottom = 0;
            for (int r = 0; r < Rows; r++)
            {
                for (int c = 0; c < Columns; c++)
                {
                    if (!alive[r, c]) continue;
                    left = Math.Min(left, AlienX(c));
                    right = Math.Max(right, AlienX(c) + CellWidth);
                    bottom = Math.Max(bottom, AlienY(r) + AlienHeight);
                }
            }

            double move = StepDistance * direction;
            if (left + move < EdgeMargin || right + move > ScreenWidth - EdgeMargin)
            {
                formationY += DropDistance;
                bottom += DropDistance;
                direction = -direction;
            }
            else formationX += move;

            CrushShields();
            if (bottom >= PlayerY)
            {
                Invaded = true;
                Lives = 0;
                AddPop(PlayerX + 1, PlayerY);
                EndGame();
            }
        }

        void FireFromRandomColumn()
        {
            var columns = Enumerable.Range(0, Columns).Where(c => Enumerable.Range(0, Rows).Any(r => alive[r, c])).ToList();
            if (columns.Count == 0) return;
            int column = columns[random.Next(columns.Count)];
            int row = Enumerable.Range(0, Rows).Last(r => alive[r, column]);
            alienShots.Add(new Shot { X = AlienX(column) + CellWidth / 2, Y = AlienY(row) + AlienHeight });
        }

        void UpdateAlienShots(double seconds)
        {
            double speed = 55 + 4 * Wave;
            for (int i = alienShots.Count - 1; i >= 0; i--)
            {
                var shot = alienShots[i];
                shot.Y += speed * seconds;
                if (shot.Y > GroundY || HitShields(shot.X, shot.Y, 1, 4))
                {
                    alienShots.RemoveAt(i);
                    continue;
                }
                if (State == ArcadeState.Playing && Overlaps(shot.X, shot.Y, 1, 4, PlayerX, PlayerY + 3, PlayerSprite.Width, PlayerSprite.Height - 3))
                {
                    HitPlayer();
                    return; // HitPlayer clears the shots
                }
            }
        }

        void HitPlayer()
        {
            Lives--;
            AddPop(PlayerX + 1, PlayerY);
            ClearShots();
            State = ArcadeState.PlayerHit;
            stateTimer = HitPause;
        }

        void UpdateUfo(double seconds)
        {
            if (!ufoActive)
            {
                ufoTimer -= seconds;
                if (ufoTimer > 0) return;
                ufoActive = true;
                ufoDirection = random.Next(2) == 0 ? 1 : -1;
                ufoX = ufoDirection > 0 ? -UfoSprite.Width : ScreenWidth;
                return;
            }
            ufoX += UfoSpeed * ufoDirection * seconds;
            if (ufoX < -UfoSprite.Width || ufoX > ScreenWidth)
            {
                ufoActive = false;
                ufoTimer = NextUfoDelay();
            }
        }

        static bool Overlaps(double ax, double ay, double aw, double ah, double bx, double by, double bw, double bh)
        {
            return ArcadeMath.Overlaps(ax, ay, aw, ah, bx, by, bw, bh);
        }

        bool HitShields(double x, double y, double w, double h)
        {
            foreach (var shield in shields)
            {
                int left = (int)Math.Floor(x) - shield.X, right = (int)Math.Ceiling(x + w) - shield.X;
                int top = (int)Math.Floor(y - ShieldY), end = (int)Math.Ceiling(y + h - ShieldY);
                for (int px = Math.Max(0, left); px < Math.Min(ShieldWidth, right); px++)
                {
                    for (int py = Math.Max(0, top); py < Math.Min(ShieldHeight, end); py++)
                    {
                        if (!shield.Pixels[px, py]) continue;
                        Erode(shield, px, py);
                        return true;
                    }
                }
            }
            return false;
        }

        void Erode(Shield shield, int cx, int cy)
        {
            for (int x = Math.Max(0, cx - 2); x <= Math.Min(ShieldWidth - 1, cx + 2); x++)
            {
                for (int y = Math.Max(0, cy - 2); y <= Math.Min(ShieldHeight - 1, cy + 2); y++)
                {
                    int dx = x - cx, dy = y - cy;
                    int distance = dx * dx + dy * dy;
                    if (distance <= 1 || (distance <= 5 && random.Next(2) == 0)) shield.Pixels[x, y] = false;
                }
            }
            shield.Cached = null;
        }

        /// <summary>Aliens wipe out any part of a shield they reach.</summary>
        void CrushShields()
        {
            for (int r = 0; r < Rows; r++)
            {
                for (int c = 0; c < Columns; c++)
                {
                    if (!alive[r, c] || AlienY(r) + AlienHeight <= ShieldY) continue;
                    foreach (var shield in shields)
                    {
                        for (int x = 0; x < ShieldWidth; x++)
                        {
                            for (int y = 0; y < ShieldHeight; y++)
                            {
                                if (shield.Pixels[x, y] && Overlaps(shield.X + x, ShieldY + y, 1, 1, AlienX(c), AlienY(r), CellWidth, AlienHeight))
                                {
                                    shield.Pixels[x, y] = false;
                                    shield.Cached = null;
                                }
                            }
                        }
                    }
                }
            }
        }

        public override void Draw(DrawingContext dc, ArcadeText text)
        {
            text.Draw(dc, "SCORE " + Score.ToString("00000"), 3, 0, 7, WhiteBrush, TextAlign.Left);
            text.Draw(dc, "HI " + HighScore.ToString("00000"), ScreenWidth / 2, 0, 7, GreyBrush, TextAlign.Center);
            text.Draw(dc, "SHIPS " + Lives, ScreenWidth - 3, 0, 7, GreenBrush, TextAlign.Right);

            if (State == ArcadeState.Title)
            {
                text.Draw(dc, "INVADERS", ScreenWidth / 2, 34, 18, GreenBrush, TextAlign.Center);
                text.Draw(dc, "PRESS SPACE TO START", ScreenWidth / 2, 62, 8, WhiteBrush, TextAlign.Center);
                Octopus[0].Draw(dc, 62, 84, WhiteBrush);
                text.Draw(dc, "= 10", 80, 83, 8, GreyBrush, TextAlign.Left);
                Crab[0].Draw(dc, 62, 96, WhiteBrush);
                text.Draw(dc, "= 20", 80, 95, 8, GreyBrush, TextAlign.Left);
                Squid[0].Draw(dc, 64, 108, WhiteBrush);
                text.Draw(dc, "= 30", 80, 107, 8, GreyBrush, TextAlign.Left);
                UfoSprite.Draw(dc, 60, 121, RedBrush);
                text.Draw(dc, "= ???", 80, 120, 8, GreyBrush, TextAlign.Left);
                return;
            }

            for (int r = 0; r < Rows; r++)
            {
                var frames = r == 0 ? Squid : r < 3 ? Crab : Octopus;
                var sprite = frames[frame ? 1 : 0];
                for (int c = 0; c < Columns; c++)
                    if (alive[r, c]) sprite.Draw(dc, AlienX(c) + (CellWidth - sprite.Width) / 2, AlienY(r), WhiteBrush);
            }

            foreach (var shield in shields)
            {
                if (shield.Cached == null)
                {
                    var pixels = shield.Pixels;
                    shield.Cached = ArcadeSprite.ToGeometry(ShieldWidth, ShieldHeight, (x, y) => pixels[x, y]);
                }
                dc.PushTransform(new TranslateTransform(shield.X, ShieldY));
                dc.DrawGeometry(GreenBrush, null, shield.Cached);
                dc.Pop();
            }

            if (ufoActive) UfoSprite.Draw(dc, ufoX, UfoY, RedBrush);
            if (State == ArcadeState.Playing || State == ArcadeState.WaveClear) PlayerSprite.Draw(dc, PlayerX, PlayerY, GreenBrush);
            if (playerShot != null) dc.DrawRectangle(GreenBrush, null, new Rect(Math.Round(playerShot.X), Math.Round(playerShot.Y), 1, 3));
            foreach (var shot in alienShots) dc.DrawRectangle(WhiteBrush, null, new Rect(Math.Round(shot.X), Math.Round(shot.Y), 1, 4));
            foreach (var pop in pops) PopSprite.Draw(dc, pop.X, pop.Y, WhiteBrush);
            dc.DrawRectangle(GreenBrush, null, new Rect(0, GroundY, ScreenWidth, 1));

            if (State == ArcadeState.WaveClear)
                text.Draw(dc, "WAVE " + (Wave + 1), ScreenWidth / 2, 62, 14, WhiteBrush, TextAlign.Center);
            if (State == ArcadeState.GameOver)
            {
                text.Draw(dc, "GAME OVER", ScreenWidth / 2, 54, 16, RedBrush, TextAlign.Center);
                text.Draw(dc, "PRESS SPACE TO PLAY AGAIN", ScreenWidth / 2, 76, 8, WhiteBrush, TextAlign.Center);
            }
        }
    }
}
