import io
import re

p = r"C:/Users/saeed/Apps/img3d/apply_final_clips.py"
s = io.open(p, encoding="utf-8").read()

# 1) a walk cycle is two steps: autocorrelation finds the half-cycle, so double it
s = s.replace(
    '    best_p, best_err = None, 1e9\n'
    '    for p in range(18, 61):',
    '    best_p, best_err = None, 1e9\n'
    '    for p in range(9, 61):')
s = s.replace(
    '    print("[ap] walk stride period %s frames (err %.5f)" % (best_p, best_err))',
    '    print("[ap] walk step period %s frames (err %.5f)" % (best_p, best_err))\n'
    '    if best_p:\n'
    '        # a full stride is two steps: the half-cycle lands on the mirrored pose\n'
    '        best_p = best_p * 2\n'
    '        print("[ap] using full stride of %d frames (%.2fs)" % (best_p, best_p / 30.0))')

# 2) rebuilding curve keys in place leaves stale references ("Keyframe not in F-Curve"):
#    read the window out, clear the curve, then insert fresh keys
old_start = s.index('    if best_p:\n        # cut a clean loop')
old_end = s.index('# ---------------------------------------------------------------- freeze horizontal drift')
new_block = '''    if best_p:
        for fc in list(walk.fcurves):
            window = [(kp.co[0] - f0 + 1, kp.co[1],
                       kp.handle_left[0] - f0 + 1, kp.handle_left[1],
                       kp.handle_right[0] - f0 + 1, kp.handle_right[1])
                      for kp in fc.keyframe_points
                      if f0 - 0.001 <= kp.co[0] <= f0 + best_p + 0.001]
            for kp in list(fc.keyframe_points):
                fc.keyframe_points.remove(kp, fast=True)
            fc.update()
            for (fr, val, hl0, hl1, hr0, hr1) in window:
                kp = fc.keyframe_points.insert(fr, val, options={"FAST"})
                kp.handle_left = (hl0, hl1)
                kp.handle_right = (hr0, hr1)
            fc.update()
        walk.use_frame_range = True
        walk.frame_start, walk.frame_end = 1, best_p + 1
        print("[ap] walk trimmed to %d frames (%.2fs loop)" % (best_p + 1, best_p / 30.0))

'''
s = s[:old_start] + new_block + s[old_end:]
io.open(p, "w", encoding="utf-8").write(s)
print("patched: full-stride period + safe curve rebuild")
