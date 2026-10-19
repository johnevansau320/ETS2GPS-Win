# ETS2 GPS for Windows

The Windows version of [ETS2 GPS](https://github.com/johnevansau320/ETS2GPS) (the Android route-GPS for Euro Truck Simulator 2 with ProMods and other map mods). Licensed under GPL-3.0 (see LICENSE).

A native Windows (.NET 8 WPF) version of the ETS2 GPS Android app, and fully standalone: **one exe, nothing else to install or
start** (no Python, no companion). It has the road map with route, turn-by-turn guidance with voice, the lane panel, waypoints,
saved trips, POIs, hill / fuel / rest alerts, job auto-detect, the trip screen, and an option to avoid ferries.

It runs on the same PC as the game:
* telemetry comes straight from the TruckSim GPS Telemetry Server (default `127.0.0.1:31377`);
* the road map is built by the app itself from your game and active mods (the map builder is a C# port of the old Python
  companion and produces the identical file). The first start, or the first after a game / DLC / mod change, takes about
  10 seconds; the result is kept in `%APPDATA%\ETS2-GPS\etsmap.bin`. If the TruckSim server is not running the saved map is
  used as is;
* voice uses Windows speech on the default audio device (your headset);
* a small built-in web server (port 31378, same as the old companion) lets the Android tablet app, a browser / iPad
  (`http://<pc-ip>:31378/`) and the lane overlay window use this PC: map download, voice on the PC, lane panel, telemetry.
  If that port is taken (for example the old Python companion is still running) the server is simply off and a message says so.

Build: `build.ps1` (single self-contained exe in `publish\`, about 64 MB). Install: `install.ps1` (`-Autostart` also starts it at
login). The app icon is `Assets\app.ico`, made from `Assets\icon-source.jpg` by `make_icon.ps1`.

Setup program: `build-installer.ps1` builds the app and wraps it (with the lane overlay, if its build is next to the project) into `installer\Output\Ets2GpsSetup-<version>.exe` with Inno Setup 6. It installs for the current user (no administrator rights), adds a Start Menu shortcut and optional desktop / start-with-Windows shortcuts, and can be removed from Windows' Apps list (the uninstaller asks whether to delete your settings, trips and the map built from the game).

Stay on top of other apps: the **Pin on top** button (or the `P` key, or Settings) keeps the window above everything else; the
choice is remembered. `--topmost` forces it for one run. ETS2 must be windowed / borderless for any window to appear above the game.

Window: no title bar. Drag the green bar to move it (double-click maximises), drag the edges to resize; the whole UI scales
from 0.2x to 1x of a 1920x1080 layout. Minimise / maximise / close are the small buttons at the top right of the green bar.

Map look: steel-blue roads with outlines, lots / yards (tan) and green patches under the roads, road-number shields, company name tags, and symbols for fuel, rest areas, service centres, garages, truck dealers, weigh stations and ferry (B) / car-train (T) terminals. Shields appear from a moderate zoom, company names when zoomed in.

3D view: above 15 km/h the map tilts into perspective (back to flat below 12 km/h, or when you drag the map). Company tags show the game's logo, borders show a BORDER tag and the flag of the country entered, and big area (city) names are drawn. Logos and flags come from your own game files (built into `%APPDATA%\ETS2-GPS\etsassets.bin` with the map, never committed or released).

Opacity: Settings -> *Window* -> Opacity makes the window see-through (the game underneath shows). Going between 100 % and less takes effect after restarting the app (a see-through window renders a little slower, so it is only used when the saved opacity is below 100 %).

Lane overlay: the app starts the stand-alone lane overlay window (`Ets2LaneOverlay.exe`) when it starts, if it is not already running (Settings -> *Lane overlay window* turns this off, sets the path, or starts it now). It is looked for in the configured path, `%LOCALAPPDATA%\Programs\ETS2 Lane Overlay`, next to the app, and in the project's `overlay-dotnet\publish` folder; `install.ps1` installs it too when that build is next to the project.

Trip planner: **Plan** (left menu, or *Plan a trip…* in the Trips list) plans a trip without the game running and without any job: choose a start, add stops, choose the destination (city search; while previewing, right-click the map to use any spot), then **Preview on map** (route, length and time, zoomable all the way out), **Save trip** and later **Drive this trip**. A saved trip remembers its start; *Drive this trip* first leads you to the start (unless you are already near it) and then through the stops. Trips can be reopened (**View / edit the plan**), renamed and deleted from the Trips list.

Share trips: *Trips* -> *Import / export…* (and *Export this trip…* on a single trip) writes trips to a `.ets2trip` file (plain JSON, the same in the Windows app, the web version and the Android app) and reads one back in. Duplicates are skipped, a name that is already used gets a `(2)`, and invalid trips or ones outside the loaded map are skipped.

Keys: `+` / `-` zoom (up to 10x), `T` trip screen, `P` stay on top, `Esc` recentre / close trip screen, `F11` maximise.
Mouse: drag the map to look around (Recentre returns), wheel to zoom, double-click resets zoom, right-click a point for
Navigate here / Add as waypoint / Avoid road here.

Settings (the gear): route type, avoid ferries and car trains, window opacity, voice, telemetry address, built-in server on/off
and port, distance scale. `Save + rebuild map` forces a new map build.

Command line (testing): `--config file.json`, `--zoom <0.35..10>`, `--plan "Start|Stop|Destination"` (+ `--plan-save`, `--plan-dialog`, `--trips-dialog`, `--trips-export file`, `--trips-import file`), `--goto <city>`, `--trip`, `--build-map out.bin` (builds a map file and exits).

## Requirements and build

* Windows 10/11, Euro Truck Simulator 2 (Steam) and the [TruckSim GPS Telemetry Server](https://github.com/TruckSim-GPS/trucksim-gps-server) running next to it.
* To build: the .NET 8 SDK, then `.\build.ps1` (single self-contained `publish\Ets2Gps.exe`) and `.\install.ps1` (Start Menu shortcut, optional autostart), or `.uild-installer.ps1` for a setup program (needs Inno Setup 6). GitHub Actions builds the exe on every push; releases carry a ready-made exe.
* The `web` folder is the browser / tablet version served by the built-in server; it is embedded in the exe.
* Map and file formats follow the MIT-licensed [ts-map](https://github.com/dariowouters/ts-map) project.
