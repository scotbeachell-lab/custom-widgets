// A basic Pole Position-style racer: qualify on a lap against the clock, then race seven rivals.
// The road is drawn in strips from near to far, each one narrower and shifted by the curves ahead.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace CustomWidgets
{
    public class PolePositionGame : ArcadeGame
    {
        // Road drawing, in game pixels. A strip's distance ahead is DepthScale / rows below the horizon.
        const double HorizonY = 50, StripHeight = 2, DepthScale = 1200, RoadWidthPerRow = 0.85, BendPerStrip = 0.06;
        const int Strips = 50;
        const double StripeLength = 8, StartLineLength = 3, RumbleWidth = 1.12, PlayerCarScale = 1.4, PlayerCarBottom = 132;
        const double PlayerRows = 82; // rows below the horizon where the player's car sits
        const double PlayerDepth = DepthScale / PlayerRows, FarthestVisible = DepthScale / 2;

        // Car handling. Speeds are km/h, distances are metres, and X is road widths: the edges are at -1 and 1.
        const double LowGearTop = 160, LowGearPull = 70, HighGearTop = 300, HighGearPullLow = 14, HighGearPullHigh = 46, HighGearPowerSpeed = 110;
        const double BrakePower = 170, Coasting = 20, OverRevDrag = 60, OffRoadTop = 90, OffRoadDrag = 200;
        const double SteerRate = 1.7, CornerPush = 3.0, FarthestX = 1.8, StartX = 0.5;
        const double CarHitWidth = 0.47, RearEndDepth = 4, CrashSpeedDifference = 25, SignHitWidth = 0.3, SignX = 1.45, SignCrashSpeed = 30;

        // The race.
        const double SegmentLength = 12, ParSpeed = 240;
        const int RaceLaps = 3, CarsInRace = 8, TrafficInQualifying = 3, SignEverySegments = 14;
        const double GridSpacing = 12, RivalAcceleration = 50, FastestRival = 262, RivalSpeedStep = 5;
        const double QualifyTimeFactor = 1.5, RaceStartTimeFactor = 1.3, LapBonusTimeFactor = 1.0, SecondsPerGridPlace = 1.0, PoleTimeFactor = 0.9;
        const double StartLightsSeconds = 3, CrashPause = 1.5, ResultPause = 3, RestartDelay = 1;
        const int PointsPerKilometre = 50, PassScore = 100, PositionBonus = 1000, TimeBonusPerSecond = 100;

        /// <summary>Corner sharpness for each run of road segments: negative left, positive right.</summary>
        static readonly double[][] Course =
        {
            new double[] { 40, 0 }, new double[] { 12, 0.35 }, new double[] { 14, 0.75 }, new double[] { 10, 0.35 },
            new double[] { 30, 0 }, new double[] { 10, -0.4 }, new double[] { 18, -0.85 }, new double[] { 8, -0.3 },
            new double[] { 20, 0 }, new double[] { 10, 0.5 }, new double[] { 10, 1 }, new double[] { 10, 0.5 },
            new double[] { 25, 0 }, new double[] { 8, -0.5 }, new double[] { 8, 0.5 }, new double[] { 8, -0.5 },
            new double[] { 30, 0 }
        };

        static readonly double[] Curves = Course.SelectMany(run => Enumerable.Repeat(run[1], (int)run[0])).ToArray();
        static readonly double LapLength = Curves.Length * SegmentLength;
        static readonly double ParLapSeconds = LapLength / (ParSpeed / 3.6);

        static readonly Brush SkyBrush = Colour(0x4C, 0x8D, 0xE8);
        static readonly Brush MountainBrush = Colour(0x3A, 0x5A, 0x8C);
        static readonly Brush GrassA = Colour(0x2E, 0xA0, 0x3C);
        static readonly Brush GrassB = Colour(0x27, 0x8C, 0x34);
        static readonly Brush RoadA = Colour(0x5A, 0x5A, 0x62);
        static readonly Brush RoadB = Colour(0x52, 0x52, 0x5A);
        static readonly Brush TyreBrush = Colour(0x18, 0x18, 0x1C);
        static readonly Brush[] RivalBrushes = { ArcadePalette.Yellow, ArcadePalette.Cyan, ArcadePalette.Magenta, ArcadePalette.Orange, ArcadePalette.Green };

        static readonly ArcadeSprite CarBody = new ArcadeSprite(
            "...XXXXXXXXXXXXXXXXX...",
            "...XXXXXXXXXXXXXXXXX...",
            ".........XXXXX.........",
            ".........XXXXX.........",
            "........XXXXXXX........",
            ".......XXXXXXXXX.......",
            ".....XXXXXXXXXXXXX.....",
            ".....XXXXXXXXXXXXX.....",
            ".....XXXXXXXXXXXXX.....",
            "......XXXXXXXXXXX......");
        static readonly ArcadeSprite CarTyres = new ArcadeSprite(
            ".......................",
            ".......................",
            ".......................",
            ".......................",
            ".......................",
            "XXXX...............XXXX",
            "XXXX...............XXXX",
            "XXXX...............XXXX",
            "XXXX...............XXXX",
            "XXXX...............XXXX");
        static readonly ArcadeSprite CarHelmet = new ArcadeSprite(
            ".......................",
            ".......................",
            ".......................",
            "..........XXX..........",
            "..........XXX..........",
            ".......................",
            ".......................",
            ".......................",
            ".......................",
            ".......................");
        static readonly ArcadeSprite SignBoard = new ArcadeSprite(
            "XXXXXXXXXXXX", "XXXXXXXXXXXX", "XXXXXXXXXXXX", "XXXXXXXXXXXX", "XXXXXXXXXXXX", "XXXXXXXXXXXX",
            "............", "............", "............", "............", "............", "............");
        static readonly ArcadeSprite SignPoles = new ArcadeSprite(
            "............", "............", "............", "............", "............", "............",
            ".X........X.", ".X........X.", ".X........X.", ".X........X.", ".X........X.", ".X........X.");
        static readonly Geometry Mountains = MakeMountains();

        class Rival
        {
            public double Distance, X, Speed, TopSpeed;
            public Brush Brush;
        }

        class Sign { public double Distance, X; }

        public override string Id { get { return "pole"; } }

        public bool IsQualifying { get; private set; }
        public bool HighGear { get; private set; }
        public bool Finished { get; private set; }
        public int RacePosition { get; private set; }
        public int GridPosition { get; private set; }
        public double QualifyingLapSeconds { get; private set; }
        public double Speed { get { return speed; } }
        public double PlayerX { get { return playerX; } }
        public double Distance { get { return distance; } }
        public double TimeLeft { get { return timeLeft; } }
        public bool LightsOn { get { return startLights > 0; } }
        public int Lap { get { return Math.Min(RaceLaps, (int)(Math.Max(0, distance) / LapLength) + 1); } }
        public static double ParLap { get { return ParLapSeconds; } }
        public static double TrackLength { get { return LapLength; } }

        readonly Random random;
        readonly ArcadeSparks sparks;
        readonly List<Rival> rivals = new List<Rival>();
        readonly List<Sign> signs = new List<Sign>();
        readonly double[] stripCenter = new double[Strips];
        readonly double[] stripHalf = new double[Strips];
        double speed, playerX, distance, timeLeft, lapClock, startLights, stateTimer, scoreDistance, mountainOffset;
        int bestPosition, nextLapBonus;
        string resultLine = "";
        string resultDetail = "";

        public PolePositionGame(int seed)
        {
            random = new Random(seed);
            sparks = new ArcadeSparks(random);
            for (int i = SignEverySegments; i < Curves.Length; i += SignEverySegments)
                signs.Add(new Sign { Distance = i * SegmentLength, X = (i / SignEverySegments) % 2 == 0 ? SignX : -SignX });
            State = ArcadeState.Title;
        }

        static Brush Colour(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        static Geometry MakeMountains()
        {
            var peaks = new[] { 0, 8, 22, 18, 40, 6, 58, 16, 74, 10, 92, 20, 110, 7, 128, 17, 150, 11, 170, 19, 188, 8, 200, 0 };
            var result = new StreamGeometry();
            using (var g = result.Open())
            {
                g.BeginFigure(new Point(0, 0), true, true);
                for (int i = 0; i < peaks.Length; i += 2) g.LineTo(new Point(peaks[i], -peaks[i + 1]), false, false);
            }
            result.Freeze();
            return result;
        }

        static double WrapOnLap(double d)
        {
            d %= LapLength;
            return d < 0 ? d + LapLength : d;
        }

        static double CurveAt(double d) { return Curves[Math.Min(Curves.Length - 1, (int)(WrapOnLap(d) / SegmentLength))]; }

        /// <summary>The corner under the car right now, which also lets the test bot see the road.</summary>
        public double CurrentCurve { get { return CurveAt(distance); } }

        void NewGame()
        {
            Score = 0;
            scoreDistance = 0;
            Finished = false;
            GridPosition = 0;
            StartQualifying();
        }

        void PlaceCar(double x)
        {
            speed = 0;
            distance = 0;
            playerX = x;
            HighGear = false;
            startLights = StartLightsSeconds;
            lapClock = 0;
            sparks.Clear();
            State = ArcadeState.Playing;
        }

        void StartQualifying()
        {
            IsQualifying = true;
            timeLeft = ParLapSeconds * QualifyTimeFactor;
            rivals.Clear();
            for (int i = 0; i < TrafficInQualifying; i++)
            {
                double top = 150 + random.NextDouble() * 30;
                rivals.Add(new Rival
                {
                    Distance = LapLength * (i + 0.6) / (TrafficInQualifying + 1),
                    X = i % 2 == 0 ? StartX : -StartX,
                    Speed = top,
                    TopSpeed = top,
                    Brush = RivalBrushes[i % RivalBrushes.Length]
                });
            }
            RacePosition = 0;
            PlaceCar(0);
        }

        void StartRace()
        {
            IsQualifying = false;
            timeLeft = ParLapSeconds * RaceStartTimeFactor;
            nextLapBonus = 1;
            rivals.Clear();
            // The grid has two columns, and qualifying decides which place the player starts from.
            for (int place = 1; place <= CarsInRace; place++)
            {
                if (place == GridPosition) continue;
                double top = FastestRival - RivalSpeedStep * (place - 1);
                rivals.Add(new Rival
                {
                    Distance = (GridPosition - place) * GridSpacing,
                    X = place % 2 == 0 ? StartX : -StartX,
                    TopSpeed = top,
                    Brush = RivalBrushes[place % RivalBrushes.Length]
                });
            }
            RacePosition = GridPosition;
            bestPosition = GridPosition;
            PlaceCar(GridPosition % 2 == 0 ? StartX : -StartX);
        }

        public override void Update(double seconds, ArcadeInput input)
        {
            sparks.Update(seconds);
            switch (State)
            {
                case ArcadeState.Title:
                    if (input.Action) NewGame();
                    break;
                case ArcadeState.Playing:
                    UpdatePlaying(seconds, input);
                    break;
                case ArcadeState.PlayerHit:
                    timeLeft -= seconds;
                    stateTimer -= seconds;
                    if (timeLeft <= 0) EndRace();
                    else if (stateTimer <= 0)
                    {
                        speed = 0;
                        HighGear = false;
                        playerX = Math.Max(-0.8, Math.Min(0.8, playerX));
                        State = ArcadeState.Playing;
                    }
                    break;
                case ArcadeState.WaveClear:
                    stateTimer -= seconds;
                    if (stateTimer <= 0) StartRace();
                    break;
                case ArcadeState.GameOver:
                    stateTimer -= seconds;
                    if (stateTimer <= 0 && input.Action) NewGame();
                    break;
            }
        }

        void UpdatePlaying(double seconds, ArcadeInput input)
        {
            if (input.Action) HighGear = !HighGear;

            if (startLights > 0)
            {
                startLights -= seconds;
                return;
            }

            timeLeft -= seconds;
            lapClock += seconds;
            UpdateCar(seconds, input);
            UpdateRivals(seconds);
            CheckSigns(seconds);
            if (State == ArcadeState.Playing) CheckRivalHits();
            if (State == ArcadeState.Playing) UpdateProgress();
            if ((State == ArcadeState.Playing || State == ArcadeState.PlayerHit) && timeLeft <= 0) EndRace();
        }

        void UpdateCar(double seconds, ArcadeInput input)
        {
            if (input.Up) speed += EnginePull() * seconds;
            else speed -= Coasting * seconds;
            if (input.Down) speed -= BrakePower * seconds;
            if (!HighGear && speed > LowGearTop) speed -= OverRevDrag * seconds;
            if (Math.Abs(playerX) > 1 && speed > OffRoadTop) speed -= OffRoadDrag * seconds;
            speed = Math.Max(0, Math.Min(HighGearTop, speed));

            double grip = Math.Min(1, speed / 60);
            if (input.Left) playerX -= SteerRate * grip * seconds;
            if (input.Right) playerX += SteerRate * grip * seconds;

            // Corners push the car outwards, and much harder at high speed.
            double fraction = speed / HighGearTop;
            double curve = CurveAt(distance);
            playerX -= curve * fraction * fraction * CornerPush * seconds;
            playerX = Math.Max(-FarthestX, Math.Min(FarthestX, playerX));

            mountainOffset -= curve * fraction * 40 * seconds;

            double moved = speed / 3.6 * seconds;
            distance += moved;
            scoreDistance += moved;
            while (scoreDistance >= 1000.0 / PointsPerKilometre)
            {
                scoreDistance -= 1000.0 / PointsPerKilometre;
                Score++;
            }
        }

        double EnginePull()
        {
            if (!HighGear) return speed < LowGearTop ? LowGearPull : 0;
            return speed < HighGearPowerSpeed ? HighGearPullLow : HighGearPullHigh;
        }

        void UpdateRivals(double seconds)
        {
            foreach (var rival in rivals)
            {
                rival.Speed = Math.Min(rival.TopSpeed, rival.Speed + RivalAcceleration * seconds);
                rival.Distance += rival.Speed / 3.6 * seconds;
            }
        }

        void CheckSigns(double seconds)
        {
            if (speed < SignCrashSpeed) return;
            double now = WrapOnLap(distance), before = now - speed / 3.6 * seconds;
            foreach (var sign in signs)
            {
                if (sign.Distance > before && sign.Distance <= now && Math.Abs(playerX - sign.X) < SignHitWidth)
                {
                    Crash();
                    return;
                }
            }
        }

        void CheckRivalHits()
        {
            foreach (var rival in rivals)
            {
                double ahead = WrapOnLap(rival.Distance - distance);
                if (ahead > RearEndDepth || Math.Abs(rival.X - playerX) >= CarHitWidth) continue;
                if (speed - rival.Speed > CrashSpeedDifference)
                {
                    Crash();
                    return;
                }
                speed = Math.Min(speed, rival.Speed);
            }
        }

        void Crash()
        {
            sparks.Burst(ViewWidth / 2, PlayerCarBottom - 7, ArcadePalette.Orange, 30);
            speed = 0;
            State = ArcadeState.PlayerHit;
            stateTimer = CrashPause;
        }

        void UpdateProgress()
        {
            if (IsQualifying)
            {
                if (distance >= LapLength) FinishQualifying();
                return;
            }

            RacePosition = 1 + rivals.Count(r => r.Distance > distance);
            if (RacePosition < bestPosition)
            {
                Score += PassScore * (bestPosition - RacePosition);
                bestPosition = RacePosition;
            }

            if (distance >= LapLength * RaceLaps)
            {
                Finished = true;
                Score += (int)(Math.Max(0, timeLeft) * TimeBonusPerSecond) + (CarsInRace + 1 - RacePosition) * PositionBonus;
                EndRace();
                return;
            }

            if (distance >= LapLength * nextLapBonus)
            {
                nextLapBonus++;
                timeLeft += ParLapSeconds * LapBonusTimeFactor;
            }
        }

        void FinishQualifying()
        {
            QualifyingLapSeconds = lapClock;
            double behindPole = lapClock - ParLapSeconds * PoleTimeFactor;
            GridPosition = Math.Max(1, Math.Min(CarsInRace, 1 + (int)Math.Ceiling(behindPole / SecondsPerGridPlace)));
            Score += (CarsInRace + 1 - GridPosition) * PassScore;
            State = ArcadeState.WaveClear;
            stateTimer = ResultPause;
        }

        void EndRace()
        {
            if (Finished)
            {
                resultLine = "FINISHED P" + RacePosition;
                resultDetail = "OF " + CarsInRace + " CARS";
            }
            else if (IsQualifying)
            {
                resultLine = "NOT QUALIFIED";
                resultDetail = "OUT OF TIME";
            }
            else
            {
                resultLine = "OUT OF TIME";
                resultDetail = "LAP " + Lap + " OF " + RaceLaps;
            }
            State = ArcadeState.GameOver;
            stateTimer = RestartDelay;
            RecordHighScore();
        }

        public override void Draw(DrawingContext dc, ArcadeText text)
        {
            if (State == ArcadeState.Title)
            {
                DrawTitle(dc, text);
                return;
            }

            dc.DrawRectangle(SkyBrush, null, new Rect(0, 0, ViewWidth, HorizonY));
            DrawMountains(dc);
            DrawRoad(dc);
            DrawRoadsideAndTraffic(dc);
            if (State != ArcadeState.PlayerHit) DrawPlayer(dc);
            sparks.Draw(dc, x => x);
            DrawHud(dc, text);
            DrawBanners(dc, text);
        }

        void DrawTitle(DrawingContext dc, ArcadeText text)
        {
            double center = ViewWidth / 2;
            text.Draw(dc, "HI " + HighScore.ToString("000000"), center, 0, 7, ArcadePalette.Grey, TextAlign.Center);
            text.Draw(dc, "POLE POSITION", center, 16, 18, ArcadePalette.Yellow, TextAlign.Center);
            text.Draw(dc, "PRESS SPACE TO START", center, 42, 8, ArcadePalette.White, TextAlign.Center);
            text.Draw(dc, "UP GAS      DOWN BRAKE", center, 60, 8, ArcadePalette.Grey, TextAlign.Center);
            text.Draw(dc, "LEFT / RIGHT STEER", center, 71, 8, ArcadePalette.Grey, TextAlign.Center);
            text.Draw(dc, "SPACE SHIFTS LOW / HIGH", center, 82, 8, ArcadePalette.Grey, TextAlign.Center);
            text.Draw(dc, "SHIFT TO HIGH AT 150", center, 93, 8, ArcadePalette.Cyan, TextAlign.Center);
            DrawCar(dc, center, 132, PlayerCarScale * 1.5, ArcadePalette.Red);
        }

        void DrawMountains(DrawingContext dc)
        {
            double offset = mountainOffset % ViewWidth;
            if (offset < 0) offset += ViewWidth;
            for (int i = -1; i <= 0; i++)
            {
                dc.PushTransform(new TranslateTransform(Math.Round(offset + i * ViewWidth), HorizonY));
                dc.DrawGeometry(MountainBrush, null, Mountains);
                dc.Pop();
            }
        }

        void DrawRoad(DrawingContext dc)
        {
            double camera = distance - PlayerDepth, shift = 0, bend = 0;
            for (int i = 0; i < Strips; i++)
            {
                double y = HorizonY + (Strips - 1 - i) * StripHeight;
                double rows = y - HorizonY + StripHeight;
                double ahead = camera + DepthScale / rows;
                bend += CurveAt(ahead) * BendPerStrip;
                shift += bend;

                double half = rows * RoadWidthPerRow;
                // Curves bend away from the car, and the road slides under it when it moves across.
                double center = ViewWidth / 2 + shift - playerX * half;
                stripCenter[i] = center;
                stripHalf[i] = half;

                double onLap = WrapOnLap(ahead);
                bool light = ((int)(onLap / StripeLength)) % 2 == 0;
                dc.DrawRectangle(light ? GrassA : GrassB, null, new Rect(0, y, ViewWidth, StripHeight));

                double rumble = half * RumbleWidth;
                dc.DrawRectangle(light ? ArcadePalette.White : ArcadePalette.Red, null, new Rect(Math.Round(center - rumble), y, Math.Round(rumble * 2), StripHeight));
                dc.DrawRectangle(light ? RoadA : RoadB, null, new Rect(Math.Round(center - half), y, Math.Round(half * 2), StripHeight));

                if (onLap < StartLineLength)
                {
                    dc.DrawRectangle(ArcadePalette.White, null, new Rect(Math.Round(center - half), y, Math.Round(half * 2), StripHeight));
                }
                else if (light)
                {
                    double line = Math.Max(1, half * 0.03);
                    dc.DrawRectangle(ArcadePalette.White, null, new Rect(Math.Round(center - half / 3 - line / 2), y, Math.Round(line), StripHeight));
                    dc.DrawRectangle(ArcadePalette.White, null, new Rect(Math.Round(center + half / 3 - line / 2), y, Math.Round(line), StripHeight));
                }
            }
        }

        void DrawRoadsideAndTraffic(DrawingContext dc)
        {
            double camera = distance - PlayerDepth;
            var things = new List<KeyValuePair<double, Action<int, double>>>();

            foreach (var sign in signs)
            {
                var s = sign;
                double ahead = WrapOnLap(s.Distance - camera);
                if (ahead > PlayerDepth && ahead < FarthestVisible)
                    things.Add(new KeyValuePair<double, Action<int, double>>(ahead, (strip, scale) => DrawSign(dc, stripCenter[strip] + s.X * stripHalf[strip], StripBottom(strip), scale)));
            }
            foreach (var rival in rivals)
            {
                var r = rival;
                double ahead = WrapOnLap(r.Distance - camera);
                if (ahead > PlayerDepth * 0.6 && ahead < FarthestVisible)
                    things.Add(new KeyValuePair<double, Action<int, double>>(ahead, (strip, scale) => DrawCar(dc, stripCenter[strip] + r.X * stripHalf[strip], StripBottom(strip), scale * PlayerCarScale, r.Brush)));
            }

            // Far things first, so that nearer ones cover them.
            foreach (var thing in things.OrderByDescending(t => t.Key))
            {
                double rows = DepthScale / thing.Key;
                int strip = Math.Max(0, Math.Min(Strips - 1, Strips - (int)(rows / StripHeight)));
                thing.Value(strip, stripHalf[strip] / (PlayerRows * RoadWidthPerRow));
            }
        }

        static double StripBottom(int strip) { return HorizonY + (Strips - 1 - strip) * StripHeight + StripHeight; }

        void DrawSign(DrawingContext dc, double x, double bottom, double scale)
        {
            double size = scale * 1.5;
            double left = x - SignBoard.Width * size / 2, top = bottom - SignBoard.Height * size;
            SignPoles.Draw(dc, left, top, size, ArcadePalette.White);
            SignBoard.Draw(dc, left, top, size, ArcadePalette.Yellow);
        }

        static void DrawCar(DrawingContext dc, double x, double bottom, double scale, Brush body)
        {
            double left = x - CarBody.Width * scale / 2, top = bottom - CarBody.Height * scale;
            CarTyres.Draw(dc, left, top, scale, TyreBrush);
            CarBody.Draw(dc, left, top, scale, body);
            CarHelmet.Draw(dc, left, top, scale, ArcadePalette.White);
        }

        void DrawPlayer(DrawingContext dc) { DrawCar(dc, ViewWidth / 2, PlayerCarBottom, PlayerCarScale, ArcadePalette.Red); }

        void DrawHud(DrawingContext dc, ArcadeText text)
        {
            double center = ViewWidth / 2;
            text.Draw(dc, "SCORE " + Score.ToString("000000"), 3, 0, 7, ArcadePalette.White, TextAlign.Left);
            text.Draw(dc, "HI " + HighScore.ToString("000000"), ViewWidth - 3, 0, 7, ArcadePalette.Grey, TextAlign.Right);
            text.Draw(dc, Math.Ceiling(Math.Max(0, timeLeft)).ToString("0"), center, 0, 14, ArcadePalette.Yellow, TextAlign.Center);

            string progress = IsQualifying ? "QUALIFYING" : "LAP " + Lap + "/" + RaceLaps + "  P" + RacePosition;
            text.Draw(dc, progress, 3, 9, 7, ArcadePalette.White, TextAlign.Left);

            string gear = HighGear ? "HIGH" : "LOW";
            text.Draw(dc, gear + " " + (Math.Round(speed / 5) * 5).ToString("0") + " KM/H", ViewWidth - 3, 9, 7, HighGear ? ArcadePalette.Cyan : ArcadePalette.Orange, TextAlign.Right);
        }

        void DrawBanners(DrawingContext dc, ArcadeText text)
        {
            double center = ViewWidth / 2;
            if (startLights > 0)
            {
                bool go = startLights < 0.5;
                int lit = go ? 3 : 3 - (int)Math.Ceiling(startLights - 0.5) + 1;
                for (int i = 0; i < 3; i++)
                {
                    var brush = go ? ArcadePalette.Green : i < lit ? ArcadePalette.Red : TyreBrush;
                    dc.DrawRectangle(brush, null, new Rect(center - 19 + i * 14, 28, 10, 10));
                }
                dc.DrawRectangle(ArcadePalette.Dim, null, new Rect(0, 58, ViewWidth, 16));
                text.Draw(dc, IsQualifying ? "QUALIFYING LAP" : "RACE " + RaceLaps + " LAPS", center, 60, 10, ArcadePalette.White, TextAlign.Center);
            }
            if (State == ArcadeState.WaveClear)
            {
                dc.DrawRectangle(ArcadePalette.Dim, null, new Rect(0, 54, ViewWidth, 34));
                text.Draw(dc, "QUALIFIED P" + GridPosition, center, 56, 14, ArcadePalette.Yellow, TextAlign.Center);
                text.Draw(dc, "LAP TIME " + QualifyingLapSeconds.ToString("0.0") + "S", center, 74, 9, ArcadePalette.White, TextAlign.Center);
            }
            if (State == ArcadeState.GameOver)
            {
                dc.DrawRectangle(ArcadePalette.Dim, null, new Rect(0, 48, ViewWidth, 46));
                text.Draw(dc, resultLine, center, 50, 15, Finished ? ArcadePalette.Yellow : ArcadePalette.Red, TextAlign.Center);
                text.Draw(dc, resultDetail, center, 68, 9, ArcadePalette.White, TextAlign.Center);
                text.Draw(dc, "PRESS SPACE TO PLAY AGAIN", center, 82, 8, ArcadePalette.White, TextAlign.Center);
            }
        }
    }
}
