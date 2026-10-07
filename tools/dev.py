import socket, sys
def cmd(c, port=28771):
    s = socket.create_connection(("127.0.0.1", port), timeout=20)
    s.sendall((c + "\n").encode())
    data = b""
    while not data.endswith(b"\n"):
        chunk = s.recv(65536)
        if not chunk: break
        data += chunk
    s.close()
    return data.decode().strip()
if __name__ == "__main__":
    for c in sys.argv[1:]:
        print(cmd(c))
