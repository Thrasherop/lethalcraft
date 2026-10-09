"""#38 repro: rebinding keys in Settings > Change keybinds (InputUtils' panel), then closing the menu, could leave the
player unable to move or crouch. Drives the real menus (dev uibtn / rebinds / keys) through one scenario, then checks
which of the game's actions are off and whether the walk/crouch keys work.
usage: py tools/rebind_repro.py <scenario>   scenarios: seq, overlap, escape, conflict, timeout, reset, mainmenu"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd as c

def open_keybinds():
    if "Resume" not in c("uibtn list"): c("keys Escape 0.1"); time.sleep(1.2)
    c("uibtn click Settings"); time.sleep(0.8)
    c("uibtn click Change keybinds"); time.sleep(1.2)

def close_menu(confirm=True):
    c("uibtn click > Back"); time.sleep(0.8)
    if confirm: c("uibtn click Confirm changes"); time.sleep(0.8)
    c("uibtn click BackButton"); time.sleep(0.8)
    c("uibtn click Resume"); time.sleep(1.0)

def idx(name, gamepad=False):
    for e in c("rebinds").split(" ; "):
        m = re.match(r"(\d+):" + re.escape(name) + r"\[-?\d+\]=<" + ("Gamepad" if gamepad else "Keyboard"), e.strip())
        if m: return int(m.group(1))

def start(name): return c(f"rebinds start {idx(name)}")
def press(key, hold=0.15): c(f"keys {key} {hold}"); time.sleep(hold + 0.6)

def moved(key, sec=0.8):
    p0 = tuple(map(float, re.search(r"pos=([-\d.]+),[-\d.]+,([-\d.]+)", c("state")).groups()))
    c(f"keys {key} {sec}"); time.sleep(sec + 0.4)
    p1 = tuple(map(float, re.search(r"pos=([-\d.]+),[-\d.]+,([-\d.]+)", c("state")).groups()))
    return round(((p1[0] - p0[0]) ** 2 + (p1[1] - p0[1]) ** 2) ** 0.5, 2)

def reset_all():
    open_keybinds(); c("uibtn click Reset all to default"); time.sleep(0.8); close_menu()

sc = sys.argv[1] if len(sys.argv) > 1 else "seq"
if sc == "mainmenu":
    # rebinding from the main menu, before hosting (every other scenario rebinds in-game): quit to the menu, Settings >
    # Change keybinds there, rebind, confirm, then host a game and check walking
    if "Resume" not in c("uibtn list"): c("keys Escape 0.1"); time.sleep(1.2)
    print(c("uibtn click Quit")); time.sleep(1.0)
    print(c("uibtn click Confirm")); time.sleep(8.0)
    print("main menu buttons:", c("uibtn list")[:400])
    c("uibtn click Settings"); time.sleep(1.0)
    c("uibtn click Change keybinds"); time.sleep(1.5)
    print("before:", c("actions"))
    for name, key in [("Jump", "K"), ("Crouch", "C"), ("Walk forward", "UpArrow")]: start(name); time.sleep(0.3); press(key)
    print("in menu:", c("rebinds").split(" ; ")[:8])
    print(c("uibtn click > Back")); time.sleep(0.8)
    print(c("uibtn click Confirm changes")); time.sleep(0.8)
    print(c("uibtn click BackButton")); time.sleep(0.8)
    print("menu buttons now:", c("uibtn list")[:400])
    print(c("uibtn click Host")); time.sleep(1.5)
    print("host screen:", c("uibtn list")[:400])
    print(c("uibtn click File 3") if "File 3" in c("uibtn list") else c("uibtn click File3")); time.sleep(0.8)
    print(c("uibtn click Friends-only")); time.sleep(0.5)  # (never a public lobby)
    print(c("uibtn click Confirm")); time.sleep(20.0)
    a = c("actions")
    print("after hosting:", a)
    print(f"walk (UpArrow) moved {moved('UpArrow')} m; W moved {moved('W')} m; strafe (D) moved {moved('D')} m")
    print("RESULT:", "BROKEN" if " 0 off" not in a else "ok")
    reset_all()
    sys.exit()
c("tpship"); time.sleep(1)
reset_all()
print("before:", c("actions"))
open_keybinds()
fwd = "W"
if sc == "seq":
    for name, key in [("Jump", "K"), ("Crouch", "C"), ("Walk forward", "UpArrow")]: start(name); time.sleep(0.3); press(key)
    fwd = "UpArrow"
elif sc == "overlap":
    # a second rebind started before the first got its key (clicking down the list quickly)
    start("Jump"); time.sleep(0.3); start("Crouch"); time.sleep(0.3); press("K"); press("C")
    start("Walk forward"); time.sleep(0.2); start("Walk back"); time.sleep(0.2); press("UpArrow"); press("DownArrow")
elif sc == "escape":
    start("Jump"); time.sleep(0.3); press("Escape")
    start("Walk forward"); time.sleep(0.3); press("Escape")
elif sc == "conflict":
    # a key already used by another action
    start("Crouch"); time.sleep(0.3); press("W")
elif sc == "timeout":
    start("Jump"); time.sleep(0.3)
    print("waiting for the rebind to time out..."); time.sleep(12)
elif sc == "closewhile":
    start("Jump"); time.sleep(0.3)  # menu closed while waiting for a key
print("in menu:", c("rebinds").split(" ; ")[54:68])
close_menu()
a = c("actions")
print("after:", a)
print(f"walk ({fwd}) moved {moved(fwd)} m; strafe (D) moved {moved('D')} m")
print("RESULT:", "BROKEN" if " 0 off" not in a else "ok")
