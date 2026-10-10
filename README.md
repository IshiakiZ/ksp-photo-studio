# Photo Studio

Your craft in a photographer's studio: a seamless white cyclorama, a green screen, or a dark showroom with a
polished floor that reflects it. A camera that goes round the craft, a turntable, and pictures larger than the
screen, with a see-through background if you want one. In the assembly building and the hangar (the building
and its crew go away while the studio is up), and in flight round the craft you are flying, with the game
paused. Closing the studio puts everything back exactly as it was.

For Kerbal Space Program 1.12.x. Needs [Keystone](https://github.com/IshiakiZ/ksp-keystone), which comes in the download.

![The same plane in the white studio, before the green screen and in the showroom](src/PhotoStudio/img/studio-plane.jpg)

## Install

Copy the `Keystone` and `PhotoStudio` folders from the download's `GameData` into your KSP `GameData`.

## Use

Press **Option-P** (Alt-P on Windows and Linux), or open the Keystone window (the keystone button on the
toolbar, or Option-K), turn to **Photo Studio** and press **Open the studio**.

* **Right mouse button**: turn round the craft, and up and down. **Wheel**: nearer and further. **Middle
  button**: raise or lower the view. The game's camera keys work too.
* **Option-Return** (Alt-Return): take a picture. It goes into the game's `Screenshots` folder as a PNG.
* **Option-1, 2, 3**: the white studio, the green screen, the showroom. **Option-T**: the turntable.
* **Option-P** or **Escape**: close the studio.

On the mod's page: which **studio**, the **lens** (18 to 200 mm) and **aperture** (depth of field), which side
the **key light** comes from, a **turntable**, the picture's **size** (up to four times the screen's), a
**see-through background** (white studio and green screen), the white backdrop's brightness, the shadow's
strength, the screen's colour and the showroom floor's gloss.

![Pictures taken with a see-through background, laid over a chequerboard](src/PhotoStudio/img/studio-clear.jpg)

## Good to know

* Everything in it is drawn with the game's own shaders and lights, from shapes worked out by the mod: no
  shader of its own and nothing meant for one system only. Made and tried on a Mac; **not yet run on Windows
  or Linux**. (Depth of field uses Keystone's lens, which on Windows needs TUFX.)
* While the studio is open in the editor its picking and moving of parts is held off and its panels are put
  away; saving, loading and launching still work. In flight the game is paused while the studio is open.

More: [the studios](docs/The-studios.md),
[settings](docs/Settings.md),
[pictures](docs/Pictures.md),
[how it works](docs/How-it-works.md),
[limits](docs/Limits.md),
[building it yourself](docs/Building.md).
