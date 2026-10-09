"""Two-instance check of the chat commands (#45) and keepInventory, plus the client-side fixes of the 1.4.9 batch.
Run tools/mp.sh first (host on 28771, client on 28772; LAN names "Player #0" / "Player #1").
usage: py tools/mp_commands.py"""
import sys, os, time, re
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd

H, C = 28771, 28772
ok = []

def check(name, cond, detail=""):
    ok.append(bool(cond))
    print(f"  [{'PASS' if cond else 'FAIL'}] {name}  ({str(detail)[:200]})")

def chat(port, text):
    """type a line in that instance's chat box and send it (real keys: / to open, Enter to send)"""
    cmd("keys Slash 0.08", port); time.sleep(0.5)
    cmd("chattext " + text.replace(" ", "_"), port); time.sleep(0.2)
    cmd("keys Enter 0.08", port); time.sleep(1.5)
    return cmd("chattext", port).split("chat='")[1]

def pos(port): return tuple(map(float, re.search(r"pos=([-\d.]+),([-\d.]+),([-\d.]+)", cmd("state", port)).groups()))

print("- a player who isn't an operator can't use commands")
said = chat(C, "give #1 cobblestone 3")
check("refused (needs /op)", "operator" in said[-200:], said[-160:])

print("- the host makes them an operator; then they can")
said = chat(H, "op #1")
check("the host's /op is announced", "Made Player #1 a server operator" in said, said[-160:])
cmd("clearinv", C); time.sleep(1)
said = chat(C, "give #1 cobblestone 3"); time.sleep(2)
check("the operator's /give works", "Cobblestonex3" in cmd("state", C), cmd("state", C)[60:150])

print("- land; the host goes outside; the client teleports to the host")
def land():
    cmd("route 0", H)
    # (in multiplayer the ship takes a while to get there: "land" before that does nothing; keep asking)
    for _ in range(40):
        time.sleep(3)
        if "landed=True" in cmd("leave_check", H): return True
        if "Experimentation" in cmd("state", H): cmd("land", H)
    return False
check("the ship lands", land(), cmd("leave_check", H))
time.sleep(8)
for port in (H, C): cmd("dayfreeze 1", port); cmd("gamemode survival", port)
cmd("tp 0 0 -40", H); time.sleep(2)
hp = pos(H)
said = chat(C, "tp #0"); time.sleep(2)
cp = pos(C)
d = ((hp[0] - cp[0]) ** 2 + (hp[2] - cp[2]) ** 2) ** 0.5
check("/tp #0 brings the client to the host", d < 2.0, f"host {hp} client {cp} ({d:.1f} m) | {said[-120:]}")
check("and the client counts as off the ship", "inElevator=False" in cmd("ship", C), cmd("ship", C)[-110:])

print("- keepInventory: the client dies, the round ends, the client gets its things back")
said = chat(H, "keepinventory true")
check("the rule is announced", "keepInventory is now set to: true" in said, said[-120:])
cmd("god 0", C); time.sleep(0.5)
print("   client before:", cmd("state", C)[60:140])
print("   hurt:", cmd("hurt 300", C)); time.sleep(3)
check("the client died", "dead=True" in cmd("state", C), cmd("state", C)[:60])
cmd("leave", H)
for _ in range(80):
    time.sleep(3)
    s = cmd("state", C)
    if "dead=False" in s and "inShipPhase=True" in s: break
time.sleep(6)
st = cmd("state", C)
check("revived with the cobblestone back in the hotbar", "dead=False" in st and "Cobblestonex3" in st, st[:150])
cmd("god 1", C)

print("- in orbit, nothing of the moon is left on the client (#36)")
r = cmd("rootcheck", C)
check("no moon blocks on the client in orbit", "frame0=0" in r, r)

print(f"\n{sum(ok)}/{len(ok)} checks passed")
