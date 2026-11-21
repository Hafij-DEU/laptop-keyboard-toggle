"""Device-pick logic of the app, the root helper and the watchdog on fake sysfs trees.

Run: python3 -m pytest -q tests
"""
import importlib.machinery
import importlib.util
import os
import re
import subprocess
import sys

import pytest

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src")


def load(name, sysfs, monkeypatch):
    monkeypatch.setenv("TIK_SYSFS", str(sysfs))
    loader = importlib.machinery.SourceFileLoader(name.replace("-", "_"), os.path.join(SRC, name))
    spec = importlib.util.spec_from_loader(loader.name, loader)
    mod = importlib.util.module_from_spec(spec)
    loader.exec_module(mod)
    return mod


def helper_pick(sysfs):
    """Run the helper's own find_internal() with its sysfs path pointed at the fake tree."""
    with open(os.path.join(SRC, "helper")) as f:
        text = f.read()
    func = re.search(r"^find_internal\(\) \{.*?^\}", text, re.S | re.M).group(0)
    assert "/sys/class/input" in func
    func = func.replace("/sys/class/input", str(sysfs))
    out = subprocess.run(["bash", "-c", "set -euo pipefail\n" + func + "\nfind_internal"],
                         capture_output=True, text=True)
    return out.stdout.strip() or None


def make_dev(root, n, name, phys="", inhibited="0", virtual=False):
    real = root / ("devices/virtual" if virtual else "devices/platform/i8042") / f"input{n}"
    real.mkdir(parents=True)
    (real / "name").write_text(name + "\n")
    (real / "phys").write_text(phys + "\n")
    (real / "inhibited").write_text(inhibited + "\n")
    (root / "class").mkdir(exist_ok=True)
    os.symlink(real, root / "class" / f"input{n}")


@pytest.fixture
def two_keyboards(tmp_path):
    # Lexical order puts input10 before input2; numeric order the other way round.
    make_dev(tmp_path, 2, "AT Translated Set 2 keyboard", "isa0060/serio0/input0", "0")
    make_dev(tmp_path, 10, "AT Raw Set 2 keyboard", "", "1")
    make_dev(tmp_path, 1, "Power Button", "PNP0C0C/button/input0")
    make_dev(tmp_path, 0, "AT Translated Set 2 keyboard", "isa0060/serio0/input0", virtual=True)
    return tmp_path / "class"


def test_all_three_pick_the_same_device(two_keyboards, monkeypatch):
    app = load("internal-keyboard", two_keyboards, monkeypatch)
    wd = load("watchdog", two_keyboards, monkeypatch)
    want = str(two_keyboards / "input2")
    assert app.find_internal() == want
    assert wd.find_internal() == want
    assert helper_pick(two_keyboards) == want


def test_none_found(tmp_path, monkeypatch):
    make_dev(tmp_path, 3, "USB Keyboard", "usb-0000:00:14.0-1/input0")
    sysfs = tmp_path / "class"
    assert load("internal-keyboard", sysfs, monkeypatch).find_internal() is None
    assert load("watchdog", sysfs, monkeypatch).find_internal() is None
    assert helper_pick(sysfs) is None


def test_helper_still_validates_arguments():
    out = subprocess.run(["bash", os.path.join(SRC, "helper"), "bogus"], capture_output=True, text=True)
    assert out.returncode == 2 and "usage" in out.stderr


def test_watchdog_does_not_reset_without_i8042_line(tmp_path, monkeypatch):
    """No i8042 line in /proc/interrupts -> never act on 'keyboard silent'."""
    make_dev(tmp_path, 2, "AT Translated Set 2 keyboard", "isa0060/serio0/input0")
    sysfs = tmp_path / "class"
    (tmp_path / "devices/platform/i8042/input2/event5").mkdir()
    wd = load("watchdog", sysfs, monkeypatch)
    resets = []
    clock = iter(range(0, 10_000))
    monkeypatch.setattr(wd, "kbd_irq_count", lambda: None)
    monkeypatch.setattr(wd.os, "open", lambda *a, **k: 99)
    monkeypatch.setattr(wd, "held_keys", lambda fd: {42})        # Shift held the whole time
    monkeypatch.setattr(wd, "set_inhibited", lambda d, v: resets.append(v))
    monkeypatch.setattr(wd, "notify", lambda body: None)
    monkeypatch.setattr(wd.time, "monotonic", lambda: float(next(clock)))
    calls = {"n": 0}

    def fake_sleep(_):
        calls["n"] += 1
        if calls["n"] > 50:
            raise KeyboardInterrupt
    monkeypatch.setattr(wd.time, "sleep", fake_sleep)
    with pytest.raises(KeyboardInterrupt):
        wd.run()
    assert resets == []


def test_watchdog_still_resets_stuck_shift_with_i8042_line(tmp_path, monkeypatch):
    make_dev(tmp_path, 2, "AT Translated Set 2 keyboard", "isa0060/serio0/input0")
    sysfs = tmp_path / "class"
    (tmp_path / "devices/platform/i8042/input2/event5").mkdir()
    wd = load("watchdog", sysfs, monkeypatch)
    resets = []
    clock = iter(range(0, 10_000))
    monkeypatch.setattr(wd, "kbd_irq_count", lambda: 1234)          # keyboard silent
    monkeypatch.setattr(wd.os, "open", lambda *a, **k: 99)
    monkeypatch.setattr(wd, "held_keys", lambda fd: {42})
    monkeypatch.setattr(wd, "set_inhibited", lambda d, v: resets.append(v))
    monkeypatch.setattr(wd, "notify", lambda body: None)
    monkeypatch.setattr(wd.time, "monotonic", lambda: float(next(clock)))
    calls = {"n": 0}

    def fake_sleep(_):
        calls["n"] += 1
        if calls["n"] > 50:
            raise KeyboardInterrupt
    monkeypatch.setattr(wd.time, "sleep", fake_sleep)
    with pytest.raises(KeyboardInterrupt):
        wd.run()
    assert resets[:2] == [True, False]
