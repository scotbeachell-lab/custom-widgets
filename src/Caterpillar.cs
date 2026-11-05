// A basic Centipede-style game: shoot a caterpillar as it winds down through a mushroom field.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace CustomWidgets
{
    public class CaterpillarGame : ArcadeGame
    {
        const int Columns = 25, Rows = 17, Cell = 8, OriginY = 10;
        const int PlayerZoneTop = 13, StartMushrooms = 34, MushroomHealth = 4, BodyLength = 10;
        const double PlayerSpeed = 95, ShotSpeed = 240, BaseSpeed = 60, SpeedPerLevel = 8, TopSpeed = 140;
        const double SpiderSpeedX = 42, SpiderSpeedY = 62, HitPause = 1.2, LevelPause = 1.5, RestartDelay = 1;
        const double SpiderTopY = OriginY + 9 * Cell, FieldBottom = OriginY + Rows * Cell;
        const int StartLives = 3, HeadScore = 100, BodyScore = 10, MushroomScore = 1, ExtraLifeEvery = 10000;

        static readonly string[] MushroomRows =
            { "..XXXX..", ".XXXXXX.", "XXXXXXXX", "XX.XX.XX", "XXXXXXXX", "...XX...", "...XX...", "..XXXX.." };
        static readonly ArcadeSprite[] MushroomSprites =
        {
            null,
            new ArcadeSprite(MushroomRows.Select((r, y) => y < 2 ? r : "........").ToArray()),
            new ArcadeSprite(MushroomRows.Select((r, y) => y < 4 ? r : "........").ToArray()),
            new ArcadeSprite(MushroomRows.Select((r, y) => y < 6 ? r : "........").ToArray()),
            new ArcadeSprite(MushroomRows)
        };
        static readonly ArcadeSprite[] BodySprites =
        {
            new ArcadeSprite("..XXXX..", ".XXXXXX.", "XXXXXXXX", "XXXXXXXX", "XXXXXXXX", ".XXXXXX.", "..X..X..", ".X....X."),
            new ArcadeSprite("..XXXX..", ".XXXXXX.", "XXXXXXXX", "XXXXXXXX", "XXXXXXXX", ".XXXXXX.", ".X....X.", "..X..X..")
        };
        static readonly ArcadeSprite[] HeadSprites =
        {
            new ArcadeSprite("..XXXX..", ".XXXXXX.", "X..XX..X", "XXXXXXXX", "XXXXXXXX", ".XXXXXX.", "..X..X..", ".X....X."),
            new ArcadeSprite("..XXXX..", ".XXXXXX.", "X..XX..X", "XXXXXXXX", "XXXXXXXX", ".XXXXXX.", ".X....X.", "..X..X..")
        };
        static readonly ArcadeSprite PlayerSprite = new ArcadeSprite(
            "...X...", "..XXX..", "..XXX..", ".XXXXX.", "XXXXXXX", "XXXXXXX", ".XXXXX.", "..X.X..");
        static readonly ArcadeSprite SpiderSprite = new ArcadeSprite(
            "X..X...X..X", ".X.X...X.X.", "..XXXXXXX..", ".XX.XXX.XX.", "X.XXXXXXX.X", "X..X...X..X", ".X.......X.");

        class Segment
        {
            public double X, Y;
            public int Column, Row, Direction, RowDirection;
            public Segment Ahead;

            public bool IsHead
            {
                get
                {
                    // A shot mushroom can also part the chain, so check that the one ahead is still close.
                    return Ahead == null || Math.Abs(Ahead.X - X) + Math.Abs(Ahead.Y - Y) > Cell * 1.5;
                }
            }
        }

        public override string Id { get { return "caterpillar"; } }

        public int Level { get; private set; }
        public int SegmentsLeft { get { return segments.Count; } }
        public int MushroomCount { get { return mushrooms.Cast<int>().Count(h => h > 0); } }
        public double PlayerX { get { return playerX; } }
        public double PlayerY { get { return playerY; } }

        readonly Random random;
        readonly ArcadeSparks sparks;
        readonly int[,] mushrooms = new int[Columns, Rows];
        readonly List<Segment> segments = new List<Segment>();
        double playerX, playerY, shotX, shotY, spiderX, spiderY, spiderVx, spiderVy, spiderTimer, stateTimer, animation;
        bool shotActive, spiderActive;
        int nextExtraLife;

        public CaterpillarGame(int seed)
        {
            random = new Random(seed);
            sparks = new ArcadeSparks(random);
            Lives = StartLives;
            Level = 1;
            State = ArcadeState.Title;
        }

        void NewGame()
        {
            Score = 0;
            Lives = StartLives;
            Level = 1;
            nextExtraLife = ExtraLifeEvery;
            Array.Clear(mushrooms, 0, mushrooms.Length);
            for (int i = 0; i < StartMushrooms; i++)
                mushrooms[random.Next(Columns), 1 + random.Next(PlayerZoneTop - 1)] = MushroomHealth;
            StartRound();
        }

        void StartRound()
        {
            for (int c = 0; c < Columns; c++) mushrooms[c, 0] = 0; // keep the entry row clear
            segments.Clear();
            Segment ahead = null;
            for (int i = 0; i < BodyLength; i++)
            {
                var segment = new Segment
                {
                    Column = 7 + i,
                    Row = 0,
                    X = (7 + i) * Cell,
                    Y = OriginY,
                    Direction = -1,
                    RowDirection = 1,
                    Ahead = ahead
                };
                segments.Add(segment);
                ahead = segment;
            }
            playerX = (ViewWidth - PlayerSprite.Width) / 2;
            playerY = FieldBottom - Cell;
            shotActive = false;
            spiderActive = false;
            spiderTimer = NextSpiderDelay();
            State = ArcadeState.Playing;
        }

        double NextSpiderDelay() { return 5 + random.NextDouble() * 7; }

        double SegmentSpeed { get { return Math.Min(TopSpeed, BaseSpeed + SpeedPerLevel * (Level - 1)); } }

        public override void Update(double seconds, ArcadeInput input)
        {
            sparks.Update(seconds);
            animation += seconds;
            switch (State)
            {
                case ArcadeState.Title:
                    if (input.Action) NewGame();
                    break;
                case ArcadeState.Playing:
                    UpdatePlayer(seconds, input);
                    UpdateShot(seconds);
                    UpdateSegments(seconds);
                    UpdateSpider(seconds);
                    if (State == ArcadeState.Playing && segments.Count == 0)
                    {
                        State = ArcadeState.WaveClear;
                        stateTimer = LevelPause;
                        shotActive = false;
                    }
                    break;
                case ArcadeState.PlayerHit:
                    stateTimer -= seconds;
                    if (stateTimer <= 0)
                    {
                        if (Lives > 0) StartRound();
                        else
                        {
                            State = ArcadeState.GameOver;
                            stateTimer = RestartDelay;
                            RecordHighScore();
                        }
                    }
                    break;
                case ArcadeState.WaveClear:
                    stateTimer -= seconds;
                    if (stateTimer <= 0)
                    {
                        Level++;
                        StartRound();
                    }
                    break;
                case ArcadeState.GameOver:
                    stateTimer -= seconds;
                    if (stateTimer <= 0 && input.Action) NewGame();
                    break;
            }
        }

        void AddScore(int points)
        {
            Score += points;
            if (Score < nextExtraLife) return;
            Lives++;
            nextExtraLife += ExtraLifeEvery;
        }

        static double RowY(int row) { return OriginY + row * Cell; }

        bool MushroomAt(int column, int row)
        {
            return column >= 0 && column < Columns && row >= 0 && row < Rows && mushrooms[column, row] > 0;
        }

        bool PlayerBlocked(double x, double y)
        {
            int left = (int)Math.Floor(x / Cell), right = (int)Math.Floor((x + PlayerSprite.Width - 1) / Cell);
            int top = (int)Math.Floor((y - OriginY) / Cell), bottom = (int)Math.Floor((y + PlayerSprite.Height - 1 - OriginY) / Cell);
            for (int c = left; c <= right; c++)
                for (int r = top; r <= bottom; r++)
                    if (MushroomAt(c, r)) return true;
            return false;
        }

        void UpdatePlayer(double seconds, ArcadeInput input)
        {
            double dx = ((input.Right ? 1 : 0) - (input.Left ? 1 : 0)) * PlayerSpeed * seconds;
            double dy = ((input.Down ? 1 : 0) - (input.Up ? 1 : 0)) * PlayerSpeed * seconds;

            double x = Math.Max(0, Math.Min(ViewWidth - PlayerSprite.Width, playerX + dx));
            if (!PlayerBlocked(x, playerY)) playerX = x;
            double y = Math.Max(RowY(PlayerZoneTop), Math.Min(FieldBottom - PlayerSprite.Height, playerY + dy));
            if (!PlayerBlocked(playerX, y)) playerY = y;

            if (input.Fire && !shotActive)
            {
                shotActive = true;
                shotX = playerX + PlayerSprite.Width / 2;
                shotY = playerY - 3;
            }
        }

        void UpdateShot(double seconds)
        {
            if (!shotActive) return;
            shotY -= ShotSpeed * seconds;
            if (shotY < OriginY) { shotActive = false; return; }

            foreach (var segment in segments)
            {
                if (!ArcadeMath.Overlaps(shotX, shotY, 1, 4, segment.X, segment.Y, Cell, Cell)) continue;
                KillSegment(segment);
                shotActive = false;
                return;
            }

            if (spiderActive && ArcadeMath.Overlaps(shotX, shotY, 1, 4, spiderX, spiderY, SpiderSprite.Width, SpiderSprite.Height))
            {
                double distance = Math.Abs(spiderY - playerY);
                AddScore(distance < 16 ? 900 : distance < 32 ? 600 : 300);
                sparks.Burst(spiderX + 5, spiderY + 3, ArcadePalette.Magenta, 14);
                spiderActive = false;
                spiderTimer = NextSpiderDelay();
                shotActive = false;
                return;
            }

            int column = (int)Math.Floor(shotX / Cell), row = (int)Math.Floor((shotY - OriginY) / Cell);
            if (MushroomAt(column, row))
            {
                mushrooms[column, row]--;
                if (mushrooms[column, row] == 0) AddScore(MushroomScore);
                shotActive = false;
            }
        }

        void KillSegment(Segment segment)
        {
            AddScore(segment.IsHead ? HeadScore : BodyScore);
            sparks.Burst(segment.X + 4, segment.Y + 4, ArcadePalette.Green, 8);
            segments.Remove(segment);
            foreach (var other in segments.Where(s => s.Ahead == segment)) other.Ahead = null;

            // A killed segment leaves a mushroom behind, which the rest of the body must go around.
            int column = (int)Math.Round(segment.X / Cell), row = (int)Math.Round((segment.Y - OriginY) / Cell);
            if (column >= 0 && column < Columns && row >= 0 && row < Rows) mushrooms[column, row] = MushroomHealth;
        }

        void UpdateSegments(double seconds)
        {
            foreach (var segment in segments)
            {
                double remaining = SegmentSpeed * seconds;
                while (remaining > 0)
                {
                    double targetX = segment.Column * Cell, targetY = RowY(segment.Row);
                    double distance = Math.Abs(targetX - segment.X) + Math.Abs(targetY - segment.Y);
                    if (distance <= remaining)
                    {
                        segment.X = targetX;
                        segment.Y = targetY;
                        remaining -= distance;
                        ChooseNextCell(segment);
                    }
                    else
                    {
                        segment.X += Math.Sign(targetX - segment.X) * remaining;
                        segment.Y += Math.Sign(targetY - segment.Y) * remaining;
                        remaining = 0;
                    }
                }
            }

            foreach (var segment in segments)
            {
                if (!ArcadeMath.Overlaps(playerX, playerY + 1, PlayerSprite.Width, PlayerSprite.Height - 1, segment.X + 1, segment.Y + 1, Cell - 2, Cell - 2)) continue;
                HitPlayer();
                return;
            }
        }

        void ChooseNextCell(Segment segment)
        {
            int next = segment.Column + segment.Direction;
            if (next >= 0 && next < Columns && !MushroomAt(next, segment.Row))
            {
                segment.Column = next;
                return;
            }

            // Blocked: drop a row and turn round. At the bottom it climbs back and forth in the player's zone.
            segment.Direction = -segment.Direction;
            int row = segment.Row + segment.RowDirection;
            if (row >= Rows) { segment.RowDirection = -1; row = segment.Row - 1; }
            else if (row < PlayerZoneTop && segment.RowDirection < 0) { segment.RowDirection = 1; row = segment.Row + 1; }
            segment.Row = row;
        }

        void UpdateSpider(double seconds)
        {
            if (!spiderActive)
            {
                spiderTimer -= seconds;
                if (spiderTimer > 0) return;
                spiderActive = true;
                bool fromLeft = random.Next(2) == 0;
                spiderX = fromLeft ? -SpiderSprite.Width : ViewWidth;
                spiderVx = fromLeft ? SpiderSpeedX : -SpiderSpeedX;
                spiderY = SpiderTopY + random.NextDouble() * (FieldBottom - SpiderTopY - SpiderSprite.Height);
                spiderVy = random.Next(2) == 0 ? SpiderSpeedY : -SpiderSpeedY;
                return;
            }

            spiderX += spiderVx * seconds;
            spiderY += spiderVy * seconds;
            if (spiderY <= SpiderTopY) { spiderY = SpiderTopY; spiderVy = SpiderSpeedY; }
            if (spiderY >= FieldBottom - SpiderSprite.Height) { spiderY = FieldBottom - SpiderSprite.Height; spiderVy = -SpiderSpeedY; }
            if (random.NextDouble() < 0.6 * seconds) spiderVy = -spiderVy;

            if (spiderX < -SpiderSprite.Width - 1 || spiderX > ViewWidth + 1)
            {
                spiderActive = false;
                spiderTimer = NextSpiderDelay();
                return;
            }

            // The spider eats mushrooms in its way.
            int column = (int)Math.Floor((spiderX + SpiderSprite.Width / 2) / Cell), row = (int)Math.Floor((spiderY + SpiderSprite.Height / 2 - OriginY) / Cell);
            if (MushroomAt(column, row) && random.NextDouble() < 2 * seconds) mushrooms[column, row] = 0;

            if (ArcadeMath.Overlaps(playerX + 1, playerY + 1, PlayerSprite.Width - 2, PlayerSprite.Height - 2, spiderX + 1, spiderY + 1, SpiderSprite.Width - 2, SpiderSprite.Height - 2))
                HitPlayer();
        }

        void HitPlayer()
        {
            Lives--;
            sparks.Burst(playerX + 3, playerY + 4, ArcadePalette.White, 24);
            shotActive = false;
            spiderActive = false;
            State = ArcadeState.PlayerHit;
            stateTimer = HitPause;
        }

        public override void Draw(DrawingContext dc, ArcadeText text)
        {
            text.Draw(dc, "SCORE " + Score.ToString("00000"), 3, 0, 7, ArcadePalette.White, TextAlign.Left);
            text.Draw(dc, "HI " + HighScore.ToString("00000"), ViewWidth / 2, 0, 7, ArcadePalette.Grey, TextAlign.Center);
            text.Draw(dc, "BUGS " + Lives, ViewWidth - 3, 0, 7, ArcadePalette.Green, TextAlign.Right);

            if (State == ArcadeState.Title)
            {
                text.Draw(dc, "CATERPILLAR", ViewWidth / 2, 22, 18, ArcadePalette.Green, TextAlign.Center);
                text.Draw(dc, "PRESS SPACE TO START", ViewWidth / 2, 48, 8, ArcadePalette.White, TextAlign.Center);
                text.Draw(dc, "ARROWS MOVE   SPACE FIRES", ViewWidth / 2, 62, 8, ArcadePalette.Grey, TextAlign.Center);
                HeadSprites[0].Draw(dc, 44, 82, ArcadePalette.Yellow);
                text.Draw(dc, "HEAD  = 100", 60, 82, 8, ArcadePalette.Grey, TextAlign.Left);
                BodySprites[0].Draw(dc, 44, 95, ArcadePalette.Green);
                text.Draw(dc, "BODY  = 10", 60, 95, 8, ArcadePalette.Grey, TextAlign.Left);
                SpiderSprite.Draw(dc, 42, 108, ArcadePalette.Magenta);
                text.Draw(dc, "SPIDER= UP TO 900", 60, 107, 8, ArcadePalette.Grey, TextAlign.Left);
                return;
            }

            for (int c = 0; c < Columns; c++)
                for (int r = 0; r < Rows; r++)
                    if (mushrooms[c, r] > 0) MushroomSprites[mushrooms[c, r]].Draw(dc, c * Cell, RowY(r), ArcadePalette.Orange);

            int frame = (int)(animation * 6) % 2;
            foreach (var segment in segments)
            {
                if (segment.IsHead) HeadSprites[frame].Draw(dc, segment.X, segment.Y, ArcadePalette.Yellow);
                else BodySprites[frame].Draw(dc, segment.X, segment.Y, ArcadePalette.Green);
            }

            if (spiderActive) SpiderSprite.Draw(dc, spiderX, spiderY, ArcadePalette.Magenta);
            if (State == ArcadeState.Playing || State == ArcadeState.WaveClear) PlayerSprite.Draw(dc, playerX, playerY, ArcadePalette.Cyan);
            if (shotActive) dc.DrawRectangle(ArcadePalette.White, null, new Rect(Math.Round(shotX), Math.Round(shotY), 1, 4));
            sparks.Draw(dc, x => x);

            if (State == ArcadeState.WaveClear)
                text.Draw(dc, "LEVEL " + (Level + 1), ViewWidth / 2, 62, 14, ArcadePalette.White, TextAlign.Center);
            if (State == ArcadeState.GameOver)
            {
                text.Draw(dc, "GAME OVER", ViewWidth / 2, 54, 16, ArcadePalette.Red, TextAlign.Center);
                text.Draw(dc, "PRESS SPACE TO PLAY AGAIN", ViewWidth / 2, 76, 8, ArcadePalette.White, TextAlign.Center);
            }
        }
    }
}
