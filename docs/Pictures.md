# Pictures

**Option-Return** or **Option-S** (on Windows **Alt-S**, since Alt-Return never reaches a mod there), or
**Take a picture** on the mod's page, while the studio is open.

* The picture is drawn afresh for itself, by the same cameras that draw the screen, at the **Size** asked for:
  the screen's, or two, three or four times as wide and high (at most 8192 pixels on its longer side, and
  with the edges smoothed by multisampling). So the game's interface, the settings window and the studio's
  own hints are never in it.
* It is saved as a PNG in the game's own `Screenshots` folder (beside `GameData`), named after the craft, the
  studio and the time: `Kerbal X - white studio - 2026-10-09 14-31-07.png`.
* Taking one stops the game for a moment: here a picture twice the screen's size (2560 by 1440) took a fifth
  of a second, one four times the screen's size (5120 by 2880) two thirds of a second, and a see-through one
  that size a little over a second.

## See-through background

With **See-through background** on, in the white studio and before the green screen, the picture has no
background at all: only the craft (and, in the white studio, its shadow, as a see-through shade), for laying
over anything in a picture editor.

![See-through pictures from the white studio and the green screen, laid over a chequerboard](../src/PhotoStudio/img/studio-clear.jpg)

How: the craft is drawn twice more, over black and over white, with the backdrop hidden and without the
game's post-processing. Where the two pictures are the same, the craft is solid; where they differ by all of
it, there is nothing; in between (the soft edge of a part, a window, the shadow) it is that much see-through.
Its colour is what it is over black, divided by how solid it is. This needs nothing from the parts' own
shaders (which do not all write a usable transparency), and gives soft edges, glass and the shadow their true
transparency, with no fringe of the backdrop's colour round the craft: which is what a green screen is for,
done without the keying.

The showroom's pictures always keep their background: its floor's reflection belongs to the picture.
