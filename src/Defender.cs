// A basic Defender clone: fly over a wrapping world and stop landers abducting your humans.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace CustomWidgets
{
    public class DefenderGame : ArcadeGame
    {
        const double WorldWidth = 800, CenterX = ViewWidth / 2;
        const double TopY = 24, ShipLowestY = 116;
        const double RadarLeft = 40, RadarWidth = 120, RadarTop = 9, RadarHeight = 12;
        const double ShipAcceleration = 260, ShipMaxSpeed = 120, ShipDrag = 90, ShipClimb = 75, CameraLead = 45;
        const double BeamLength = 105, BeamSeconds = 0.08, FireCooldown = 0.2;
        const double RespawnSafeSeconds = 1.5, HitPause = 1.4, WavePause = 2, RestartDelay = 1;
        const double BulletSpeed = 62, BulletSeconds = 2.5;
        const double FallGravity = 30, FallTopSpeed = 38, SafeFallHeight = 40;
        const int StartLives = 3, HumanCount = 8, BatchSize = 3, MaxBullets = 6;
        const double BatchSeconds = 3;
        const int LanderScore = 150, MutantScore = 150, CatchScore = 500, SafeLandingScore = 250;

        static readonly string[] ShipRows = { "XX..........", "XXXX........", "XXXXXXXXXXXX", ".XXXXXXX....", "..XX........" };
        static readonly ArcadeSprite ShipRight = new ArcadeSprite(ShipRows);
        static readonly ArcadeSprite ShipLeft = ArcadeSprite.Flipped(ShipRows);
        static readonly ArcadeSprite LanderSprite = new ArcadeSprite(
            "...XXX...", "..XXXXX..", ".XX.X.XX.", ".XXXXXXX.", "..XXXXX..", ".X.X.X.X.", "X..X.X..X", "X.......X");
        static readonly ArcadeSprite MutantSprite = new ArcadeSprite(
            "X..XXX..X", ".XXXXXXX.", "XX.XXX.XX", "XXXXXXXXX", ".X.XXX.X.", "X..X.X..X", "...X.X...");
        static readonly ArcadeSprite HumanSprite = new ArcadeSprite(".X.", "XXX", ".X.", ".X.", "X.X", "X.X");
        static readonly Pen GroundPen = MakePen(ArcadePalette.Orange);
        static readonly Pen RadarPen = MakePen(ArcadePalette.Grey);

        enum HumanState { Walking, Carried, Falling, OnShip, Dead }

        class Human
        {
            public double X, Y, Direction, FallSpeed, FallStartY;
            public HumanState State;
        }

        class Enemy
        {
            public double X, Y, CruiseY, Speed, FireTimer, Wander;
            public bool IsMutant;
            public Human Target, Carrying;
        }

        class Bullet { public double X, Y, Vx, Vy, Left; }

        class Beam { public double X, Y, Direction, Left; }

        public override string Id { get { return "defender"; } }

        public int Wave { get; private set; }
        public int EnemiesLeft { get { return enemies.Count + spawnsLeft; } }
        public int HumansAlive { get { return humans.Count(h => h.State != HumanState.Dead); } }
        public int HumansCaught { get; private set; }
        public double ShipX { get { return shipX; } }
        public double ShipY { get { return shipY; } }

        readonly Random random;
        readonly ArcadeSparks sparks;
        readonly double[] groundWaves;
        readonly List<Human> humans = new List<Human>();
        readonly List<Enemy> enemies = new List<Enemy>();
        readonly List<Bullet> bullets = new List<Bullet>();
        readonly List<Beam> beams = new List<Beam>();
        double shipX, shipY, shipVx, facing = 1, camera, cooldown, safeTimer, batchTimer, stateTimer;
        int spawnsLeft;

        public DefenderGame(int seed)
        {
            random = new Random(seed);
            sparks = new ArcadeSparks(random);
            groundWaves = Enumerable.Range(0, 6).Select(i => random.NextDouble() * Math.PI * 2).ToArray();
            Lives = StartLives;
            Wave = 1;
            shipX = WorldWidth / 2;
            shipY = 70;
            camera = shipX;
            State = ArcadeState.Title;
        }

        static Pen MakePen(Brush brush)
        {
            var pen = new Pen(brush, 1);
            pen.Freeze();
            return pen;
        }

        /// <summary>Shortest signed distance across the wrapping world.</summary>
        static double Wrap(double delta)
        {
            delta %= WorldWidth;
            if (delta > WorldWidth / 2) delta -= WorldWidth;
            if (delta < -WorldWidth / 2) delta += WorldWidth;
            return delta;
        }

        static double WrapPosition(double x)
        {
            x %= WorldWidth;
            return x < 0 ? x + WorldWidth : x;
        }

        double GroundAt(double x)
        {
            // Whole-number cycles around the world, so the hills join up where it wraps.
            double angle = x / WorldWidth * Math.PI * 2;
            return 133 + 4 * Math.Sin(3 * angle + groundWaves[0]) + 3 * Math.Sin(7 * angle + groundWaves[1]) + 2 * Math.Sin(13 * angle + groundWaves[2]);
        }

        double ToScreen(double worldX) { return CenterX + Wrap(worldX - camera); }

        void NewGame()
        {
            Score = 0;
            Lives = StartLives;
            Wave = 1;
            HumansCaught = 0;
            StartWave();
        }

        void StartWave()
        {
            enemies.Clear();
            bullets.Clear();
            beams.Clear();
            humans.Clear();
            for (int i = 0; i < HumanCount; i++)
            {
                double x = (i + random.NextDouble() * 0.6) * WorldWidth / HumanCount;
                humans.Add(new Human { X = x, Y = GroundAt(x) - HumanSprite.Height, Direction = random.Next(2) == 0 ? 1 : -1 });
            }
            spawnsLeft = 5 + 2 * Wave;
            batchTimer = 0.5;
            PlaceShip();
            State = ArcadeState.Playing;
        }

        void PlaceShip()
        {
            shipVx = 0;
            shipY = 70;
            safeTimer = RespawnSafeSeconds;
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
                    UpdateShip(seconds, input);
                    UpdateBeams(seconds);
                    UpdateSpawning(seconds);
                    UpdateEnemies(seconds);
                    UpdateBullets(seconds);
                    UpdateHumans(seconds);
                    if (State == ArcadeState.Playing && EnemiesLeft == 0)
                    {
                        Score += HumansAlive * 100 * Math.Min(Wave, 5);
                        bullets.Clear();
                        State = ArcadeState.WaveClear;
                        stateTimer = WavePause;
                    }
                    break;
                case ArcadeState.PlayerHit:
                    stateTimer -= seconds;
                    UpdateHumans(seconds);
                    if (stateTimer <= 0)
                    {
                        if (Lives > 0)
                        {
                            PlaceShip();
                            State = ArcadeState.Playing;
                        }
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
                    UpdateHumans(seconds);
                    if (stateTimer <= 0)
                    {
                        Wave++;
                        StartWave();
                    }
                    break;
                case ArcadeState.GameOver:
                    stateTimer -= seconds;
                    if (stateTimer <= 0 && input.Action) NewGame();
                    break;
            }
        }

        void UpdateShip(double seconds, ArcadeInput input)
        {
            if (input.Left) { shipVx -= ShipAcceleration * seconds; facing = -1; }
            if (input.Right) { shipVx += ShipAcceleration * seconds; facing = 1; }
            if (!input.Left && !input.Right)
                shipVx = shipVx > 0 ? Math.Max(0, shipVx - ShipDrag * seconds) : Math.Min(0, shipVx + ShipDrag * seconds);
            shipVx = Math.Max(-ShipMaxSpeed, Math.Min(ShipMaxSpeed, shipVx));
            shipX = WrapPosition(shipX + shipVx * seconds);

            if (input.Up) shipY -= ShipClimb * seconds;
            if (input.Down) shipY += ShipClimb * seconds;
            shipY = Math.Max(TopY, Math.Min(ShipLowestY, shipY));

            double target = shipX + facing * CameraLead;
            camera = WrapPosition(camera + Wrap(target - camera) * Math.Min(1, 4 * seconds));

            if (safeTimer > 0) safeTimer -= seconds;
            cooldown -= seconds;
            if (input.Fire && cooldown <= 0)
            {
                cooldown = FireCooldown;
                beams.Add(new Beam { X = shipX + (facing > 0 ? ShipRight.Width : 0), Y = shipY + 2, Direction = facing, Left = BeamSeconds });
            }
        }

        void UpdateBeams(double seconds)
        {
            foreach (var beam in beams)
            {
                beam.Left -= seconds;
                foreach (var enemy in enemies.ToList())
                {
                    double along = Wrap(enemy.X + LanderSprite.Width / 2.0 - beam.X) * beam.Direction;
                    if (along >= -LanderSprite.Width / 2.0 && along <= BeamLength && beam.Y >= enemy.Y - 1 && beam.Y <= enemy.Y + LanderSprite.Height) Kill(enemy);
                }
            }
            beams.RemoveAll(b => b.Left <= 0);
        }

        void Kill(Enemy enemy)
        {
            enemies.Remove(enemy);
            Score += enemy.IsMutant ? MutantScore : LanderScore;
            sparks.Burst(enemy.X + 4, enemy.Y + 4, enemy.IsMutant ? ArcadePalette.Magenta : ArcadePalette.Green, 10);
            if (enemy.Carrying != null)
            {
                enemy.Carrying.State = HumanState.Falling;
                enemy.Carrying.FallSpeed = 0;
                enemy.Carrying.FallStartY = enemy.Carrying.Y;
            }
        }

        void UpdateSpawning(double seconds)
        {
            if (spawnsLeft == 0) return;
            batchTimer -= seconds;
            if (batchTimer > 0) return;
            batchTimer = BatchSeconds;
            for (int i = 0; i < BatchSize && spawnsLeft > 0; i++, spawnsLeft--)
            {
                // Appear away from the ship so nothing lands on top of it.
                double x = WrapPosition(shipX + 180 + random.NextDouble() * (WorldWidth - 360));
                enemies.Add(new Enemy
                {
                    X = x,
                    Y = TopY,
                    CruiseY = 35 + random.NextDouble() * 45,
                    Speed = 18 + 2 * Wave + random.NextDouble() * 6,
                    FireTimer = 1.5 + random.NextDouble() * 2,
                    Wander = random.Next(2) == 0 ? 1 : -1
                });
            }
        }

        void UpdateEnemies(double seconds)
        {
            foreach (var enemy in enemies.ToList())
            {
                if (enemy.IsMutant) UpdateMutant(enemy, seconds);
                else UpdateLander(enemy, seconds);

                enemy.FireTimer -= seconds;
                if (enemy.FireTimer <= 0)
                {
                    enemy.FireTimer = (enemy.IsMutant ? 1 : 1.5) + random.NextDouble() * 2;
                    if (Math.Abs(ToScreen(enemy.X) - CenterX) < CenterX && bullets.Count < MaxBullets) FireAtShip(enemy);
                }

                if (safeTimer <= 0 && Math.Abs(Wrap(enemy.X - shipX)) < 10 && ArcadeMath.Overlaps(0, enemy.Y, 1, LanderSprite.Height, 0, shipY, 1, ShipRight.Height))
                {
                    HitShip();
                    return;
                }
            }
        }

        void UpdateLander(Enemy enemy, double seconds)
        {
            if (enemy.Carrying != null)
            {
                enemy.Y -= (12 + Wave) * seconds;
                enemy.Carrying.X = enemy.X + 3;
                enemy.Carrying.Y = enemy.Y + LanderSprite.Height;
                if (enemy.Y <= TopY)
                {
                    // Reaching the top kills the human and turns the lander into a mutant.
                    enemy.Carrying.State = HumanState.Dead;
                    enemy.Carrying = null;
                    enemy.IsMutant = true;
                    sparks.Burst(enemy.X + 4, enemy.Y + 4, ArcadePalette.Magenta, 8);
                }
                return;
            }

            if (enemy.Target == null || enemy.Target.State != HumanState.Walking) enemy.Target = PickTarget();
            if (enemy.Target == null)
            {
                if (HumansAlive == 0) enemy.IsMutant = true; // nothing left to abduct
                enemy.X = WrapPosition(enemy.X + enemy.Speed * enemy.Wander * seconds);
                return;
            }

            double dx = Wrap(enemy.Target.X - enemy.X - 3);
            enemy.X = WrapPosition(enemy.X + Math.Sign(dx) * Math.Min(Math.Abs(dx), enemy.Speed * seconds));
            double wantY = Math.Abs(dx) < 30 ? enemy.Target.Y - LanderSprite.Height : enemy.CruiseY;
            enemy.Y += Math.Sign(wantY - enemy.Y) * Math.Min(Math.Abs(wantY - enemy.Y), 22 * seconds);

            if (Math.Abs(dx) < 3 && Math.Abs(enemy.Y + LanderSprite.Height - enemy.Target.Y) < 3)
            {
                enemy.Carrying = enemy.Target;
                enemy.Carrying.State = HumanState.Carried;
                enemy.Target = null;
            }
        }

        Human PickTarget()
        {
            var free = humans.Where(h => h.State == HumanState.Walking && !enemies.Any(e => e.Target == h)).ToList();
            return free.Count == 0 ? null : free[random.Next(free.Count)];
        }

        void UpdateMutant(Enemy enemy, double seconds)
        {
            double speed = 40 + 3 * Wave;
            enemy.X = WrapPosition(enemy.X + Math.Sign(Wrap(shipX - enemy.X)) * speed * seconds + (random.NextDouble() - 0.5) * 2);
            enemy.Y += Math.Sign(shipY - enemy.Y) * speed * 0.6 * seconds + (random.NextDouble() - 0.5) * 2;
            enemy.Y = Math.Max(TopY, Math.Min(ShipLowestY + 8, enemy.Y));
        }

        void FireAtShip(Enemy enemy)
        {
            double dx = Wrap(shipX + 6 - enemy.X), dy = shipY + 2 - enemy.Y;
            double length = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            bullets.Add(new Bullet { X = enemy.X + 4, Y = enemy.Y + 4, Vx = dx / length * BulletSpeed, Vy = dy / length * BulletSpeed, Left = BulletSeconds });
        }

        void UpdateBullets(double seconds)
        {
            foreach (var bullet in bullets)
            {
                bullet.X = WrapPosition(bullet.X + bullet.Vx * seconds);
                bullet.Y += bullet.Vy * seconds;
                bullet.Left -= seconds;
            }
            bullets.RemoveAll(b => b.Left <= 0 || b.Y < TopY - 4 || b.Y > ViewHeight);

            if (safeTimer > 0 || State != ArcadeState.Playing) return;
            foreach (var bullet in bullets)
            {
                if (Math.Abs(Wrap(bullet.X - shipX - 6)) < 6 && bullet.Y >= shipY && bullet.Y <= shipY + ShipRight.Height)
                {
                    HitShip();
                    return;
                }
            }
        }

        void HitShip()
        {
            Lives--;
            sparks.Burst(shipX + 6, shipY + 2, ArcadePalette.White, 24);
            bullets.Clear();
            beams.Clear();
            foreach (var human in humans.Where(h => h.State == HumanState.OnShip))
            {
                human.State = HumanState.Falling;
                human.FallSpeed = 0;
                human.FallStartY = human.Y;
            }
            State = ArcadeState.PlayerHit;
            stateTimer = HitPause;
        }

        void UpdateHumans(double seconds)
        {
            foreach (var human in humans)
            {
                switch (human.State)
                {
                    case HumanState.Walking:
                        human.X = WrapPosition(human.X + human.Direction * 3 * seconds);
                        human.Y = GroundAt(human.X) - HumanSprite.Height;
                        break;
                    case HumanState.Falling:
                        UpdateFalling(human, seconds);
                        break;
                    case HumanState.OnShip:
                        human.X = shipX + 5;
                        human.Y = shipY + ShipRight.Height;
                        if (human.Y + HumanSprite.Height >= GroundAt(human.X))
                        {
                            human.Y = GroundAt(human.X) - HumanSprite.Height;
                            human.State = HumanState.Walking;
                            Score += SafeLandingScore;
                        }
                        break;
                }
            }
        }

        void UpdateFalling(Human human, double seconds)
        {
            human.FallSpeed = Math.Min(FallTopSpeed, human.FallSpeed + FallGravity * seconds);
            human.Y += human.FallSpeed * seconds;

            if (State == ArcadeState.Playing && Math.Abs(Wrap(human.X - shipX - 5)) < 9 && Math.Abs(human.Y - shipY - ShipRight.Height) < 7)
            {
                human.State = HumanState.OnShip;
                HumansCaught++;
                Score += CatchScore;
                return;
            }

            double ground = GroundAt(human.X) - HumanSprite.Height;
            if (human.Y < ground) return;
            human.Y = ground;
            if (ground - human.FallStartY <= SafeFallHeight)
            {
                human.State = HumanState.Walking;
                Score += SafeLandingScore;
            }
            else
            {
                human.State = HumanState.Dead;
                sparks.Burst(human.X + 1, human.Y + 3, ArcadePalette.Cyan, 6);
            }
        }

        public override void Draw(DrawingContext dc, ArcadeText text)
        {
            text.Draw(dc, "SCORE " + Score.ToString("00000"), 3, 0, 7, ArcadePalette.White, TextAlign.Left);
            text.Draw(dc, "HI " + HighScore.ToString("00000"), CenterX, 0, 7, ArcadePalette.Grey, TextAlign.Center);
            text.Draw(dc, "SHIPS " + Lives, ViewWidth - 3, 0, 7, ArcadePalette.Green, TextAlign.Right);

            if (State == ArcadeState.Title)
            {
                text.Draw(dc, "DEFENDER", CenterX, 22, 18, ArcadePalette.Cyan, TextAlign.Center);
                text.Draw(dc, "PRESS SPACE TO START", CenterX, 48, 8, ArcadePalette.White, TextAlign.Center);
                text.Draw(dc, "ARROWS FLY   SPACE FIRES", CenterX, 62, 8, ArcadePalette.Grey, TextAlign.Center);
                LanderSprite.Draw(dc, 34, 82, ArcadePalette.Green);
                text.Draw(dc, "LANDER STEALS HUMANS", 50, 82, 8, ArcadePalette.Grey, TextAlign.Left);
                MutantSprite.Draw(dc, 34, 97, ArcadePalette.Magenta);
                text.Draw(dc, "MUTANT HUNTS YOU", 50, 97, 8, ArcadePalette.Grey, TextAlign.Left);
                HumanSprite.Draw(dc, 37, 112, ArcadePalette.Cyan);
                text.Draw(dc, "CATCH THEM IF THEY FALL", 50, 112, 8, ArcadePalette.Grey, TextAlign.Left);
                return;
            }

            DrawRadar(dc);
            DrawGround(dc);

            foreach (var human in humans)
                if (human.State != HumanState.Dead && OnScreen(human.X)) HumanSprite.Draw(dc, ToScreen(human.X), human.Y, ArcadePalette.Cyan);
            foreach (var enemy in enemies)
                if (OnScreen(enemy.X)) (enemy.IsMutant ? MutantSprite : LanderSprite).Draw(dc, ToScreen(enemy.X), enemy.Y, enemy.IsMutant ? ArcadePalette.Magenta : ArcadePalette.Green);
            foreach (var bullet in bullets)
                if (OnScreen(bullet.X)) dc.DrawRectangle(ArcadePalette.White, null, new Rect(Math.Round(ToScreen(bullet.X)), Math.Round(bullet.Y), 2, 2));
            foreach (var beam in beams)
            {
                double start = ToScreen(beam.X);
                double left = beam.Direction > 0 ? start : start - BeamLength;
                dc.DrawRectangle(ArcadePalette.Yellow, null, new Rect(Math.Round(left), Math.Round(beam.Y), BeamLength, 1));
            }

            // Blink the ship while it can't be hit, so you can see that it is safe.
            bool blinkOff = safeTimer > 0 && (int)(safeTimer * 10) % 2 == 0;
            if ((State == ArcadeState.Playing || State == ArcadeState.WaveClear) && !blinkOff)
                (facing > 0 ? ShipRight : ShipLeft).Draw(dc, ToScreen(shipX), shipY, ArcadePalette.White);
            sparks.Draw(dc, ToScreen);

            if (State == ArcadeState.WaveClear)
                text.Draw(dc, "WAVE " + (Wave + 1), CenterX, 62, 14, ArcadePalette.White, TextAlign.Center);
            if (State == ArcadeState.GameOver)
            {
                text.Draw(dc, "GAME OVER", CenterX, 54, 16, ArcadePalette.Red, TextAlign.Center);
                text.Draw(dc, "PRESS SPACE TO PLAY AGAIN", CenterX, 76, 8, ArcadePalette.White, TextAlign.Center);
            }
        }

        bool OnScreen(double worldX) { return Math.Abs(Wrap(worldX - camera)) < CenterX + 16; }

        void DrawGround(DrawingContext dc)
        {
            const int Step = 4;
            double previous = GroundAt(camera - CenterX);
            for (int x = Step; x <= ViewWidth; x += Step)
            {
                double y = GroundAt(camera - CenterX + x);
                dc.DrawLine(GroundPen, new Point(x - Step, previous), new Point(x, y));
                previous = y;
            }
        }

        void DrawRadar(DrawingContext dc)
        {
            dc.DrawRectangle(null, RadarPen, new Rect(RadarLeft, RadarTop, RadarWidth, RadarHeight));
            double viewLeft = RadarX(camera - CenterX), viewWidth = ViewWidth / WorldWidth * RadarWidth;
            dc.DrawLine(RadarPen, new Point(viewLeft, RadarTop), new Point(viewLeft, RadarTop + 2));
            dc.DrawLine(RadarPen, new Point(viewLeft + viewWidth, RadarTop), new Point(viewLeft + viewWidth, RadarTop + 2));
            foreach (var human in humans)
                if (human.State != HumanState.Dead) RadarDot(dc, human.X, human.Y, ArcadePalette.Cyan);
            foreach (var enemy in enemies) RadarDot(dc, enemy.X, enemy.Y, enemy.IsMutant ? ArcadePalette.Magenta : ArcadePalette.Green);
            RadarDot(dc, shipX, shipY, ArcadePalette.White);
        }

        void RadarDot(DrawingContext dc, double worldX, double worldY, Brush brush)
        {
            double y = RadarTop + 1 + (worldY - TopY) / (ViewHeight - TopY) * (RadarHeight - 3);
            dc.DrawRectangle(brush, null, new Rect(Math.Round(RadarX(worldX)), Math.Round(y), 1, 1));
        }

        static double RadarX(double worldX) { return RadarLeft + WrapPosition(worldX) / WorldWidth * RadarWidth; }
    }
}
