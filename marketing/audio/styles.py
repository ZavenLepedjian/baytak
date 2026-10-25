"""One musical identity per video, so no two sound alike: a different key, chords, instrument, rhythm and hook for each.

Fields (see synth.music):
  key/scale/prog      tonal centre; prog = (semitones above the key, chord quality) per bar, repeating
  final/pre           the last chord (where the video ends) and the chord just before it
  tempo               preferred BPM (the logo's bar line decides what is possible)
  timbre/arp/arp_gain the arpeggio instrument and its pattern (indexes into the chord; None is a rest)
  bass                (eighth-note step, which note, length in eighths)
  kick/clap/brush     eighth-note steps; hat: sixteenth-note steps (shaker=True plays a shaker instead of a hat)
  melody/lead         a hook per bar of the progression (eighth step, scale degree, length), played by `lead`
"""
BASE = dict(scale="major", pad=0.085, pad_lp=2200, wet=0.24, kick=[0, 4], clap=[], hat=[], brush=[], shaker=False, kick_gain=1.0,
            hat_gain=0.07, bass_gain=0.34, arp_gain=0.15, lead="bell", lead_gain=0.12, melody=None, intro=None, pingpong=False,
            bass=[(0, "root", 4), (4, "root", 3)])


def style(**kw):
    d = dict(BASE)
    d.update(kw)
    return d


STYLES = {
    # the launch ad: bright and driving, with a singing bell hook
    "launch": style(key="D", tempo=104, prog=[(0, "maj"), (7, "maj"), (9, "min"), (5, "maj")], final=(0, "maj"), pre=(7, "maj"),
                    timbre="pluck", arp=[0, 1, 2, 3, 2, 1, 2, 3], kick=[0, 2, 4, 6], clap=[2, 6], hat=[2, 6, 10, 14], hat_gain=0.08,
                    bass=[(i, "root", 1) for i in range(7)] + [(7, "oct", 1)], bass_gain=0.28,
                    melody=[[(0, 4, 2), (2, 5, 2), (4, 7, 4)], [(0, 6, 2), (2, 5, 2), (4, 4, 4)],
                            [(0, 5, 2), (2, 4, 2), (4, 2, 4)], [(0, 4, 2), (2, 2, 2), (4, 0, 4)]]),
    # the group chat: minor, nervous staccato plucks and ticking, then it resolves into a bright major chord
    "chat": style(key="A", scale="minor", tempo=113, prog=[(0, "min"), (8, "maj"), (3, "maj"), (10, "maj")], final=(0, "maj"), pre=(7, "maj"),
                  timbre="pizz", arp=[0, None, 2, None, 1, None, 3, None], arp_gain=0.2, kick=[0, 3], hat=[0, 2, 4, 6, 8, 10, 12, 14], hat_gain=0.05,
                  bass=[(0, "root", 2), (3, "root", 1), (5, "fifth", 1), (6, "root", 2)], intro="ticks", pad=0.06, wet=0.2),
    # issuing a receipt: folky G major with a marimba and handclaps
    "issue": style(key="G", tempo=101, prog=[(0, "maj"), (5, "maj"), (0, "maj"), (7, "maj")], final=(0, "maj"), pre=(7, "maj"),
                   timbre="marimba", arp=[0, 2, 1, 2, 0, 2, 3, 2], arp_gain=0.2, kick=[0, 4], clap=[2, 6],
                   bass=[(0, "root", 1), (2, "oct", 1), (3, "root", 1), (4, "fifth", 1), (6, "oct", 1)], lead="marimba", lead_gain=0.16,
                   melody=[[(0, 2, 1), (1, 4, 1), (2, 5, 2), (4, 4, 4)], [(0, 2, 1), (1, 4, 1), (2, 5, 2), (4, 7, 4)],
                           [(0, 4, 1), (1, 2, 1), (2, 4, 2), (4, 2, 4)], [(0, 1, 1), (1, 2, 1), (2, 4, 2), (4, 0, 4)]]),
    # collecting: playful, bouncy F major with a shaker and syncopated kicks
    "collect": style(key="F", tempo=113, prog=[(0, "maj"), (5, "maj"), (7, "maj"), (0, "maj")], final=(0, "maj"), pre=(7, "maj"),
                     timbre="marimba", arp=[0, None, 1, 0, None, 2, 1, None], arp_gain=0.22, kick=[0, 3, 4, 7], hat=list(range(16)), shaker=True,
                     hat_gain=0.05, bass=[(0, "root", 1), (2, "oct", 1), (3, "root", 1), (4, "fifth", 1), (6, "oct", 1), (7, "root", 1)],
                     bass_gain=0.3, pad=0.06),
    # "do I owe anything": calm and reassuring, soft electric piano, brushes and no kick drum
    "owe": style(key="Eb", tempo=85, prog=[(0, "maj7"), (9, "min7"), (5, "maj7"), (7, "maj")], final=(0, "maj7"), pre=(7, "maj"),
                 timbre="epiano", arp=[0, None, 1, None, 2, None, 1, None], arp_gain=0.2, kick=[], brush=[2, 6], hat=[0, 4, 8, 12], hat_gain=0.03,
                 bass=[(0, "root", 8)], bass_gain=0.3, pad=0.12, pad_lp=1800, wet=0.32, lead="epiano", lead_gain=0.13,
                 melody=[[(0, 4, 6)], [(0, 2, 6)], [(0, 5, 6)], [(0, 4, 6)]]),
    # locked receipts: serious and solid, a low heartbeat, a dark pad and no hats
    "locked": style(key="B", scale="minor", tempo=96, prog=[(0, "min"), (0, "min"), (8, "maj"), (10, "maj")], final=(0, "maj"), pre=(10, "maj"),
                    timbre="pizz", arp=[0, None, None, None, 0, None, 1, None], arp_gain=0.2, kick=[0, 4], kick_gain=1.5,
                    bass=[(0, "root", 8)], bass_gain=0.4, intro="heart", pad=0.1, pad_lp=1200, wet=0.18),
    # two currencies: ping-pong bells left and right, a flat-seven mixolydian sound in B flat
    "currency": style(key="Gb", tempo=96, prog=[(0, "maj"), (10, "maj"), (5, "maj"), (0, "maj")], final=(0, "maj"), pre=(5, "maj"),
                      timbre="bell", arp=[0, 3, 1, 2, 0, 3, 1, 2], arp_gain=0.085, pingpong=True, kick=[0, 4], hat=[2, 6, 10, 14], hat_gain=0.06,
                      bass=[(0, "root", 2), (2, "oct", 2), (4, "root", 2), (6, "oct", 2)], lead="bell", lead_gain=0.11,
                      melody=[[(0, 0, 2), (4, 4, 2)], [(0, 4, 2), (4, 7, 2)], [(0, 7, 2), (4, 4, 2)], [(0, 4, 2), (4, 0, 2)]]),
    # signing in: light and modern, quick high harp arpeggios in A major
    "login": style(key="E", tempo=113, prog=[(0, "maj"), (9, "min"), (5, "maj"), (7, "maj")], final=(0, "maj"), pre=(7, "maj"),
                   timbre="harp", arp=[0, 1, 2, 3, 2, 1, 2, 3, 0, 1, 2, 3, 2, 1, 2, 3], arp_gain=0.1, kick=[0, 4], kick_gain=0.7,
                   hat=list(range(16)), shaker=True, hat_gain=0.05, bass=[(0, "root", 3), (4, "root", 3)], bass_gain=0.26,
                   lead="bell", lead_gain=0.11, pad=0.07,
                   melody=[[(0, 4, 2), (3, 5, 1), (4, 7, 4)], [(0, 6, 2), (3, 5, 1), (4, 4, 4)],
                           [(0, 5, 2), (3, 4, 1), (4, 2, 4)], [(0, 4, 2), (3, 2, 1), (4, 0, 4)]]),
    # the long Play demo: warm and cinematic, a big pad with electric piano and a soft beat
    "demo": style(key="C", tempo=85, prog=[(0, "maj"), (9, "min"), (5, "maj"), (7, "maj")], final=(0, "maj"), pre=(7, "maj"),
                  timbre="epiano", arp=[0, 1, 2, 1, 3, 2, 1, 2], arp_gain=0.13, kick=[0, 4], kick_gain=0.8, hat=[2, 6, 10, 14], hat_gain=0.05,
                  bass=[(0, "root", 4), (4, "root", 3)], pad=0.11, wet=0.3, lead="bell", lead_gain=0.12,
                  melody=[[(0, 4, 4), (4, 5, 2), (6, 4, 2)], [(0, 2, 4), (4, 4, 2), (6, 2, 2)],
                          [(0, 5, 4), (4, 7, 2), (6, 5, 2)], [(0, 4, 4), (4, 2, 4)]]),
}

# which style each video uses
FOR_VIDEO = {
    "baytak-launch-ad": "launch", "r1-messages": "chat", "r2-issue": "issue", "r3-collect": "collect", "r4-owe": "owe",
    "r5-locked": "locked", "r6-currency": "currency", "r7-login": "login", "baytak-play-demo": "demo",
}
