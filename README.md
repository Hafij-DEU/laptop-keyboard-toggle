# Laptop Keyboard Toggle

Turn your laptop's built-in keyboard on or off with one click. It can also fix a stuck Shift key automatically.

Use it when an external keyboard sits on top of the laptop, while you clean the keys, or when the built-in keyboard has a faulty key.

Built with GTK4 and libadwaita for GNOME on Ubuntu/Debian. Once the package is installed, no password is needed.

## Features

- **One-click on/off** for the built-in keyboard. External USB and Bluetooth keyboards keep working.
- **Turn back on automatically** after 1, 5 or 15 minutes, or 1 hour. This still works after you close the window.
- **Safety check:** if no other keyboard is connected, the app asks before turning the built-in one off, because then only the mouse or touchpad can turn it back on.
- **Stuck Shift fix:** a small background service watches for a Shift key that stays down after you let go. It resets the keyboard to release it. If Shift sticks again within a minute, the service turns the keyboard off and notifies you.
- **Open at login:** the app can start when you log in, either as a normal window or minimized.
- **Quick toggle:** right-click the app icon and choose **Toggle Now** to switch without opening a window.
- **Command line:** `internal-keyboard on|off|toggle|status`.
- **Survives reboots and kernel updates:** the app finds the keyboard by its hardware port (`isa0060/serio0`), not by a fixed `eventN` number, which can change.

## Install

Download the latest `.deb` from [Releases](https://github.com/Hafij-DEU/laptop-keyboard-toggle/releases), then run:

```bash
sudo apt install ./toggle-internal-keyboard_*_all.deb
```

Open **Toggle Internal Keyboard** from the app grid.

The installer enables the stuck-Shift service. To turn it off, use the switch in the app or run `internal-keyboard watchdog off`.

### Requirements

- Linux with kernel 5.11 or newer, for the input `inhibited` attribute
- systemd and polkit (`pkexec`)
- Python 3, PyGObject, GTK 4 and libadwaita 1.5 or newer
- A built-in keyboard on the i8042 (PS/2) controller, which most laptops use. On Linux it shows up as "AT Translated Set 2 keyboard".

## Command line

```text
internal-keyboard [--minimized]            open the window
internal-keyboard status                   print "on" or "off"
internal-keyboard on|off|toggle [--notify] change the keyboard state
internal-keyboard watchdog on|off|status   stuck-Shift auto-fix
```

## How it works

| Part | Path | Role |
|---|---|---|
| App and CLI | `/usr/bin/internal-keyboard` | GTK4 window and command line. Runs as you. |
| Helper | `/usr/libexec/toggle-internal-keyboard/helper` | Writes `1` or `0` to `/sys/class/input/inputN/inhibited`. Runs as root through `pkexec`. |
| Polkit policy | `/usr/share/polkit-1/actions/io.github.hafij_bge.ToggleInternalKeyboard.policy` | Lets the active local user run the helper without a password. |
| Watchdog | `/usr/libexec/toggle-internal-keyboard/watchdog` + `toggle-internal-keyboard-watchdog.service` | Detects a stuck Shift and resets the keyboard. |

Inhibiting an input device makes the kernel ignore it and release any keys it thinks are still held down. That is why turning the keyboard off and on again releases a stuck Shift.

### Stuck-Shift detection

Some keyboards lose the "Shift released" signal, so the system keeps acting as if Shift is held: it types capitals, Shift-clicks, and rejects passwords. Twice a second, the watchdog checks two things:

1. Does the kernel still report Left or Right Shift as held? It asks with the `EVIOCGKEY` ioctl.
2. Has the keyboard been completely silent? It checks the i8042 interrupt count in `/proc/interrupts`.

If both stay true for 4 seconds, Shift is treated as stuck and the keyboard is reset. While you are typing, the keyboard sends interrupts, so Shift held during normal typing never triggers a reset.

The watchdog only reads key state; it does not read what you type. Check its log with:

```bash
journalctl -u toggle-internal-keyboard-watchdog
```

## Build from source

```bash
git clone https://github.com/Hafij-DEU/laptop-keyboard-toggle.git
cd laptop-keyboard-toggle
./build-deb.sh
```

The package is written to `dist/toggle-internal-keyboard_<version>_all.deb`. It needs only `dpkg-deb`; there is nothing to compile.

## Uninstall

```bash
sudo apt remove toggle-internal-keyboard
```

Removing the package stops and disables the watchdog service. Any keyboard you turned off comes back after a reboot.

## Limitations

- **Linux only.** The app depends on Linux sysfs and polkit, so it does not work on Windows.
- **Only i8042 keyboards are found.** Some newer laptops connect the keyboard over I²C or USB instead, and those are not detected yet.
- **Holding Shift alone gets released.** If you hold Shift for more than 4 seconds without pressing any other key, for example Shift-clicking with the mouse, the watchdog releases it.

## License

GPL-3.0-or-later. See [LICENSE](LICENSE).
