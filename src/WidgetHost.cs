// Widget Host: shows every widgets\*.xaml file as its own borderless, transparent window.
// Written for the C# 5 compiler that ships with Windows, so no newer syntax. See build.cmd.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Draw = System.Drawing;
using WF = System.Windows.Forms;

namespace CustomWidgets
{
    static class Native
    {
        [DllImport("kernel32")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
                         ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32")] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS
        {
            public IntPtr hwnd, hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }

        public const int GWL_EXSTYLE = -20, GWLP_HWNDPARENT = -8;
        public const int WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
        public const int WM_WINDOWPOSCHANGING = 0x46;
        public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10;
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        [DllImport("user32")] public static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32")] public static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
        [DllImport("user32")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32")] public static extern bool DestroyIcon(IntPtr h);

        public const int AF_INET = 2, AF_INET6 = 23, TCP_TABLE_OWNER_PID_ALL = 5;
        [DllImport("iphlpapi")] public static extern int GetBestInterface(uint destination, out uint index);
        [DllImport("iphlpapi")] public static extern int GetExtendedTcpTable(IntPtr table, ref int size, bool sorted, int family, int tableClass, uint reserved);
    }

    class RelayCommand : ICommand
    {
        readonly Action<object> run;
        public RelayCommand(Action<object> run) { this.run = run; }
        public bool CanExecute(object p) { return true; }
        public void Execute(object p) { run(p); }
        public event EventHandler CanExecuteChanged { add { } remove { } }
    }

    /// <summary>Two-way text storage for widgets: {Binding Notes[todo]} is saved to notes\todo.txt.</summary>
    class NoteStore
    {
        readonly string dir;
        public NoteStore(string dir) { this.dir = dir; }

        string PathFor(string key)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) key = key.Replace(c, '_');
            return System.IO.Path.Combine(dir, key + ".txt");
        }

        public string this[string key]
        {
            get { var p = PathFor(key); return File.Exists(p) ? File.ReadAllText(p) : ""; }
            set { Directory.CreateDirectory(dir); File.WriteAllText(PathFor(key), value ?? ""); }
        }
    }

    /// <summary>The last two minutes of ping results for one host. A failed ping is stored as -1.</summary>
    class PingTrack
    {
        const int Length = 60;
        readonly List<long> samples = new List<long>();

        public void Add(long rtt)
        {
            samples.Add(rtt);
            if (samples.Count > Length) samples.RemoveAt(0);
        }

        public int Count { get { return samples.Count; } }

        public long LatestMs { get { return samples.Count == 0 ? -1 : samples[samples.Count - 1]; } }

        public string LatestText
        {
            get
            {
                if (samples.Count == 0) return "...";
                if (LatestMs < 0) return "no reply";
                return LatestMs == 0 ? "<1 ms" : LatestMs + " ms"; // Windows rounds sub-millisecond replies to 0
            }
        }

        public bool AnsweredWithin(int last) { return Recent(last).Any(s => s >= 0); }

        public double LossPercent(int last)
        {
            var recent = Recent(last);
            return recent.Count == 0 ? 0 : 100.0 * recent.Count(s => s < 0) / recent.Count;
        }

        /// <summary>Average change between one successful ping and the next.</summary>
        public double JitterMs(int last)
        {
            var ok = Recent(last).Where(s => s >= 0).ToList();
            if (ok.Count < 2) return 0;
            double sum = 0;
            for (int i = 1; i < ok.Count; i++) sum += Math.Abs(ok[i] - ok[i - 1]);
            return sum / (ok.Count - 1);
        }

        public double AverageMs(int last)
        {
            var ok = Recent(last).Where(s => s >= 0).ToList();
            return ok.Count == 0 ? 0 : ok.Average();
        }

        List<long> Recent(int last) { return samples.Skip(Math.Max(0, samples.Count - last)).ToList(); }

        /// <summary>x runs 0 to 295, y runs 0 to height. Failed pings are drawn at the very top.</summary>
        public PointCollection ToPoints(double height, double scaleMax)
        {
            var points = new PointCollection(samples.Count);
            int offset = Length - samples.Count;
            for (int i = 0; i < samples.Count; i++)
            {
                double value = samples[i] < 0 ? scaleMax : Math.Min(samples[i], scaleMax);
                points.Add(new Point(5 * (offset + i), height - value / scaleMax * height));
            }
            points.Freeze();
            return points;
        }

        public double MaxMs { get { return samples.Count == 0 ? 0 : samples.Max(); } }
    }

    class RankedItem
    {
        public string Name { get; set; }
        public int Count { get; set; }
    }

    class AdapterInfo
    {
        public NetworkInterface Nic;
        public string Name, Type, Speed, LocalIp, Gateway, Dns;
    }

    class ConnectionSnapshot
    {
        public int Established, Listening;
        public List<RankedItem> TopApps;
    }

    /// <summary>Everything the network widget binds to, as {Binding Net.Something}.</summary>
    class NetworkMonitor : INotifyPropertyChanged
    {
        const int GraphSamples = 120;
        const double GraphStep = 2.5, GraphHeight = 70, LatencyHeight = 28;
        const int HealthWindow = 15; // pings, so the last 30 seconds
        const string DefaultPingTarget = "1.1.1.1";

        public static readonly Brush GoodBrush = Frozen(0x6F, 0xD6, 0x8E);
        public static readonly Brush WarnBrush = Frozen(0xFF, 0xC2, 0x66);
        public static readonly Brush BadBrush = Frozen(0xFF, 0x70, 0x70);
        public static readonly Brush WaitBrush = Frozen(0x9A, 0xA1, 0xB2);

        public event PropertyChangedEventHandler PropertyChanged;

        public string Health { get; private set; }
        public string HealthDetail { get; private set; }
        public Brush HealthBrush { get; private set; }

        public string Adapter { get { return adapter == null ? "Not connected" : adapter.Name; } }
        public string AdapterType { get { return adapter == null ? "" : adapter.Type; } }
        public string LinkSpeed { get { return adapter == null ? "" : adapter.Speed; } }
        public string LocalIp { get { return adapter == null ? "-" : adapter.LocalIp; } }
        public string Gateway { get { return adapter == null ? "-" : adapter.Gateway; } }
        public string Dns { get { return adapter == null ? "-" : adapter.Dns; } }

        public string Down { get; private set; }
        public string Up { get; private set; }
        public string PeakDown { get { return FormatRate(peakDown); } }
        public string PeakUp { get { return FormatRate(peakUp); } }
        public string SessionDown { get { return FormatBytes(sessionDown); } }
        public string SessionUp { get { return FormatBytes(sessionUp); } }
        public string TodayDown { get { return FormatBytes(todayDown); } }
        public string TodayUp { get { return FormatBytes(todayUp); } }
        public long PacketProblems { get; private set; }
        public PointCollection DownPoints { get; private set; }
        public PointCollection UpPoints { get; private set; }
        public string GraphScale { get; private set; }

        public string PingTarget { get { return pingTarget; } }
        public string GatewayPing { get { return gatewayTrack.LatestText; } }
        public string InternetPing { get { return internetTrack.LatestText; } }
        public string Jitter { get { return internetTrack.Count < 2 ? "..." : internetTrack.JitterMs(int.MaxValue).ToString("0") + " ms"; } }
        public string Loss { get { return internetTrack.Count == 0 ? "..." : internetTrack.LossPercent(int.MaxValue).ToString("0") + "%"; } }
        public PointCollection GatewayPoints { get; private set; }
        public PointCollection InternetPoints { get; private set; }

        public int Established { get; private set; }
        public int Listening { get; private set; }
        public List<RankedItem> TopApps { get; private set; }

        readonly Settings settings;
        readonly string pingTarget;
        readonly PingTrack gatewayTrack = new PingTrack(), internetTrack = new PingTrack();
        readonly Queue<double> downHistory = new Queue<double>(), upHistory = new Queue<double>();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Dictionary<int, string> processNames = new Dictionary<int, string>();
        AdapterInfo adapter;
        long lastDown = -1, lastUp, problemBaseline;
        long sessionDown, sessionUp, todayDown, todayUp;
        double peakDown, peakUp;
        string today;
        int secondsSinceSave;
        bool pinging, scanning;

        public NetworkMonitor(Settings settings)
        {
            this.settings = settings;
            pingTarget = settings.Get("network.pingTarget", DefaultPingTarget);
            Down = Up = "0 KB/s";
            GraphScale = "";
            TopApps = new List<RankedItem>();
            today = settings.Get("network.day", "");
            if (today == DateTime.Today.ToString("yyyy-MM-dd"))
            {
                long.TryParse(settings.Get("network.todayDown", "0"), out todayDown);
                long.TryParse(settings.Get("network.todayUp", "0"), out todayUp);
            }
            today = DateTime.Today.ToString("yyyy-MM-dd");
            for (int i = 0; i < GraphSamples; i++) { downHistory.Enqueue(0); upHistory.Enqueue(0); }

            adapter = FindActiveAdapter();
            SetHealth("Checking", "Measuring the connection...", WaitBrush);
            DownPoints = UpPoints = GatewayPoints = InternetPoints = new PointCollection();

            // These run off the UI thread or asynchronously, so a slow network can't freeze the widgets.
            StartTimer(2, OnPingTimer);
            StartTimer(3, OnConnectionTimer);
            StartTimer(5, OnAdapterTimer);
        }

        static void StartTimer(int seconds, Action action)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += delegate { action(); };
            timer.Start();
        }

        /// <summary>Called once a second by LiveData.</summary>
        public void SampleTraffic()
        {
            double seconds = clock.Elapsed.TotalSeconds;
            clock.Restart();
            double downRate = 0, upRate = 0;

            if (adapter != null)
            {
                var stats = adapter.Nic.GetIPStatistics();
                long down = stats.BytesReceived, up = stats.BytesSent;
                long problems = stats.IncomingPacketsWithErrors + stats.IncomingPacketsDiscarded
                              + stats.OutgoingPacketsWithErrors + stats.OutgoingPacketsDiscarded;
                if (lastDown < 0)
                {
                    problemBaseline = problems; // counters restart when the adapter changes
                }
                else if (seconds > 0)
                {
                    long deltaDown = Math.Max(0, down - lastDown), deltaUp = Math.Max(0, up - lastUp);
                    downRate = deltaDown / seconds;
                    upRate = deltaUp / seconds;
                    sessionDown += deltaDown;
                    sessionUp += deltaUp;
                    AddToToday(deltaDown, deltaUp);
                }
                lastDown = down;
                lastUp = up;
                PacketProblems = Math.Max(0, problems - problemBaseline);
            }

            Down = FormatRate(downRate);
            Up = FormatRate(upRate);
            peakDown = Math.Max(peakDown, downRate);
            peakUp = Math.Max(peakUp, upRate);

            downHistory.Dequeue(); downHistory.Enqueue(downRate);
            upHistory.Dequeue(); upHistory.Enqueue(upRate);
            double scale = Math.Max(1024, Math.Max(downHistory.Max(), upHistory.Max()));
            GraphScale = FormatRate(scale);
            DownPoints = ToArea(downHistory, scale);
            UpPoints = ToArea(upHistory, scale);

            if (++secondsSinceSave >= 60) SaveToday();
            Raise();
        }

        void AddToToday(long down, long up)
        {
            var now = DateTime.Today.ToString("yyyy-MM-dd");
            if (now != today) { today = now; todayDown = 0; todayUp = 0; }
            todayDown += down;
            todayUp += up;
        }

        public void SaveToday()
        {
            secondsSinceSave = 0;
            settings.Set("network.day", today);
            settings.Set("network.todayDown", todayDown.ToString(CultureInfo.InvariantCulture));
            settings.Set("network.todayUp", todayUp.ToString(CultureInfo.InvariantCulture));
        }

        static PointCollection ToArea(IEnumerable<double> values, double scale)
        {
            var points = new PointCollection(GraphSamples + 2);
            points.Add(new Point(0, GraphHeight));
            int i = 0;
            foreach (var v in values) points.Add(new Point(GraphStep * i++, GraphHeight - v / scale * (GraphHeight - 2)));
            points.Add(new Point(GraphStep * (i - 1), GraphHeight));
            points.Freeze();
            return points;
        }

        async void OnPingTimer()
        {
            if (pinging) return;
            pinging = true;
            try
            {
                var gateway = PingOnce(adapter == null ? null : adapter.Gateway);
                var internet = PingOnce(pingTarget);
                var results = await Task.WhenAll(gateway, internet);
                gatewayTrack.Add(results[0]);
                internetTrack.Add(results[1]);

                double scale = Math.Max(50, Math.Max(gatewayTrack.MaxMs, internetTrack.MaxMs));
                GatewayPoints = gatewayTrack.ToPoints(LatencyHeight, scale);
                InternetPoints = internetTrack.ToPoints(LatencyHeight, scale);
                UpdateHealth();
                Raise();
            }
            finally { pinging = false; }
        }

        static async Task<long> PingOnce(string host)
        {
            if (string.IsNullOrEmpty(host) || host == "-") return -1;
            try
            {
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync(host, 1500);
                    return reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
                }
            }
            catch (PingException) { return -1; }
        }

        void UpdateHealth()
        {
            if (adapter == null) { SetHealth("Offline", "No network connection", BadBrush); return; }
            if (internetTrack.Count < 3) return;

            bool routerAnswers = gatewayTrack.AnsweredWithin(HealthWindow);
            if (!internetTrack.AnsweredWithin(3))
            {
                if (routerAnswers && gatewayTrack.AnsweredWithin(3)) SetHealth("No internet", "Your router answers, but the internet does not", BadBrush);
                else if (routerAnswers) SetHealth("Router lost", "Your router has stopped answering", BadBrush);
                else SetHealth("No internet", "The internet is not answering", BadBrush);
                return;
            }

            double loss = internetTrack.LossPercent(HealthWindow);
            double routerLoss = gatewayTrack.LossPercent(HealthWindow);
            double latency = internetTrack.AverageMs(HealthWindow);
            double jitter = internetTrack.JitterMs(HealthWindow);

            if (loss >= 5)
            {
                // Some routers never answer pings, so only blame the local link if this one normally does.
                bool localFault = routerAnswers && routerLoss >= 5;
                SetHealth("Unstable", loss.ToString("0") + "% packet loss " + (localFault ? "between this PC and your router" : "past your router"), WarnBrush);
            }
            else if (latency > 150) SetHealth("Slow", "High latency: " + latency.ToString("0") + " ms average", WarnBrush);
            else if (jitter > 40) SetHealth("Unstable", "Latency is jumping around: " + jitter.ToString("0") + " ms jitter", WarnBrush);
            else SetHealth("Good", "Connection is healthy", GoodBrush);
        }

        void SetHealth(string title, string detail, Brush brush)
        {
            Health = title;
            HealthDetail = detail;
            HealthBrush = brush;
        }

        async void OnAdapterTimer()
        {
            var found = await Task.Run(() => FindActiveAdapter());
            if (found == null || adapter == null || found.Nic.Id != adapter.Nic.Id) lastDown = -1;
            adapter = found;
            if (adapter == null) UpdateHealth();
            Raise();
        }

        /// <summary>Asks Windows which adapter it would use to reach the internet. Sends no traffic.</summary>
        static AdapterInfo FindActiveAdapter()
        {
            uint index;
            if (Native.GetBestInterface(0x08080808, out index) != 0) return null;
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || !nic.Supports(NetworkInterfaceComponent.IPv4)) continue;
                    var ip = nic.GetIPProperties();
                    if (ip.GetIPv4Properties().Index != index) continue;
                    return new AdapterInfo
                    {
                        Nic = nic,
                        Name = nic.Name,
                        Type = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi"
                             : nic.NetworkInterfaceType.ToString().StartsWith("Ethernet") ? "Ethernet"
                             : nic.NetworkInterfaceType.ToString(),
                        Speed = FormatLinkSpeed(nic.Speed),
                        LocalIp = FirstIPv4(ip.UnicastAddresses.Select(a => a.Address)),
                        Gateway = FirstIPv4(ip.GatewayAddresses.Select(a => a.Address)),
                        Dns = FirstIPv4(ip.DnsAddresses)
                    };
                }
            }
            catch (NetworkInformationException e) { Host.Log("Adapter lookup: " + e.Message); }
            return null;
        }

        static string FirstIPv4(IEnumerable<IPAddress> addresses)
        {
            var match = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            return match == null ? "-" : match.ToString();
        }

        async void OnConnectionTimer()
        {
            if (scanning) return;
            scanning = true;
            try
            {
                var snapshot = await Task.Run(() => ScanConnections());
                Established = snapshot.Established;
                Listening = snapshot.Listening;
                TopApps = snapshot.TopApps;
                Raise();
            }
            finally { scanning = false; }
        }

        ConnectionSnapshot ScanConnections()
        {
            var snapshot = new ConnectionSnapshot();
            var byPid = new Dictionary<int, int>();
            // Row layouts of MIB_TCPROW_OWNER_PID and MIB_TCP6ROW_OWNER_PID.
            ReadTcpTable(Native.AF_INET, 24, 0, 12, 4, 20, snapshot, byPid);
            ReadTcpTable(Native.AF_INET6, 56, 48, 24, 16, 52, snapshot, byPid);

            snapshot.TopApps = byPid
                .GroupBy(p => ProcessName(p.Key))
                .Select(g => new RankedItem { Name = g.Key, Count = g.Sum(p => p.Value) })
                .OrderByDescending(a => a.Count).ThenBy(a => a.Name)
                .Take(5).ToList();
            return snapshot;
        }

        static void ReadTcpTable(int family, int rowSize, int stateAt, int remoteAt, int remoteLength, int pidAt,
                                 ConnectionSnapshot snapshot, Dictionary<int, int> byPid)
        {
            int size = 0;
            Native.GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, Native.TCP_TABLE_OWNER_PID_ALL, 0);
            if (size == 0) return;
            size += rowSize * 16; // the table can grow between the two calls
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (Native.GetExtendedTcpTable(buffer, ref size, false, family, Native.TCP_TABLE_OWNER_PID_ALL, 0) != 0) return;
                int count = Marshal.ReadInt32(buffer);
                var row = new byte[rowSize];
                for (int i = 0; i < count; i++)
                {
                    Marshal.Copy(IntPtr.Add(buffer, 4 + i * rowSize), row, 0, rowSize);
                    int state = BitConverter.ToInt32(row, stateAt);
                    if (state == 2) { snapshot.Listening++; continue; }
                    if (state != 5) continue; // only established connections count below
                    if (IsLoopback(row, remoteAt, remoteLength)) continue;
                    snapshot.Established++;
                    int pid = BitConverter.ToInt32(row, pidAt);
                    int current;
                    byPid.TryGetValue(pid, out current);
                    byPid[pid] = current + 1;
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        static bool IsLoopback(byte[] row, int at, int length)
        {
            if (length == 4) return row[at] == 127;
            var bytes = new byte[16];
            Array.Copy(row, at, bytes, 0, 16);
            var address = new IPAddress(bytes);
            return IPAddress.IsLoopback(address) || (address.IsIPv4MappedToIPv6 && bytes[12] == 127);
        }

        string ProcessName(int pid)
        {
            string name;
            lock (processNames)
            {
                if (processNames.TryGetValue(pid, out name)) return name;
            }
            try
            {
                using (var process = Process.GetProcessById(pid)) name = process.ProcessName;
            }
            catch (ArgumentException) { name = "Unknown"; }
            catch (InvalidOperationException) { name = "Unknown"; }
            lock (processNames)
            {
                if (processNames.Count > 2000) processNames.Clear(); // process ids get reused
                processNames[pid] = name;
            }
            return name;
        }

        public static string FormatRate(double bytesPerSecond)
        {
            if (bytesPerSecond >= 1073741824) return (bytesPerSecond / 1073741824).ToString("0.00") + " GB/s";
            if (bytesPerSecond >= 1048576) return (bytesPerSecond / 1048576).ToString("0.0") + " MB/s";
            return (bytesPerSecond / 1024).ToString("0") + " KB/s";
        }

        static string FormatBytes(long bytes)
        {
            if (bytes >= 1073741824) return (bytes / 1073741824.0).ToString("0.00") + " GB";
            if (bytes >= 1048576) return (bytes / 1048576.0).ToString("0.0") + " MB";
            return (bytes / 1024.0).ToString("0") + " KB";
        }

        static string FormatLinkSpeed(long bitsPerSecond)
        {
            if (bitsPerSecond <= 0) return "";
            if (bitsPerSecond >= 1000000000) return (bitsPerSecond / 1000000000.0).ToString("0.#") + " Gbps";
            return (bitsPerSecond / 1000000.0).ToString("0") + " Mbps";
        }

        static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        void Raise()
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    class PiholeAuthException : Exception
    {
        public PiholeAuthException(string message) : base(message) { }
    }

    /// <summary>Read-only stats from a Pi-hole v6 server, as {Binding Pihole.Something}.</summary>
    class PiholeMonitor : INotifyPropertyChanged
    {
        const int PollSeconds = 5, ListsEveryPolls = 6, HistoryEveryPolls = 12, TopCount = 5;
        const double GraphWidth = 298, GraphHeight = 56;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Status { get; private set; }
        public string StatusDetail { get; private set; }
        public Brush StatusBrush { get; private set; }

        public string Total { get; private set; }
        public string Blocked { get; private set; }
        public double PercentBlocked { get; private set; }
        public string PercentText { get; private set; }
        public string UniqueDomains { get; private set; }
        public string CachedPercent { get; private set; }
        public string Rate { get; private set; }
        public string Clients { get; private set; }
        public string BlockList { get; private set; }
        public string BlockListAge { get; private set; }
        public List<RankedItem> TopBlocked { get; private set; }
        public List<RankedItem> TopClients { get; private set; }
        public PointCollection HistoryTotal { get; private set; }
        public PointCollection HistoryBlocked { get; private set; }
        public string HistoryScale { get; private set; }

        readonly NetworkMonitor net;
        readonly string baseUrl, password;
        readonly HttpClient client;
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        string sessionId;
        int polls;
        bool busy;

        public PiholeMonitor(Settings settings, NetworkMonitor net)
        {
            this.net = net;
            baseUrl = settings.Get("pihole.url", "").Trim().TrimEnd('/');
            password = settings.Get("pihole.password", "");
            Total = Blocked = PercentText = UniqueDomains = CachedPercent = Rate = Clients = BlockList = "-";
            BlockListAge = HistoryScale = "";
            TopBlocked = new List<RankedItem>();
            TopClients = new List<RankedItem>();
            HistoryTotal = HistoryBlocked = new PointCollection();

            if (baseUrl.Length == 0)
            {
                SetStatus("Not set up", "Add pihole.url to state.ini", NetworkMonitor.WaitBrush);
                return;
            }

            SetStatus("Connecting", baseUrl, NetworkMonitor.WaitBrush);
            client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(4) };
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(PollSeconds) };
            timer.Tick += delegate { Refresh(); };
            timer.Start();
            timer.Dispatcher.BeginInvoke(new Action(Refresh)); // runs as soon as the app is up
        }

        async void Refresh()
        {
            if (busy) return;
            busy = true;
            try
            {
                var summary = await GetJson("/api/stats/summary");
                var blocking = await GetJson("/api/dns/blocking");
                ApplySummary(summary);
                ApplyBlocking(blocking);

                if (polls % ListsEveryPolls == 0)
                {
                    TopBlocked = ReadTopBlocked(await GetJson("/api/stats/top_domains?blocked=true&count=" + TopCount));
                    TopClients = ReadTopClients(await GetJson("/api/stats/top_clients?count=" + TopCount));
                }
                if (polls % HistoryEveryPolls == 0) ApplyHistory(await GetJson("/api/history"));
                polls++;
            }
            catch (PiholeAuthException e) { SetStatus("Password needed", e.Message, NetworkMonitor.WarnBrush); }
            catch (HttpRequestException) { SetUnreachable(); }
            catch (TaskCanceledException) { SetUnreachable(); }
            catch (ArgumentException e) { SetStatus("Bad response", e.Message, NetworkMonitor.BadBrush); } // invalid JSON
            catch (InvalidOperationException e) { SetStatus("Check pihole.url", e.Message, NetworkMonitor.BadBrush); }
            catch (FormatException e) { SetStatus("Check pihole.url", e.Message, NetworkMonitor.BadBrush); }
            finally
            {
                busy = false;
                Raise();
            }
        }

        void SetUnreachable()
        {
            SetStatus("Unreachable", "No answer from " + baseUrl + ". DNS may be failing too.", NetworkMonitor.BadBrush);
        }

        async Task<Dictionary<string, object>> GetJson(string path)
        {
            var response = await Send(path);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                if (password.Length == 0) throw new PiholeAuthException("Add pihole.password to state.ini");
                await LogIn();
                response = await Send(path);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    response.Dispose();
                    throw new PiholeAuthException("Pi-hole rejected the login");
                }
            }
            using (response)
            {
                response.EnsureSuccessStatusCode();
                var parsed = json.DeserializeObject(await response.Content.ReadAsStringAsync()) as Dictionary<string, object>;
                if (parsed == null) throw new ArgumentException("Pi-hole did not return a JSON object");
                return parsed;
            }
        }

        async Task<HttpResponseMessage> Send(string path)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + path))
            {
                if (sessionId != null) request.Headers.Add("X-FTL-SID", sessionId);
                return await client.SendAsync(request);
            }
        }

        async Task LogIn()
        {
            sessionId = null;
            var body = json.Serialize(new Dictionary<string, string> { { "password", password } });
            using (var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"))
            using (var response = await client.PostAsync(baseUrl + "/api/auth", content))
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized) throw new PiholeAuthException("Pi-hole rejected pihole.password");
                if ((int)response.StatusCode == 429) throw new PiholeAuthException("Pi-hole has too many active logins");
                response.EnsureSuccessStatusCode();
                var parsed = json.DeserializeObject(await response.Content.ReadAsStringAsync()) as Dictionary<string, object>;
                sessionId = Text(Child(parsed, "session"), "sid");
                if (string.IsNullOrEmpty(sessionId)) { sessionId = null; throw new PiholeAuthException("Pi-hole rejected pihole.password"); }
            }
        }

        void ApplySummary(Dictionary<string, object> summary)
        {
            var queries = Child(summary, "queries");
            double total = Number(queries, "total");
            Total = total.ToString("N0");
            Blocked = Number(queries, "blocked").ToString("N0");
            PercentBlocked = Number(queries, "percent_blocked");
            PercentText = PercentBlocked.ToString("0.0") + "%";
            UniqueDomains = Number(queries, "unique_domains").ToString("N0");
            CachedPercent = total > 0 ? (100 * Number(queries, "cached") / total).ToString("0") + "%" : "-";
            Rate = Number(queries, "frequency").ToString("0.0") + "/s";
            Clients = Number(Child(summary, "clients"), "active").ToString("N0");

            var gravity = Child(summary, "gravity");
            BlockList = Number(gravity, "domains_being_blocked").ToString("N0");
            double updated = Number(gravity, "last_update");
            BlockListAge = updated > 0 ? FormatAge(DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds((long)updated)) : "";
        }

        void ApplyBlocking(Dictionary<string, object> blocking)
        {
            var state = Text(blocking, "blocking");
            double timer = Number(blocking, "timer");
            string countdown = timer > 0 ? TimeSpan.FromSeconds(timer).ToString(@"m\:ss") : "";
            if (state == "enabled")
                SetStatus("Blocking on", countdown.Length > 0 ? "Turns off in " + countdown : "Pi-hole is filtering your network", NetworkMonitor.GoodBrush);
            else if (state == "disabled")
                SetStatus("Blocking off", countdown.Length > 0 ? "Turns back on in " + countdown : "Ads and trackers are not being blocked", NetworkMonitor.WarnBrush);
            else
                SetStatus("Blocking unknown", "Pi-hole reports: " + state, NetworkMonitor.WarnBrush);
        }

        static List<RankedItem> ReadTopBlocked(Dictionary<string, object> data)
        {
            return Items(data, "domains").Select(d => new RankedItem { Name = Text(d, "domain"), Count = (int)Number(d, "count") }).ToList();
        }

        List<RankedItem> ReadTopClients(Dictionary<string, object> data)
        {
            return Items(data, "clients").Select(c =>
            {
                string name = Text(c, "name"), ip = Text(c, "ip");
                if (string.IsNullOrEmpty(name)) name = ip == net.LocalIp ? "This PC" : ip;
                return new RankedItem { Name = name, Count = (int)Number(c, "count") };
            }).ToList();
        }

        void ApplyHistory(Dictionary<string, object> data)
        {
            var buckets = Items(data, "history").ToList();
            if (buckets.Count < 2) return;
            double max = Math.Max(1, buckets.Max(b => Number(b, "total")));
            HistoryTotal = ToArea(buckets.Select(b => Number(b, "total")).ToList(), max);
            HistoryBlocked = ToArea(buckets.Select(b => Number(b, "blocked")).ToList(), max);
            HistoryScale = max.ToString("N0") + " per 10 min";
        }

        static PointCollection ToArea(List<double> values, double max)
        {
            double step = GraphWidth / (values.Count - 1);
            var points = new PointCollection(values.Count + 2);
            points.Add(new Point(0, GraphHeight));
            for (int i = 0; i < values.Count; i++) points.Add(new Point(i * step, GraphHeight - values[i] / max * (GraphHeight - 2)));
            points.Add(new Point(GraphWidth, GraphHeight));
            points.Freeze();
            return points;
        }

        static string FormatAge(TimeSpan age)
        {
            if (age.TotalHours < 1) return "updated just now";
            if (age.TotalDays < 1) return "updated " + (int)age.TotalHours + "h ago";
            return "updated " + (int)age.TotalDays + "d ago";
        }

        static Dictionary<string, object> Child(Dictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        static double Number(Dictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : 0;
        }

        static string Text(Dictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) ? value as string : null;
        }

        static IEnumerable<Dictionary<string, object>> Items(Dictionary<string, object> data, string key)
        {
            object value;
            var list = data != null && data.TryGetValue(key, out value) ? value as System.Collections.IEnumerable : null;
            return list == null ? Enumerable.Empty<Dictionary<string, object>>() : list.OfType<Dictionary<string, object>>();
        }

        void SetStatus(string title, string detail, Brush brush)
        {
            Status = title;
            StatusDetail = detail;
            StatusBrush = brush;
        }

        void Raise()
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    /// <summary>The DataContext of every widget. These properties are what XAML can bind to.</summary>
    class LiveData : INotifyPropertyChanged
    {
        const int HistoryLength = 60;
        static readonly string[] EverySecond =
        {
            "Now", "Cpu", "CpuPoints", "Ram", "RamUsedGb", "RamTotalGb", "NetDown", "NetUp",
            "Uptime", "Battery", "HasBattery", "IsCharging"
        };

        public event PropertyChangedEventHandler PropertyChanged;

        public DateTime Now { get; private set; }
        public double Cpu { get; private set; }
        public PointCollection CpuPoints { get; private set; }
        public double Ram { get; private set; }
        public double RamUsedGb { get; private set; }
        public double RamTotalGb { get; private set; }
        public double Disk { get; private set; }
        public double DiskFreeGb { get; private set; }
        public NetworkMonitor Net { get; private set; }
        public PiholeMonitor Pihole { get; private set; }
        public string NetDown { get { return Net.Down; } }
        public string NetUp { get { return Net.Up; } }
        public string Uptime { get; private set; }
        public int Battery { get; private set; }
        public bool HasBattery { get; private set; }
        public bool IsCharging { get; private set; }

        public NoteStore Notes { get; private set; }
        public Settings Settings { get; private set; }
        public ICommand Run { get; private set; }
        public ICommand Media { get; private set; }

        readonly Queue<double> history = new Queue<double>();
        long lastIdle, lastKernel, lastUser;
        int tick;

        public LiveData(string notesDir, Settings settings)
        {
            Notes = new NoteStore(notesDir);
            Settings = settings;
            Run = new RelayCommand(StartTarget);
            Media = new RelayCommand(SendMediaKey);
            Net = new NetworkMonitor(settings);
            Pihole = new PiholeMonitor(settings, Net);
            Native.GetSystemTimes(out lastIdle, out lastKernel, out lastUser);
            for (int i = 0; i < HistoryLength; i++) history.Enqueue(0);
            Sample();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += delegate { Sample(); };
            timer.Start();
        }

        void Sample()
        {
            Now = DateTime.Now;
            SampleCpu();
            SampleMemory();
            Net.SampleTraffic();
            if (tick++ % 30 == 0) SampleDisk();

            var up = TimeSpan.FromMilliseconds(Environment.TickCount & int.MaxValue);
            Uptime = string.Format("{0}d {1}h {2}m", up.Days, up.Hours, up.Minutes);

            var power = WF.SystemInformation.PowerStatus;
            HasBattery = (power.BatteryChargeStatus & WF.BatteryChargeStatus.NoSystemBattery) == 0;
            Battery = (int)Math.Round(power.BatteryLifePercent * 100);
            IsCharging = power.PowerLineStatus == WF.PowerLineStatus.Online;

            foreach (var name in EverySecond) Raise(name);
        }

        void SampleCpu()
        {
            long idle, kernel, user;
            Native.GetSystemTimes(out idle, out kernel, out user);
            long total = (kernel - lastKernel) + (user - lastUser); // kernel time includes idle
            if (total > 0) Cpu = Math.Max(0, Math.Min(100, 100.0 * (total - (idle - lastIdle)) / total));
            lastIdle = idle; lastKernel = kernel; lastUser = user;

            history.Dequeue();
            history.Enqueue(Cpu);
            var points = new PointCollection(HistoryLength);
            int x = 0;
            foreach (var v in history) points.Add(new Point(4 * x++, 40 - v * 0.4));
            points.Freeze();
            CpuPoints = points;
        }

        void SampleMemory()
        {
            var m = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX)) };
            if (!Native.GlobalMemoryStatusEx(ref m)) return;
            Ram = m.dwMemoryLoad;
            RamTotalGb = m.ullTotalPhys / 1073741824.0;
            RamUsedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
        }

        void SampleDisk()
        {
            try
            {
                var d = new DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory));
                DiskFreeGb = d.AvailableFreeSpace / 1073741824.0;
                Disk = 100.0 * (d.TotalSize - d.AvailableFreeSpace) / d.TotalSize;
                Raise("Disk"); Raise("DiskFreeGb");
            }
            catch (IOException) { }
        }

        static void StartTarget(object parameter)
        {
            var target = Convert.ToString(parameter);
            if (string.IsNullOrWhiteSpace(target)) return;
            var parts = target.Split(new[] { '|' }, 2); // "program|arguments"
            try
            {
                Process.Start(new ProcessStartInfo(parts[0].Trim()) { UseShellExecute = true, Arguments = parts.Length > 1 ? parts[1].Trim() : "" });
            }
            catch (Win32Exception e) { Host.Log("Run '" + target + "': " + e.Message); }
        }

        static void SendMediaKey(object parameter)
        {
            byte vk;
            switch (Convert.ToString(parameter))
            {
                case "play": vk = 0xB3; break;
                case "next": vk = 0xB0; break;
                case "prev": vk = 0xB1; break;
                case "mute": vk = 0xAD; break;
                case "volup": vk = 0xAF; break;
                case "voldown": vk = 0xAE; break;
                default: return;
            }
            Native.keybd_event(vk, 0, 1, UIntPtr.Zero);
            Native.keybd_event(vk, 0, 3, UIntPtr.Zero);
        }

        void Raise(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>Simple key=value settings next to the exe.</summary>
    class Settings
    {
        readonly string path;
        readonly Dictionary<string, string> values = new Dictionary<string, string>();

        public Settings(string path)
        {
            this.path = path;
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadAllLines(path))
            {
                int i = line.IndexOf('=');
                if (i > 0) values[line.Substring(0, i)] = line.Substring(i + 1);
            }
        }

        public string Get(string key, string fallback)
        {
            string v;
            return values.TryGetValue(key, out v) ? v : fallback;
        }

        public double? GetDouble(string key)
        {
            double d;
            return double.TryParse(Get(key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : (double?)null;
        }

        public void Set(string key, string value) { values[key] = value; Save(); }

        public void SetPosition(string name, double x, double y)
        {
            values[name + ".x"] = x.ToString(CultureInfo.InvariantCulture);
            values[name + ".y"] = y.ToString(CultureInfo.InvariantCulture);
            Save();
        }

        void Save()
        {
            File.WriteAllLines(path, values.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value));
        }
    }

    /// <summary>One widget: a frameless, per-pixel transparent window whose content is a XAML file.</summary>
    class WidgetWindow : Window
    {
        public const string LayerDesktop = "desktop", LayerNormal = "normal", LayerTop = "top";

        public readonly string WidgetName;
        public readonly string FilePath;
        readonly Host host;
        string layer;
        MenuItem[] layerItems;

        public WidgetWindow(Host host, string path)
        {
            this.host = host;
            FilePath = path;
            WidgetName = System.IO.Path.GetFileNameWithoutExtension(path);
            Title = WidgetName;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;
            Top = -32000;
            DataContext = host.Data;

            MouseLeftButtonDown += OnDragStart;
            ContextMenu = BuildMenu();
            SourceInitialized += OnSourceInitialized;
            LoadContent();
            layer = host.Settings.Get(WidgetName + ".layer", HasHint(LayerTop) ? LayerTop : LayerDesktop);
        }

        /// <summary>A widget's root Tag can hold hints: "bottom-center" for first placement, "top" for the starting layer.</summary>
        public bool HasHint(string hint)
        {
            var root = Content as FrameworkElement;
            var tag = root == null ? null : root.Tag as string;
            return tag != null && tag.Split(' ').Contains(hint);
        }

        public void LoadContent()
        {
            try
            {
                var bytes = Host.ReadAllBytesWithRetry(FilePath);
                var context = new ParserContext { BaseUri = new Uri(FilePath) };
                var element = XamlReader.Load(new MemoryStream(bytes), context) as FrameworkElement;
                if (element == null) throw new InvalidDataException("The root of a widget must be a visual element such as Border or Grid.");
                Content = element;
            }
            catch (Exception e)
            {
                if (e is OutOfMemoryException) throw;
                Content = ErrorCard(e.Message);
                Host.Log(WidgetName + ": " + e.Message);
            }
        }

        FrameworkElement ErrorCard(string message)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x5A, 0x1E, 0x24)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Margin = new Thickness(12),
                Child = new TextBlock
                {
                    Text = WidgetName + ".xaml has a problem:\n" + message,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 360
                }
            };
        }

        void OnDragStart(object sender, MouseButtonEventArgs e)
        {
            if (host.Locked || e.ButtonState != MouseButtonState.Pressed) return;
            DragMove();
            host.Settings.SetPosition(WidgetName, Left, Top);
        }

        void OnSourceInitialized(object sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // Tool windows stay out of Alt+Tab and the taskbar.
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, (ex | Native.WS_EX_TOOLWINDOW) & ~Native.WS_EX_APPWINDOW);
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            ApplyLayer();
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_WINDOWPOSCHANGING && layer == LayerDesktop)
            {
                // Keep "on desktop" widgets behind other windows, even after being clicked.
                var pos = (Native.WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(Native.WINDOWPOS));
                if ((pos.flags & Native.SWP_NOZORDER) == 0)
                {
                    pos.hwndInsertAfter = Native.HWND_BOTTOM;
                    Marshal.StructureToPtr(pos, lParam, false);
                }
            }
            return IntPtr.Zero;
        }

        void ApplyLayer()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            Topmost = layer == LayerTop;
            // Owned by the desktop window so "Show desktop" (Win+D) does not hide the widget.
            var owner = layer == LayerDesktop ? Native.FindWindow("Progman", null) : IntPtr.Zero;
            Native.SetWindowLongPtr(hwnd, Native.GWLP_HWNDPARENT, owner);
            if (layer == LayerDesktop)
                Native.SetWindowPos(hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        ContextMenu BuildMenu()
        {
            var menu = new ContextMenu();
            layerItems = new[]
            {
                LayerItem("On desktop", LayerDesktop),
                LayerItem("Normal window", LayerNormal),
                LayerItem("Always on top", LayerTop)
            };
            foreach (var item in layerItems) menu.Items.Add(item);
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Edit XAML", delegate { Process.Start("notepad.exe", "\"" + FilePath + "\""); }));
            menu.Items.Add(Item("Hide this widget", delegate { host.SetHidden(this, true); }));
            menu.Opened += delegate
            {
                foreach (var item in layerItems) item.IsChecked = (string)item.Tag == layer;
            };
            return menu;
        }

        MenuItem LayerItem(string header, string value)
        {
            var item = Item(header, delegate
            {
                layer = value;
                host.Settings.Set(WidgetName + ".layer", value);
                ApplyLayer();
            });
            item.Tag = value;
            return item;
        }

        static MenuItem Item(string header, RoutedEventHandler click)
        {
            var item = new MenuItem { Header = header };
            item.Click += click;
            return item;
        }
    }

    /// <summary>Owns the widget windows, the tray icon and the live reload of the widgets folder.</summary>
    class Host
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "CustomWidgets";
        const double Gap = 8;

        static string baseDir;

        public readonly LiveData Data;
        public readonly Settings Settings;
        public bool Locked { get; private set; }

        readonly string widgetsDir;
        readonly Dictionary<string, WidgetWindow> windows = new Dictionary<string, WidgetWindow>(StringComparer.OrdinalIgnoreCase);
        readonly DispatcherTimer reloadTimer;
        FileSystemWatcher watcher;
        WF.NotifyIcon tray;

        public Host(string dir)
        {
            baseDir = dir;
            widgetsDir = System.IO.Path.Combine(dir, "widgets");
            Directory.CreateDirectory(widgetsDir);
            Settings = new Settings(System.IO.Path.Combine(dir, "state.ini"));
            Locked = Settings.Get("locked", "0") == "1";
            Data = new LiveData(System.IO.Path.Combine(dir, "notes"), Settings);

            reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            reloadTimer.Tick += delegate { reloadTimer.Stop(); Sync(); };

            Sync();
            StartWatching();
            CreateTray();
        }

        public static void Log(string message)
        {
            try { File.AppendAllText(System.IO.Path.Combine(baseDir, "error.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine); }
            catch (IOException) { }
        }

        public static byte[] ReadAllBytesWithRetry(string path)
        {
            // Editors can briefly hold the file while saving.
            for (int attempt = 0; ; attempt++)
            {
                try { return File.ReadAllBytes(path); }
                catch (IOException)
                {
                    if (attempt == 4) throw;
                    Thread.Sleep(60);
                }
            }
        }

        void LoadTheme()
        {
            var path = System.IO.Path.Combine(widgetsDir, "_theme.xaml");
            if (!File.Exists(path)) { Application.Current.Resources = new ResourceDictionary(); return; }
            try
            {
                var bytes = ReadAllBytesWithRetry(path);
                Application.Current.Resources = (ResourceDictionary)XamlReader.Load(new MemoryStream(bytes));
            }
            catch (Exception e)
            {
                if (e is OutOfMemoryException) throw;
                Log("_theme.xaml: " + e.Message); // keep the previous theme until the file is fixed
            }
        }

        void Sync()
        {
            LoadTheme();

            // Files that start with an underscore are shared resources, not widgets.
            var files = Directory.GetFiles(widgetsDir, "*.xaml")
                .Where(f => !System.IO.Path.GetFileName(f).StartsWith("_"))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var gone in windows.Keys.Where(k => !files.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
            {
                windows[gone].Close();
                windows.Remove(gone);
            }

            foreach (var file in files)
            {
                WidgetWindow window;
                if (windows.TryGetValue(file, out window)) { window.LoadContent(); continue; }
                window = new WidgetWindow(this, file);
                windows[file] = window;
                if (Settings.Get(window.WidgetName + ".hidden", "0") != "1") ShowWidget(window);
            }
        }

        void ShowWidget(WidgetWindow window)
        {
            window.Show();
            window.UpdateLayout();
            var x = Settings.GetDouble(window.WidgetName + ".x");
            var y = Settings.GetDouble(window.WidgetName + ".y");
            if (x.HasValue && y.HasValue)
            {
                window.Left = Clamp(x.Value, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - window.ActualWidth);
                window.Top = Clamp(y.Value, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - window.ActualHeight);
            }
            else
            {
                var area = SystemParameters.WorkArea;
                var spot = window.HasHint("bottom-center")
                    ? new Point(area.Left + (area.Width - window.ActualWidth) / 2, area.Bottom - window.ActualHeight)
                    : FindFreeSpot(window);
                window.Left = spot.X;
                window.Top = spot.Y;
                Settings.SetPosition(window.WidgetName, spot.X, spot.Y);
            }
        }

        static double Clamp(double v, double min, double max) { return Math.Max(min, Math.Min(max, v)); }

        /// <summary>First free place, in columns from the right edge of the screen.</summary>
        Point FindFreeSpot(WidgetWindow window)
        {
            var area = SystemParameters.WorkArea;
            var placed = windows.Values
                .Where(w => w != window && w.IsVisible)
                .Select(w => new Rect(w.Left, w.Top, w.ActualWidth, w.ActualHeight)).ToList();

            for (double x = area.Right - window.ActualWidth - Gap; x >= area.Left; x -= Gap * 4)
            {
                for (double y = area.Top + Gap; y + window.ActualHeight <= area.Bottom; y += Gap)
                {
                    var r = new Rect(x, y, window.ActualWidth, window.ActualHeight);
                    if (!placed.Any(p => p.IntersectsWith(r))) return new Point(x, y);
                }
            }
            return new Point(area.Left + Gap, area.Top + Gap);
        }

        public void SetHidden(WidgetWindow window, bool hidden)
        {
            Settings.Set(window.WidgetName + ".hidden", hidden ? "1" : "0");
            if (hidden) window.Hide(); else ShowWidget(window);
        }

        void StartWatching()
        {
            watcher = new FileSystemWatcher(widgetsDir, "*.xaml")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
            };
            FileSystemEventHandler changed = delegate { QueueReload(); };
            watcher.Changed += changed;
            watcher.Created += changed;
            watcher.Deleted += changed;
            watcher.Renamed += delegate { QueueReload(); };
            watcher.EnableRaisingEvents = true;
        }

        void QueueReload()
        {
            reloadTimer.Dispatcher.BeginInvoke(new Action(delegate
            {
                reloadTimer.Stop();
                reloadTimer.Start();
            }));
        }

        void CreateTray()
        {
            var menu = new WF.ContextMenuStrip();
            menu.Opening += delegate { BuildTrayMenu(menu); };
            // The strip has to have an item, or WinForms cancels the first opening.
            menu.Items.Add("Widgets");
            tray = new WF.NotifyIcon { Icon = MakeTrayIcon(), Text = "Custom Widgets", Visible = true, ContextMenuStrip = menu };
        }

        void BuildTrayMenu(WF.ContextMenuStrip menu)
        {
            menu.Items.Clear();
            foreach (var window in windows.Values.OrderBy(w => w.WidgetName))
            {
                var w = window;
                var item = new WF.ToolStripMenuItem(w.WidgetName) { Checked = w.IsVisible };
                item.Click += delegate { SetHidden(w, w.IsVisible); };
                menu.Items.Add(item);
            }
            menu.Items.Add(new WF.ToolStripSeparator());

            var lockItem = new WF.ToolStripMenuItem("Lock positions") { Checked = Locked };
            lockItem.Click += delegate { Locked = !Locked; Settings.Set("locked", Locked ? "1" : "0"); };
            menu.Items.Add(lockItem);

            var startup = new WF.ToolStripMenuItem("Start with Windows") { Checked = IsStartupEnabled() };
            startup.Click += delegate { SetStartup(!IsStartupEnabled()); };
            menu.Items.Add(startup);

            menu.Items.Add("Open widgets folder", null, delegate { Process.Start("explorer.exe", "\"" + widgetsDir + "\""); });
            menu.Items.Add("Reload", null, delegate { Sync(); });
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { Shutdown(); });
        }

        static bool IsStartupEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && key.GetValue(RunValue) != null;
        }

        static void SetStartup(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(RunValue, "\"" + Process.GetCurrentProcess().MainModule.FileName + "\"");
                else key.DeleteValue(RunValue, false);
            }
        }

        static Draw.Icon MakeTrayIcon()
        {
            using (var bitmap = new Draw.Bitmap(32, 32))
            using (var g = Draw.Graphics.FromImage(bitmap))
            using (var a = new Draw.SolidBrush(Draw.Color.FromArgb(0x8F, 0xA8, 0xFF)))
            using (var b = new Draw.SolidBrush(Draw.Color.FromArgb(0xE8, 0xEA, 0xF0)))
            {
                g.FillRectangle(a, 2, 2, 12, 12);
                g.FillRectangle(b, 18, 2, 12, 12);
                g.FillRectangle(b, 2, 18, 12, 12);
                g.FillRectangle(a, 18, 18, 12, 12);
                var handle = bitmap.GetHicon();
                using (var temp = Draw.Icon.FromHandle(handle))
                {
                    var icon = (Draw.Icon)temp.Clone();
                    Native.DestroyIcon(handle);
                    return icon;
                }
            }
        }

        public void Shutdown()
        {
            Data.Net.SaveToday();
            tray.Visible = false;
            tray.Dispose();
            watcher.Dispose();
            Application.Current.Shutdown();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool first;
            using (new Mutex(true, "CustomWidgets.Host.SingleInstance", out first))
            {
                if (!first) return;

                // Dates and numbers in bindings follow the Windows region setting.
                FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
                    new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (s, e) =>
                {
                    Host.Log(e.Exception.ToString()); // a faulty widget should not take the others down
                    e.Handled = true;
                };
                var host = new Host(AppDomain.CurrentDomain.BaseDirectory);
                app.Run();
                GC.KeepAlive(host);
            }
        }
    }
}
