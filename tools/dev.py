import socket, sys

# one connection per game instance (port), kept open: a new connection per command costs the game a thread-pool
# hand-off each time. Reconnects (and resends) if the game restarted or dropped it.
_conns = {}

def _recv_line(s):
    data = b""
    while not data.endswith(b"\n"):
        chunk = s.recv(65536)
        if not chunk: raise ConnectionError("closed")
        data += chunk
    return data

def cmd(c, port=28771):
    for attempt in range(2):
        s = _conns.get(port)
        try:
            if s is None:
                s = socket.create_connection(("127.0.0.1", port), timeout=20)
                s.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                _conns[port] = s
            s.settimeout(130 if c.startswith("wait ") else 20)  # (a game-time wait can take a while when the game lags)
            s.sendall((c + "\n").encode())
            return _recv_line(s).decode().strip()
        except (ConnectionError, OSError):
            _conns.pop(port, None)
            try: s.close()
            except Exception: pass
            if attempt == 1: raise
    return ""

def cmds(cs, port=28771):
    """several commands in one game frame ("batch"): their replies, in order"""
    cs = list(cs)
    if not cs: return []
    out = []
    for i in range(0, len(cs), 200):  # (a few hundred per line at most)
        part = cs[i:i + 200]
        out += globals()["cmd"]("batch " + " ;; ".join(part), port).split(" <<>> ")  # (the harness may wrap cmd)
    return out

if __name__ == "__main__":
    for c in sys.argv[1:]:
        print(cmd(c))
