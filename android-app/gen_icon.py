"""生成安卓启动图标（纯 Python 无依赖）：蓝底 + 键盘键位图形"""
import struct, zlib, os

def png(path, w, h, pixel):  # pixel(x,y) -> (r,g,b,a)
    raw = b''.join(b'\x00' + b''.join(bytes(pixel(x, y)) for x in range(w)) for y in range(h))
    def chunk(tag, data):
        c = tag + data
        return struct.pack('>I', len(data)) + c + struct.pack('>I', zlib.crc32(c))
    ihdr = struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0)
    with open(path, 'wb') as f:
        f.write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', ihdr) +
                chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))

BG = (40, 108, 255, 255)   # 蓝
FG = (255, 255, 255, 255)  # 白

def draw(size):
    r = size * 0.22  # 圆角半径
    def in_bg(x, y):
        s = size - 1
        # 圆角判定
        if r <= x <= s - r or r <= y <= s - r:
            return 0 <= x < size and 0 <= y < size
        for cx, cy in ((r, r), (s - r, r), (r, s - r), (s - r, s - r)):
            if (x - cx) ** 2 + (y - cy) ** 2 <= r * r:
                return True
        return False
    # 键盘：3 行键
    rows = []
    top, bottom = size * 0.30, size * 0.74
    gap = size * 0.045
    kh = (bottom - top - 2 * gap) / 3
    cols = [10, 9, 7]
    for i, n in enumerate(cols):
        y0 = top + i * (kh + gap)
        kw = (size * 0.66 - (n - 1) * gap) / n
        x0 = (size - (n * kw + (n - 1) * gap)) / 2
        rows.append((y0, y0 + kh, [(x0 + j * (kw + gap), x0 + j * (kw + gap) + kw) for j in range(n)]))
    def pixel(x, y):
        if not in_bg(x, y):
            return (0, 0, 0, 0)
        for y0, y1, keys in rows:
            if y0 <= y <= y1:
                for x0, x1 in keys:
                    if x0 <= x <= x1:
                        return FG
        return BG
    return pixel

base = os.path.dirname(os.path.abspath(__file__))
for dpi, size in (('mdpi', 48), ('hdpi', 72), ('xhdpi', 96), ('xxhdpi', 144), ('xxxhdpi', 192)):
    d = os.path.join(base, f'app/src/main/res/mipmap-{dpi}')
    os.makedirs(d, exist_ok=True)
    png(os.path.join(d, 'ic_launcher.png'), size, size, draw(size))
    print(dpi, size, 'ok')
