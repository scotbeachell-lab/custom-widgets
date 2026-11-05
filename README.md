# Custom Widgets

Borderless desktop widgets for Windows. Each `.xaml` file in `widgets\` becomes its own frameless,
transparent window. Nothing to install: it uses the .NET Framework and WPF that come with Windows.

## Run

Run `build.cmd` once, then start `WidgetHost.exe`.

- **Move a widget:** drag it. Positions are remembered.
- **Right-click a widget:** on desktop, normal window, or always on top; edit; hide.
- **Tray icon (right-click):** show or hide widgets, lock positions, start with Windows, exit.

## Make your own widget

Add a file such as `widgets\hello.xaml`. It appears as soon as you save, and edits apply on each
save. If the XAML has a mistake, the widget shows the error instead of crashing.

```xml
<Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        Style="{StaticResource Card}">
  <TextBlock Style="{StaticResource Text}" Text="{Binding Cpu, StringFormat='CPU {0:0}%'}"/>
</Border>
```

Widgets can only use XAML, not event handlers or code. To add a new kind of data or action, extend
`LiveData` in `src\WidgetHost.cs` and run `build.cmd` again.

### What you can bind to

| Name | Meaning |
| --- | --- |
| `Now` | Current time, e.g. `{Binding Now, StringFormat=HH:mm}` |
| `Cpu`, `Ram`, `Disk` | Percent used, 0 to 100 |
| `CpuPoints` | Last 60 seconds of CPU for a `Polyline` (x 0 to 236, y 0 to 40) |
| `RamUsedGb`, `RamTotalGb`, `DiskFreeGb` | Sizes in GB |
| `NetDown`, `NetUp` | Text such as `1.2 MB/s` |
| `Uptime` | Text such as `2d 4h 10m` |
| `Battery`, `HasBattery`, `IsCharging` | Battery state |
| `Notes[name]` | Text saved to `notes\name.txt` |
| `Run` | Command. `CommandParameter` is a program, file, URL or `program\|arguments` |
| `Media` | Command. `CommandParameter` is `play`, `next`, `prev`, `mute`, `volup` or `voldown` |

Bind a `ProgressBar` with `Mode=OneWay`, otherwise WPF rejects the binding.

### Network monitor

`widgets\network.xaml` shows connection health, live speed, a 2-minute traffic graph, peak and daily
totals, router and internet latency, jitter, packet loss, and the apps with the most connections.

The health line compares two pings, sent every 2 seconds, to show where a problem is: one to your router
and one to `1.1.1.1` (Cloudflare). To ping something else, exit from the tray, add
`network.pingTarget=your.host` to `state.ini`, and start the app again.

It lists connections per app rather than speed per app, because Windows only reports per-app speed
to programs running as administrator. Traffic is counted for the adapter Windows uses for internet
access. Today's totals are saved once a minute and on exit.

Your own widgets can bind to `Net.Health`, `Net.HealthDetail`, `Net.HealthBrush`, `Net.Down`, `Net.Up`,
`Net.PeakDown`, `Net.PeakUp`, `Net.SessionDown`, `Net.SessionUp`, `Net.TodayDown`, `Net.TodayUp`,
`Net.DownPoints`, `Net.UpPoints`, `Net.GraphScale`, `Net.GatewayPing`, `Net.InternetPing`, `Net.Jitter`,
`Net.Loss`, `Net.GatewayPoints`, `Net.InternetPoints`, `Net.Established`, `Net.Listening`, `Net.TopApps`,
`Net.Adapter`, `Net.AdapterType`, `Net.LinkSpeed`, `Net.LocalIp`, `Net.Gateway`, `Net.Dns`,
`Net.PingTarget` and `Net.PacketProblems`.

### Pi-hole

`widgets\pihole.xaml` shows blocking status and the last 24 hours of queries from a Pi-hole v6 server. It
only reads stats and never changes anything on the Pi-hole. Settings are in `state.ini`; exit from the
tray before editing that file, because the app overwrites it while running.

```ini
pihole.url=http://pi.hole
pihole.password=your-app-password
```

Leave out `pihole.password` if your Pi-hole has no password. If it has one, create an app password
under Settings > Web interface / API in Pi-hole and use that rather than your main password. It is stored
unencrypted in `state.ini` and sent over plain HTTP unless `pihole.url` uses HTTPS. The widget can't use
HTTPS with Pi-hole's default self-signed certificate unless Windows trusts that certificate.

Your own widgets can bind to `Pihole.Status`, `Pihole.StatusDetail`, `Pihole.StatusBrush`, `Pihole.Total`,
`Pihole.Blocked`, `Pihole.PercentBlocked`, `Pihole.PercentText`, `Pihole.UniqueDomains`,
`Pihole.CachedPercent`, `Pihole.Rate`, `Pihole.Clients`, `Pihole.BlockList`, `Pihole.BlockListAge`,
`Pihole.TopBlocked`, `Pihole.TopClients`, `Pihole.HistoryTotal`, `Pihole.HistoryBlocked` and
`Pihole.HistoryScale`.

### Mini arcade

`widgets\arcade.xaml` appears at the bottom centre of the screen, always on top. Click the game screen,
choose a game with the up and down arrows, and press Space. It has Invaders, Defender and Caterpillar.

The arrow keys or WASD move, Space fires, P or Esc pauses, and M returns to the game list. Clicking
anywhere else pauses the game and gives your keyboard back. The game loop only runs while you play,
so an idle arcade uses no CPU. High scores are saved in `state.ini`.

- **Invaders:** clear the descending aliens from behind your shields.
- **Defender:** fly around a wrapping world using the radar at the top. Landers carry humans up, and a
  human that reaches the top dies and turns its lander into a mutant that hunts you. Shoot a carrying
  lander, then catch the falling human. A long fall kills them.
- **Caterpillar:** shoot the caterpillar as it winds through the mushrooms. Each segment you hit leaves a
  mushroom and can split it in two. A spider roams near the bottom, and you earn an extra life every 10,000 points.

To change its size, edit `Width` and `Height` in the XAML; keep a 4:3 ratio, and whole multiples of 200 by
150 keep the pixels even. To stop it sitting on top, right-click it and choose a different layer. Drag the
top bar to move it.

To add a game, create a class in `src\` that derives from `ArcadeGame`, add a line for it to the `Menu` list
in `src\Arcade.cs`, and run `build.cmd`.

### Good to know

- `widgets\_theme.xaml` holds the shared colours and styles (`Card`, `Text`, `Muted`, `Display`, `Meter`,
  `IconButton`). Files that start with `_` are not shown as widgets.
- Fully transparent pixels let clicks through. To make an empty area draggable, give it the
  background `#01000000`, as `clock.xaml` does.
- Problems are written to `error.log`.

## Licence

MIT. See [LICENSE](LICENSE).
