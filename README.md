# HSPE (fork) for Honey Select Studio NEO

A drop-in replacement for Joan6694's **HSPE** (HSPENeo.dll 2.12.0.2) with a new interface, copy / paste and presets,
a Face window, undo for HSPE edits, and Gru's bone presets / Blend / Reset All / bone settings on cards (version 2.16.0
merges the HSPE 2.15.0 bone-presets fork). Scenes, VNGE scripts, Timeline tracks and other plugins that use HSPE keep
working unchanged.

## Install

Copy the `Plugins` folder into the game folder and replace the files when asked (the old `HSPENeo.dll`, and an older
`HSUIKit.dll` if you have one). Close Studio first. Do not keep two HSPE versions in `Plugins` (for example the 2.15.0
bone-presets fork next to this one): this version replaces both.

| File | What it is |
|---|---|
| `HSPENeo.dll` | the plugin (same name as the original) |
| `HSUIKit.dll` | the UI library the new windows are built with - [HSUIKit](https://github.com/mucski/HSUIKit), **1.3.0 or newer** (an older one shows a red notice and the original windows) |
| `HSPE\face_recipes.txt` | what each mood, vowel and face point does (plain text, editable) |
| `HSPE\face_presets\*.xml` | the 10 default face presets |

Also needed, already part of every Honey Select plugin setup (the original HSPE needs them too): `0Harmony.dll` and
`HSExtSave.dll` in `Plugins`.

Everything you save goes to `UserData\HSPE\` (pose presets, face presets, face-point pins); bone presets go to
`Plugins\HSPE\BonePresets\`, as in the 2.15.0 fork, so its presets keep working. Your old HSPE settings
(`Plugins\HSPE\configNEO.xml`, blend-shape presets, bone aliases, shortcuts) are used as they are.

To get Joan's original windows back: small window → Options → turn off **New HSPE window**, restart Studio.

## New in 2.17.0

- **FK and Hands pages: right-click menu**, the same as on the IK page: copy an arm, hand or leg to the other side,
  or swap the pose left / right.
- **FK and Hands pages: several bones at once.** Ctrl-click joints to add or remove them; on the Hands page,
  right-click a finger joint for **Select whole finger** or **Select all fingers**. The rotate pads turn every
  selected bone by the same amount (one Ctrl+Z undoes it), so a whole finger curls in one drag.
- **Bones page search** now finds the bones of swapped hair or clothes and of a reloaded character (it used to keep
  searching the bones from before the change).

## What's new

**Small window** - a mannequin figure instead of the button grid.
- **IK**: click the circles (effectors) and diamonds (bend goals) on the figure; right-click a limb for its menu.
- **FK**, **Hands** (finger joints) and **More** (every other FK bone, grouped like Studio's list). On FK and Hands,
  Ctrl-click selects several bones and right-click opens the limb menu (Hands: select a whole finger).
- The window resizes, and the figures grow with it.
- **Copy / Paste / Presets** in the header: copy a character's settings and paste them onto others, or save them as a
  preset with a thumbnail. Choose which parts travel: Bones, Colliders, Dynamic Bones, Blend Shapes, IK, Boobs and,
  optionally, the Studio pose. Every paste can be undone from the notification.

**Advanced window** - one resizable window with a sidebar instead of Joan's tabs.
- **Bones**: searchable bone tree, "changed only" filter, favourites column (add, rename, remove), and an editor
  drawer - drag a number to change it, click to type, mouse wheel to nudge; symmetry, copy / paste, reset menu.
- **Dynamic Bones, Boobs, Colliders, Blend Shapes, IK** pages, **Presets** page with thumbnails.
- The number next to each module shows how many things you changed on the selected object.

**Face window** (face icon in the small window's header, or "Face..." in the Advanced sidebar):
- **Mood pad**: drag the dot - direction picks the mood (Joy, Excited, Surprise, Shy, Anger, Sad, Sleepy, Smug),
  distance from the centre is the strength. In between two moods you get a blend.
- **Vowels** A / I / U / E / O change only the mouth. **Eyes open / Mouth open** sliders (Studio's own sliders can't
  move a face HSPE holds).
- **Face points**: drag brows, eyelids, mouth corners, lips and jaw on the drawn face. Shift mirrors, Ctrl is fine
  control, right-click resets a point. Bones that mods add to the face show up in a list and can be pinned to the face.
- **Face presets**: 10 defaults, or save your own with a face thumbnail (optionally with the eye direction). Tears and
  blush are part of a preset. Presets work on female and male characters.
- **Timeline**: add the **Face** track (under HSPE) and keyframe whole faces - the mood dot glides around the pad
  between keys.
- Works on male and female heads and on modded heads with extra shapes or bones.

**Bone presets, Blend, Reset All and bone settings on cards** (from the HSPE 2.15.0 bone-presets fork, merged in 2.16.0).
In the new Advanced window: **Bone presets** in the sidebar (Load / Blend / Create tabs), and **Save to card** and
**Reset All...** in the Bones page toolbar (Save to card is a switch: on = the next character-card save includes the
bone edits; it saves nothing by itself, and is off unless turned on or loaded from a card / scene that had it). In the original window (New HSPE window off): Advanced Mode → Bones, as
in the 2.15.0 fork. Both windows use the same presets and settings.
- **Bone presets**: save only the bones and position / rotation / scale values you pick; load with **Keep current
  edits** or **Overwrite edits**, with a per-value preview before anything changes. Presets are filtered to the
  character's sex or the item's own model. Stored in `Plugins\HSPE\BonePresets`.
- **Blend** the current pose towards a preset, or between two presets, with a live preview; Apply or Cancel.
- **Reset All...** puts every modified bone of the character / item back to its value before editing, FK included.
- **Save current settings on card**: the character's bone edits travel inside the character card.

**Undo for HSPE edits**: Ctrl+Z / Ctrl+Shift+Z now also undo and redo HSPE's own edits, in order with Studio's own
steps: Advanced window changes (bones, Dynamic Bones, colliders, blend shapes, boobs, IK weights), bone presets, Blend,
Reset All, HSPE gizmo drags, and copy / paste / presets. A drag, a run of wheel nudges or a typed value is one step;
the edit covers every selected object. The Face window keeps its own undo button.

## Compatibility

- Scenes: the original HSPE section is written as before, plus a fuller bone section (exact bone addresses and reset
  values) that the 2.15.0 bone-presets fork also reads, and a `<face>` entry for characters that use the Face window.
  The original HSPE and older versions of either fork read the parts they know and ignore the rest. Scenes, presets
  and cards made with either fork load unchanged.
- VNGE, Timeline (all existing HSPE tracks), NodesConstraints, Renderer Editor, HSSceneStates: unchanged behaviour.
- VNGE's own Scene Save State only stores HSPE data when `ExportChara_HSPENeo=1` in `Plugins\Console\Lib\vnactor.ini`
  (off by default to keep saves small). With it off, a face set in the Face window is the same on every VNGE state.

## Known limits

- Copy / paste between a female and a male transfers nothing (their skeletons have different bone names).
- The original HSPE drops a few entries on load / save of some older scenes; the fork behaves the same (not fixed).
- Bones whose name appears twice under the same parent are saved only in the fuller bone section, so the original
  HSPE and older fork versions skip those few bones when they open a scene saved with this version.
- Copy / paste and pose presets carry the original HSPE bone section, so those same few duplicate-name bones are not
  pasted (before, they could land on the wrong one of the two bones).

## License

GPL-3.0 (see `LICENSE`), like the original's source release is treated. HSUIKit is GPL-3.0 as well.

## Credits

HSPE by **Joan6694**. This fork: new UI (HSUIKit), copy / paste / presets, Face window, Face Timeline track, and fixes
for a few bugs in the original (Dynamic Bones field reset zeroed the value; full-body IK weight sliders did nothing
until the IK had been edited; the IK weight shown was Studio's per-frame value; loading HSPE data could freeze the game
when a bone driven by FK had a rotation set in HSPE).

Bone presets, Blend, Reset All, bone settings on cards and scene save protection: the HSPE 2.15.0 bone-presets fork by
**Gru** (Grubster on Discord), based on ElusiveCake's `HSExtSave` branch, merged here with his permission. It also fixed two
original bugs: Dirty Scale with symmetry / children copied the selected bone's scale onto the others, and duplicating
an object could copy the wrong bone edits if the source changed in the same frame.

## Building

Visual Studio 2019+ / MSBuild, .NET Framework 3.5 target. Clone [HSUIKit](https://github.com/mucski/HSUIKit) next
to this repository and build it first:

```
msbuild ..\HSUIKit\HSUIKit.csproj /p:Configuration=Release /p:HSDir="C:\path\to\HoneySelect"
msbuild HSPE.csproj /p:Configuration=Release /p:HSDir="C:\path\to\HoneySelect"
```

`Upstream\` holds Joan's HSPE source (with ToolBox_Shared and UIUtility_Shared); every change in there is marked
"(HSPE fork)" in the code. `Kit\` is the new interface, `Data\` the face recipes and default face presets,
`Tests\` a console test program.
