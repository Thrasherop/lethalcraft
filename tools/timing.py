"""The test harness's clock: where a run's wall time goes, and running the game faster.

install(speed): wraps time.sleep / time.time and dev.cmd for the whole process (call it before anything takes
dev.cmd). With speed > 1 the game runs that many times faster ("speed" dev command) and the tests' sleeps and
time.time() are in GAME seconds: a sleep(2) waits until the game's clock has moved 2 s (about 2/speed s of wall time,
longer if the game can't keep up), so every wait, timeout and timing check in the tests keeps its meaning.
report(): totals printed at the end of a run."""
import time, collections, atexit
import dev

_real_sleep, _real_time, _real_cmd = time.sleep, time.time, dev.cmd
SPEED = 1.0
slept = 0.0
cmd_n = 0
cmd_t = 0.0
by_cmd = collections.Counter()
by_cmd_t = collections.Counter()
sections = []  # (label, seconds, slept, cmds)

def _cmd(c, port=28771):
    global cmd_n, cmd_t
    t0 = time.perf_counter()
    try: return _real_cmd(c, port)
    finally:
        dt = time.perf_counter() - t0
        cmd_n += 1; cmd_t += dt
        k = c.split(" ", 1)[0]
        by_cmd[k] += 1; by_cmd_t[k] += dt

_g_last, _w_last = 0.0, -1e9  # the game clock at the last reading, and the wall clock then

def game_now():
    """the game's clock (Time.time), read from the game"""
    global _g_last, _w_last
    for _ in range(3):
        try:
            w = time.perf_counter()
            _g_last, _w_last = float(_cmd("gtime").split()[0]), w
            return _g_last
        except (ValueError, IndexError, OSError): _real_sleep(0.5)  # (busy loading a level)
    return _game_estimate()

def _game_estimate():
    """the game clock without asking: never behind the real one (the game can run slower than SPEED, never faster),
    so a sleep that ends by this estimate checks the real clock before it trusts it"""
    return _g_last + (time.perf_counter() - _w_last) * SPEED

requested = 0.0  # game seconds the tests asked to sleep

def _sleep(t):
    global slept, requested
    t0 = time.perf_counter()
    if t <= 0: return
    requested += t
    if SPEED <= 1.0:
        _real_sleep(t)
    else:
        # the game answers "wait" once its own clock has moved t seconds: one round trip, exact in game time
        try: _cmd(f"wait {t:.4f}")
        except OSError: _real_sleep(t / SPEED)
    slept += time.perf_counter() - t0

def _time():
    return game_now() if SPEED > 1.0 else _real_time()

_installed = False

def install(speed=None):
    """(again from another module importing the harness: no change unless a speed is given)"""
    global _installed
    if not _installed:
        dev.cmd = _cmd
        time.sleep = _sleep
        time.time = _time
        _installed = True
    if speed is not None: set_speed(speed)

def set_speed(speed):
    global SPEED
    SPEED = float(speed)
    try: print("  game speed:", _real_cmd(f"speed {SPEED}"), flush=True)
    except OSError: pass
    if SPEED != 1.0: atexit.register(_back_to_normal)

def _back_to_normal():
    """the run is over (or crashed): the game back at its normal speed and frame cap"""
    try: _real_cmd("speed 1")
    except OSError: pass

class section:
    def __init__(self, label): self.label = label
    def __enter__(self):
        self.t0, self.s0, self.c0 = time.perf_counter(), slept, cmd_n
        return self
    def __exit__(self, *a):
        sections.append((self.label, time.perf_counter() - self.t0, slept - self.s0, cmd_n - self.c0))

def report(total):
    print(f"\n== timing (speed {SPEED:g}): {total:.0f} s wall; sleeping {slept:.0f} s ({slept / max(total, 1e-9):.0%}) for {requested:.0f} s of game time; "
          f"{cmd_n} game commands taking {cmd_t:.0f} s ({cmd_t / max(total, 1e-9):.0%}, {1000 * cmd_t / max(cmd_n, 1):.0f} ms each)")
    for label, t, s, c in sorted(sections, key=lambda x: -x[1])[:40]:
        print(f"   {t:6.1f} s  {label:34s} slept {s:5.1f} s, {c:4d} commands")
    print("   slowest commands:", ", ".join(f"{k} {by_cmd_t[k]:.0f}s/{by_cmd[k]}" for k, _ in by_cmd_t.most_common(12)))
