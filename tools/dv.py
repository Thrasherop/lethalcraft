import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from dev import cmd
port = int(sys.argv[1])
for c in sys.argv[2:]:
    print(cmd(c, port))
