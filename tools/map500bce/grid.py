"""World grid size for the 500 BCE map. 2:1 equirectangular. Default 16384x8192; set MAP_W=32768 to try the 32k test grid.

Owner decision (this session): 16k is the working size; 32k is kept only as a test concept. Earlier decision was 8192x4096.
"""
import os

W = int(os.environ.get("MAP_W", "16384"))
H = W // 2
assert W % 4 == 0 and W <= 65536
