"""Find code swallowed by an end-of-line comment: `a, // note b,` turns `b` into comment text and the compiler says
nothing (1.57.0 lost Silk Touch that way; 1.58.4 found the enchanted book's and the stews' icons and six store prices).
Prints each line whose comment, after its leading "(...)" note, still looks like code.

usage:  py tools/scan_comments.py [files...]      (default: every src/**/*.cs)"""
import re, sys, glob, os

CODE = [r'\["[^"]+"\]\s*=',                      # a dictionary entry: ["key"] = value
        r'\b[A-Za-z_]+\([^)]*\);\s*$',            # a call ending its statement
        r'\)\s*,\s*$',                            # a tuple or argument list going on
        r'=\s*new\b', r'\bvar\s+\w+\s*=',
        r'^\s*(if|for|foreach|return)\b.*[;{]\s*$',
        r'"\s*,\s*"[^"]*"\s*,']                   # a list of strings going on

def comment_of(line):
    """the text after the line's first // outside a string (and not in a URL), or None; None too for a whole-line comment"""
    ins = False
    for j, ch in enumerate(line):
        if ch == '"' and (j == 0 or line[j - 1] != '\\'): ins = not ins
        if not ins and line[j:j + 2] == '//' and (j == 0 or line[j - 1] != ':'):
            return line[j + 2:].rstrip('\n') if line[:j].strip() else None
    return None

def scan(path):
    found = []
    for i, line in enumerate(open(path, encoding='utf-8', errors='replace'), 1):
        c = comment_of(line)
        if c is None: continue
        m = re.match(r'\s*\((?:[^()]|\([^()]*\))*\)\s*(.*)$', c)
        rest = m.group(1) if m else c
        if rest and any(re.search(p, rest) for p in CODE): found.append((path, i, c.strip()))
    return found

if __name__ == '__main__':
    root = os.path.join(os.path.dirname(__file__), '..')
    files = sys.argv[1:] or glob.glob(os.path.join(root, 'src', '**', '*.cs'), recursive=True)
    hits = [h for f in files for h in scan(f)]
    for path, i, c in hits: print(f"{path}:{i}: //{c[:170]}")
    print(f"{len(hits)} line(s) with code after a comment")
    sys.exit(len(hits))
