# Limits, and which systems it works on

## Which systems

* **Mac:** made and tried here (macOS, OpenGL), with TUFX, Natural Light, Scatterer, EVE and Waterfall
  installed.
* **Windows:** tried on Windows 11 (KSP 1.12.5, Direct3D 11, an NVIDIA GeForce RTX 4080 SUPER) on
  2026-10-10, in the assembly building and in flight on the launch pad: the white studio, the green
  screen and the showroom; pictures twice the screen's size saved by the page's button and by Alt-S; the keys
  pressed in the game's window (Alt-P to open and close, Alt-1 and Alt-2, Alt-S, Escape, the arrow keys to
  turn the camera); no glow with the pointer on the craft, on the screen or in the pictures; no input lock
  left after closing, and the editor's own glow back. With the game started on OpenGL (`-force-glcore`) the
  showroom drew, and Keystone's lens loaded for depth of field. Found there and mended (true on a Mac too):
  the glow the game lays into the parts' materials under the pointer showed in the studio and its pictures;
  Alt-Return never reaches a mod on Windows (Alt-S does it now, everywhere); in flight none of the studio's
  keys worked, the game's pause, which the studio sets, holding them all off. Later that day, in the hangar
  with the Aeris 4A: the turntable on and off with Alt-T, and a see-through picture four times the screen's size
  (5120 by 2880, 91% of it clear, the soft shadow under the plane kept) taken with Alt-S.
* **Linux:** everything in the mod is drawn with the game's own shaders (`KSP/Unlit` and the game's
  see-through particle shader) and lamps, from shapes the mod works out itself, with nothing written for one
  system alone. **It has not been run there.**

## What was tried

In the assembly building with the stock Kerbal X and Mun Lander I, and in the hangar with a small plane; in
flight with a rocket on the launch pad, a craft in orbit and a lander on Laythe (with Scatterer's air and EVE's
clouds). Each studio, pictures at one, two and four times the screen's size, see-through pictures; the editor
and the flight compared before opening and after closing (cameras, lamps, layers, the light from all round,
the game's shadow settings, input locks, what is hidden, how many objects there are: the same, but for what
the game moves by itself). The studio left open while: changing studio, loading another craft, leaving the
editor, launching, and a new build of the mod being swapped in.

All of that was done through the game's remote control (the AI Bridge). On Windows the keys were pressed in
the game's own window as well (by a program at the keyboard, not by a person): **no person has yet pressed
the keys or dragged the mouse** in the studio.

## Limits

* **The craft does not move.** The turntable turns the camera, the lights and the set round the craft, which
  looks the same as the craft turning on a turntable in front of a still camera. In the editor the craft is
  never touched; in flight it stands still because the game is paused.
* **In flight** the floor lies level across the planet's up, under the craft's lowest point: a lander that
  has fallen over lies on its side in the studio, as it does on the ground. The studio does not open from
  the map or from inside the craft, and gives way if Cinema Camera is switched on (both move the same camera).
* **What the parts reflect.** Only parts whose shaders take reflections from the scene show the studio in
  them; others are lit by the studio's lamps and the light from all round, and reflect nothing.
* **The shadow under the craft** (white studio) is worked out from the craft's colliders when the studio
  opens: parts without colliders cast none. Seen from low down, the darkest of it is under the craft, behind
  its lowest part.
* **The showroom's reflection** is a clean mirror image: real polished floors blur it a little, more the
  further the reflected thing is from the floor.
* **The game's post-processing** (TUFX) is held off in the white studio and before the green screen, so that
  the white stays white and the green one green; the showroom keeps it (glow round the lights).
* **Launch clamps** are hidden while the studio is open, and other craft near by in flight.
* While the studio is open the editor's picking and moving of parts is held off; to change the craft, close
  the studio.
