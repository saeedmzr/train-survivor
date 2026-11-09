import io

p = r"C:/Users/saeed/Apps/img3d/attach_clips.py"
s = io.open(p, encoding="utf-8").read()

old_start = s.index("walk = bpy.data.actions.get(\"Walk\")")
old_end = s.index("for label, act in imported:")
new = '''walk = bpy.data.actions.get("Walk")
stride = None
if walk:
    assign(walk)
    f0, f1 = (int(v) for v in walk.frame_range)
    frames = list(range(f0, min(f1, f0 + 240)))
    zs = np.array([toe_z(f) for f in frames], dtype=np.float64)
    # smooth, then autocorrelate: the left toe rises and falls once per stride
    sm = np.convolve(zs, np.ones(3) / 3.0, mode="same")
    d = sm - sm.mean()
    best_lag, best_corr = None, -2.0
    for lag in range(12, 61):
        a, b = d[:-lag], d[lag:]
        if len(a) < 30:
            break
        c = float(np.corrcoef(a, b)[0, 1])
        if c > best_corr:
            best_corr, best_lag = c, lag
    stride = best_lag
    print("[ap] walk autocorrelation: stride %s frames (%s s, corr %.2f)"
          % (stride, round(stride / 30.0, 2), best_corr))

'''
s = s[:old_start] + new + s[old_end:]

# strip spans: frame math was off by one
s = s.replace('    span = (stride + 1) if (label == "Walk" and stride) else (f1 - f0 + 1)',
              '    span = stride if (label == "Walk" and stride) else (f1 - f0)')
s = s.replace('    strip.action_frame_end = f0 + span', '    strip.action_frame_end = f0 + span')
s = s.replace('    strip.frame_end = 1 + span', '    strip.frame_end = 1 + span')
s = s.replace('scene.frame_start, scene.frame_end = 1, (stride + 1) if stride else 40',
              'scene.frame_start, scene.frame_end = 1, stride if stride else 40')
io.open(p, "w", encoding="utf-8").write(s)
print("patched stride detection to autocorrelation")
