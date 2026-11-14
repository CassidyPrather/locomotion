# Wirenook Locomotion

Procedural walking and running for VRChat avatars. A parametric model of a
biped's gait is baked into humanoid animation clips, and two generated animator
controllers replace VRChat's default locomotion with them: the Base layer plays
the gaits, the Additive layer leans the body into acceleration and turns.

"Weighty" is a set of numbers here, not a mood. The pelvis sinks into each step
and rises over the planted leg, the knee gives after the heel lands, the upper
body trails the pelvis, stride and cadence follow speed the way a person's do,
and a smoothed copy of the velocity makes the body lean when it speeds up and
rock back when it stops.

Package: `Packages/net.wirenook.locomotion` (`net.wirenook.locomotion`).
Requires the VRChat Avatars SDK 3.10.6+, Modular Avatar 1.18.7+ and
`net.wirenook.assets` 0.1.0+.

## Use

1. Drag `Prefabs/Wirenook Locomotion.prefab` into the avatar, anywhere under
   its root. At build time Modular Avatar replaces the avatar's Base and
   Additive playable layers with the generated controllers. Nothing else on the
   avatar changes; there are no expression parameters, menu entries or synced
   bits.
2. Turn on **Force Locomotion Animations** on the avatar descriptor if the
   gaits should keep playing while moving in full-body tracking. The
   controllers never switch tracking off to make that happen.

The prefab ships with a bake made on the robot from the VRChat SDK's samples
(leg 0.83 m). Humanoid clips are retargeted by Unity, so it works on any
humanoid, but stride length then scales with the avatar's size while the
world's speeds do not: on an avatar much larger or smaller than the robot the
feet slide in proportion. To fit an avatar:

- select its prefab and run **Tools > Wirenook > Locomotion > Bake For Selected
  Avatar**, or call
  `Wirenook.Locomotion.Editor.LocomotionBuild.BakeFor(avatarPrefabAssetPath, outputFolder)`.

  The output folder receives `Locomotion Base.controller`,
  `Locomotion Additive.controller`, a `Wirenook Locomotion.prefab` that installs
  them, and `Gait Profile.asset`, a copy of the style used. Use that prefab
  instead of the shipped one. Edit the profile and bake again to restyle that
  avatar alone.

Other entry points, also under **Tools > Wirenook > Locomotion**:

- `LocomotionBuild.Rebuild()` regenerates the shipped bake, controllers and
  prefab from `Profiles/Weighty Gait.asset`. The prefab is edited in place, so
  its GUID and component ids are stable.
- `LocomotionBuild.Verify()` runs every check described under
  [Verification](#verification) against the shipped bake and returns the report.

## The gait model

The model lives in `Runtime/Core/`, has no Unity dependency and is unit-tested
with plain `dotnet`. `GaitCycle` takes body proportions, a `GaitProfile` (the
style) and a `GaitRequest` (ground speed in m/s and a direction of travel
relative to where the body faces) and returns, for any phase of the cycle, a
pose in root space: pelvis position and rotation, ankle position and foot
rotation for both feet, chest and head orientation and arm angles. Phase 0 is
the left foot landing; the right foot runs half a cycle behind.

- **Regime and rhythm** (`GaitTiming`). Speed is made dimensionless as
  v / sqrt(g × leg length), so the same number means the same gait at any body
  size. Below `WalkRunTransition` the gait is a walk (each foot down more than
  half the cycle, so both are down twice per stride); above it a run (less than
  half, so there are two flight phases). Stride length follows a power law in
  speed: about the square root when walking, steeper when running, where
  cadence is nearly constant. A runner's foot is down for a fixed sweep of the
  leg, so its share of the cycle falls as speed rises.
- **Feet.** Each foot is planted for its share of the cycle. While planted, the
  point of the sole it rolls on (heel, whole sole, then ball) moves back through
  root space at exactly the root's velocity, so it does not slide; the ankle
  arcs over that point as the foot rolls. In swing a point of the sole is
  carried from where the foot left to where it lands, starting and ending at
  rest over the ground, and lifted on an arch that clears the ground whatever
  the foot's pitch.
- **Direction.** Each foot keeps to its own track, a line parallel to the
  travel direction, so strafing and backpedalling come from the same model and
  the feet cannot collide. The pelvis and feet turn part of the way toward the
  travel direction (`HipTurn`) and the chest keeps facing forward. Moving
  backward the ball of the foot lands first.
- **Pelvis height.** With the feet fixed, the model computes how high the legs
  allow the pelvis at each instant. A walker's pelvis follows the smooth part of
  that ceiling, which is the vault over each planted leg, less a sag that the
  body's weight adds after each heel strike (`LoadingDrop`, `LoadingDelay`); the
  knee bends by whatever that takes. A runner's pelvis follows the path of a
  bouncing mass: a half-sine of leg force on the ground, free fall in the air.
  Either way the legs never have to stretch past straight.
- **Upper body.** The chest counter-rotates against the pelvis and leans with
  speed; the head keeps its heading; arms swing against the legs. All of it runs
  `UpperBodyLag` behind the pelvis.
- **Legs.** `TwoBoneIk` places the knee analytically from hip, ankle and a pole
  direction, and clamps unreachable targets to the nearest reachable point.

`StandingCycle` is the idle: weight shifts from one foot to the other and back
over one loop, with two breaths. `Posture.Stand` gives the static leaning poses
the Additive layer blends.

### Style parameters

All are fields of `GaitProfile`, edited on a `GaitProfileAsset`
(**Create > Wirenook > Locomotion > Gait Profile**). Fractions are of the named
body length, so a profile carries over between body sizes.

| Parameter | Unit | Range | Default | Effect |
| --- | --- | --- | --- | --- |
| `Weight` | unitless | 0 to 2 | 1 | Master scale. Multiplies loading drop and delay, pelvis sway, list and surge, upper-body lag and crouch, and lengthens ground contact. 0 is a purely geometric gait. |
| `LoadingDrop` | fraction of leg length | 0 to 0.05 | 0.02 | Extra sink of the pelvis after heel strike; this is what bends the loaded knee. |
| `LoadingDelay` | fraction of a step | 0.05 to 0.3 | 0.16 | How long after heel strike the sink bottoms out. |
| `Crouch` | fraction of leg length | 0 to 0.1 | 0.005 | Lowers the whole pelvis path so the knees never lock. |
| `UpperBodyLag` | fraction of a cycle | 0 to 0.15 | 0.05 | How far chest, head and arms trail the pelvis. |
| `PelvisSway` | fraction of hip width | 0 to 0.5 | 0.25 | Sideways travel toward the supporting foot at a crawl; narrows with speed. |
| `PelvisList` | degrees | 0 to 10 | 4 | Drop of the hip on the swinging side. |
| `PelvisYaw` | degrees | 0 to 12 | 5 | Pelvis rotation with the stride when a step is one leg long; scales with step length. |
| `Surge` | fraction of leg length | 0 to 0.03 | 0.012 | Fore-aft surge as each step brakes and pushes. |
| `RunBounce` | unitless | 0.5 to 1.5 | 1 | Vertical travel of a run relative to the ballistic bounce. |
| `StrideScale` | unitless | 0.7 to 1.3 | 1 | Multiplies stride length; cadence compensates. |
| `WalkRunTransition` | dimensionless speed | 0.6 to 1 | 0.8 | Where walking becomes running; 0.8 is about 2.3 m/s for an adult. |
| `LateralStride` | unitless | 0.4 to 1 | 0.6 | Stride length sideways relative to forward. |
| `BackwardStride` | unitless | 0.5 to 1 | 0.75 | Stride length backward relative to forward. |
| `HipTurn` | share of travel angle | 0 to 0.8 | 0.45 | How far pelvis and feet turn toward the travel direction. |
| `StepWidth` | fraction of hip width | 0 to 1.5 | 0.6 | Distance between the feet's tracks. |
| `StepHeight` | fraction of leg length | 0.02 to 0.2 | 0.06 | Ground clearance of the swinging foot when walking; runs lift higher. |
| `ToeOut` | degrees | -5 to 20 | 6 | Outward turn of each foot. |
| `HeelStrikeAngle` | degrees | 0 to 30 | 18 | Toe-up angle as the heel lands. |
| `ToeOffAngle` | degrees | 20 to 70 | 60 | Heel-up angle as the toes leave. |
| `SpineCounterRotation` | multiple of pelvis yaw | 0 to 1.5 | 0.7 | Chest rotation against the pelvis. |
| `ForwardLean` | degrees per unit of dimensionless speed | 0 to 12 | 5 | Lean into the travel direction; about 3° at a brisk walk, 10° at a sprint. |
| `ArmSwing` | degrees | 0 to 40 | 22 | Shoulder swing each way at a brisk walk; grows with speed. |
| `ArmSpread` | degrees | 0 to 25 | 8 | How far the arms hang from the body. |
| `RunElbowBend` | degrees | 30 to 110 | 80 | Elbow bend carried while running. |
| `IdlePeriod` | seconds | 4 to 16 | 8 | Length of the idle loop. |
| `IdleSway` | fraction of hip width | 0 to 0.4 | 0.12 | Pelvis travel toward each foot while idling. |
| `BreathDepth` | degrees | 0 to 4 | 1.5 | Chest rise per breath. |
| `InertiaTime` | seconds | 0.1 to 0.8 | 0.3 | Time constant of the smoothed velocity. |
| `AccelerationLean` | degrees per m/s | 0 to 5 | 2.5 | Lean per m/s of difference between actual and smoothed velocity. |
| `MaxLean` | degrees | 0 to 20 | 12 | Cap on that lean. |
| `TurnBank` | degrees | 0 to 15 | 6 | Bank at `TurnBankRate` while moving at `TurnBankSpeed` or faster. |
| `TurnBankRate` | degrees per second | 60 to 360 | 180 | Turn rate that gives the full bank. |
| `TurnBankSpeed` | m/s | 1 to 8 | 4 | Forward speed at which banking reaches full strength. |

The asset also holds **Measure Body**. When on, leg length, thigh share, hip
width, foot length and ankle height are measured from the rig's T-pose. When
off, the values on the asset are used and the result is scaled to the rig by
leg length.

## What is generated

`LocomotionBuild` bakes by posing a copy of the rig's skeleton at each sample
(pelvis from the model, legs from the IK solution, spine, head and arms from
the model's angles), reading the pose back through `HumanPoseHandler` and
writing humanoid curves: `RootT.*`, `RootQ.*` and the muscles of the spine,
neck, head, arms to the wrists and legs. Fingers, eyes and jaw are not keyed.
Keys are reduced to the fewest that reproduce every sample within 0.002 of a
muscle's range.

Clips, each baked twice (see [VR](#vr)):

- idle;
- stroll at 0.7 m/s forward, backward and right; amble at 1.2 m/s forward;
- walk at 2 m/s forward, backward, right, and the diagonals (2, 2) and (2, -2);
- run at 4 m/s forward, backward, right, and the diagonals (2, 4) and (2, -4);
- sprint at 6 m/s forward;
- seven standing poses for the lean: neutral, forward, back, left, right, bank
  left, bank right.

The clips are stored inside the two generated controllers, about 8 MB of text
between them.

Leftward travel uses the rightward clips mirrored and offset half a cycle,
which the model's symmetry makes exact. The walk and run speeds are VRChat's
defaults, and (2, 4) is what a desktop player produces running diagonally,
because the strafe speed caps sideways motion.

### Base controller

Parameters: `VelocityX`, `VelocityY`, `VelocityZ`, `Upright`, `Grounded`,
`Seated`, `AFK` (VRChat built-ins), `VRMode` declared as a float so it can
drive a blend tree, and `Wirenook/Locomotion/Cadence`, which the controller
computes.

- **Locomotion** has the states and thresholds of VRChat's default controller.
  Standing is a 2D freeform directional blend tree on `VelocityX` /
  `VelocityZ` with each clip at the velocity it was baked for. Crouching and
  Prone are VRChat's trees of `proxy_*` clips, switched by `Upright` (below
  0.68 to crouch, below 0.41 to go prone, above 0.43 and 0.7 to come back).
  SmallHop, Short Fall, LongFall, QuickLand, HardLand, RestoreTracking and
  RestoreToHop use `proxy_fall_*`, `proxy_land*` and `proxy_stand_still` and
  are driven by `Grounded` and `VelocityY` as in the default. The default's
  jump-and-fall sub-state machine is flattened into the layer.
- **Cadence** sets a playback-speed multiplier for Standing: 1 inside the
  fastest baked speeds (6 m/s ahead, 4 m/s back and sideways), rising in
  proportion beyond them, up to 4×. Blend trees do not extrapolate; this is
  what keeps the feet on the ground in worlds that raise the speed limits.

The airborne states carry the same `VRCAnimatorTrackingControl` settings as
VRChat's default (hips and feet animated during a fall, everything during a
long fall, tracking restored on the way out). Standing, Crouching and Prone
carry none. If the avatar should keep its tracked limbs during falls, remove
the `Track(...)` calls in `LocomotionControllers.BuildLocomotionLayer`.

### Additive controller

Parameters: `VelocityX`, `VelocityZ`, `AngularY`, `VRMode`, `TrackingType`
(built-ins) and nine computed ones under `Wirenook/Locomotion/`. The computed
parameters are not in any expression parameter list: they are not synced, and
every client derives them from the built-ins it already has.

- **Clock** plays a clip that ramps a parameter from 0 to 10 seconds and loops.
- **Inertia** is a direct blend tree that, every frame, measures the frame time
  (the clock minus last frame's copy of it), moves `SmoothX`, `SmoothZ` and
  `SmoothTurn` toward `VelocityX`, `VelocityZ` and `AngularY` by
  frame time / `InertiaTime` of the remaining distance, and writes the
  differences `PushX` and `PushZ` between raw and smoothed velocity.
- **Lean** (additive) blends the leaning poses on `PushX` / `PushZ`: the body
  leans the way it is accelerating and returns as the smoothed velocity
  catches up, which after a stop reads as rocking back and settling.
- **Bank** (additive) blends the banking poses on `SmoothTurn` and `SmoothZ`,
  in proportion to turn rate times forward speed.

The lean is in the Additive playable layer because that is where VRChat mixes
additive humanoid motion over Base. Each lean clip names the neutral pose as
its additive reference, so only the difference is applied.

Parameter smoothing done with a fixed blend share per frame has a time
constant that depends on frame rate. Measuring the frame time and taking
frame time / time constant instead is one Euler step of the filter per frame:
the time constant holds to first order at any frame rate, the share is capped
at 1 when a frame is longer than the time constant, and the single frame in
which the clock wraps holds the previous value. In the simulation, one time
constant after a 4 m/s step, the smoothed velocity is within 0.13 m/s of the
ideal filter at every frame rate tried from 20 to 144 fps.

### VR

In VR the head and hands follow the player, and in full-body tracking the hips
and feet do too; animated upper-body motion fights that.

- Base picks its clip set with `VRMode`. On desktop it plays the full gaits.
  In VR it plays a second set baked with `GaitProfile.ForTrackedUpperBody()`:
  the same footfalls, no arm swing, chest counter-rotation, lean, lag or
  breathing, and half the pelvis rotation, sway and hip turn.
- Lean and Bank have one state per audience. Desktop (`VRMode` 0) gets the full
  lean. Head and hands tracked (`VRMode` 1, `TrackingType` 3) gets 35% of it.
  Hips or feet tracked (`TrackingType` above 3) gets none.

All states have Write Defaults on.

## Verification

`scripts/test.sh` runs 38 engine-free tests of the model: periodicity and
continuity, planted feet moving at exactly minus the root velocity, no sole
point below the ground, left and right half a cycle apart and mirrored, stride
over duration equal to the requested speed, double support when walking and
flight when running, legs always reaching their targets, direction handling,
the weight parameters' effects, and the IK solver on reachable, unreachable
and degenerate targets.

`LocomotionBuild.Verify()` checks the bake in the editor, without play mode:

- **Clips.** Every gait clip is sampled back onto a fresh copy of the rig at 60
  phases between its keys. On the shipped bake the worst ankle is 7.3 mm from
  where the model put it (limit 20 mm), the lowest heel or ball is 2.8 mm below
  the ground (limit 10 mm) and a flat foot creeps at most 5.1 mm over a stance
  (limit 10 mm). This is also what confirms the curve names and clip settings.
- **Controllers.** The Base and Additive controllers are run in a playable
  graph stacked as VRChat stacks them, with the built-in parameters driven by
  hand. Checked: the blend tree reproduces the model at baked velocities,
  including mirrored and VR sets (7.4 mm); planted-foot speed is within 1.8% of
  ground speed at baked velocities and within 19% between them; the cadence
  multiplier is 1.00 inside the baked speeds and 2.00 at twice the fastest;
  posture and jump/fall states follow `Upright`, `Grounded` and `VelocityY`;
  smoothing is frame-rate independent as above; stopping from 4 m/s rocks the
  pelvis back 80 mm on desktop, 28 mm with head and hands tracked, 0 with the
  body tracked, and returns to rest; banking is proportional to turn rate and
  absent at a standstill.
- **Install.** The prefab is put on a throwaway avatar and built with Modular
  Avatar; the avatar's Base and Additive layers must come out as the generated
  controllers and still pass the gait and lean checks.

## Limits

- Nothing has been run in the VRChat client. The editor simulation covers what
  Unity alone decides (clips, blend trees, layer mixing, animated parameters).
  It does not cover VRChat's IK, tracking, network-synced velocity on remote
  avatars, or the client's substitution of `proxy_*` clips. In particular:
  - the unit and sign of `AngularY` are assumed to be degrees per second,
    positive turning right; if not, `TurnBankRate` or the bank's direction
    needs changing;
  - how the reduced VR clip set and the 35% lean feel against real head and
    hand tracking is a judgement nobody has made yet;
  - behaviour with Force Locomotion Animations in full-body tracking is
    untested;
  - the Additive controller relies on animated animator parameters inside the
    Additive playable layer behaving as they do in a plain Unity playable
    graph.
- Between baked velocities the blend tree mixes neighbouring gaits, whose
  stride lengths and cadences do not combine into exactly the requested speed.
  The planted foot is up to 19% off ground speed in the worst places measured:
  backward and sideways between 0.7 and 2 m/s, and between a walk and a run,
  where the two gaits also disagree about when the foot is down. Below 0.7 m/s
  the idle is blended in and the steps are slower than the ground.
- Beyond the fastest baked speeds the cadence correction is exact straight
  ahead, back and sideways and overestimates by up to a third on fast
  diagonals.
- No foot IK goal curves (`LeftFootT`, `LeftFootQ`, ...) are written, and the
  states leave Foot IK off, as VRChat's default controller does. Unity gives no
  editor-time way to read the goals it would compute, and none was derived. On
  the rig a bake was made for this costs nothing, since the muscle curves put
  the feet where the model did; on a different rig the feet land where Unity's
  retargeting puts them.
- The shipped bake fits the SDK's robot. See [Use](#use) for fitting another
  avatar.
- The gait comes from a kinematic model with a few fitted relations, not from
  motion capture. Toes are a single hinge, the foot has no roll at the ankle,
  and the hands are not posed.

## Flight

`Prefabs/Wirenook Flight.prefab` adds collider flight: a "Flight" switch in
the menu that lets you stand, walk and climb in mid-air. Drag it onto an
avatar like the locomotion prefab; the two are independent. It costs one
synced bit, one constraint and one physics collider (which ranks Good).

Under your feet is an invisible platform with a small cloud drawn on it.
The platform follows you sideways, and its height is either latched in the
world or following your feet. There are no extra controls; it reads
parameters VRChat already provides.

| You | The platform | Result |
| --- | --- | --- |
| Switch Flight on | Latches at the height of your feet | You stop falling and can walk on the spot you are at. |
| Jump | Lets go while you are moving upward, and latches again at the top | Each jump is a step up. |
| Crouch | Is gone while you are crouched | You sink; standing up catches you. |
| Switch Flight off | Is gone | You fall. |

How it is built: the platform is a child of the prefab with a VRChat
Position Constraint that affects the Y axis only. Animating the
constraint's Freeze To World option latches that axis and leaves the other
two following the avatar. The animator (`Generated/flight-fx.controller`)
has five states, Off, Hover, Rise, Sink and a non-solid Hover for everyone
else's copy of your avatar, switched by `VelocityY` (rising faster than
0.6 m/s), `Upright` (below 0.55 sinks, above 0.68 catches) and `IsLocal`.
Only the wearer's own copy has a solid collider.

Rebuild it with `Tools > Wirenook > Locomotion > Rebuild Flight`
(`Wirenook.Locomotion.Editor.FlightBuild.Rebuild`). The cloud model comes
from `blender/`.

Not yet run in the VRChat client. It rests on two things only a client can
confirm: that a collider on your own avatar holds up your own player
capsule (the basis of every collider-flight prefab), and that jumping off
such a collider works as it does off world geometry. The collider is on
the Default layer. The thresholds above are first guesses.

## Development

Requires Unity 2022.3.22f1 (see `ProjectSettings/ProjectVersion.txt`) via a VPM
client (prefer [ALCOM](https://vrc-get.anatawa12.com/en/alcom/)).
Requires [.NET SDK](https://dotnet.microsoft.com/download) 11 for headless checks.

Open: add this folder as a project in your VPM client, then open in Unity. The
client installs the defaults pinned in `Packages/vpm-manifest.json`: the
VRChat Avatars SDK, [lilToon](https://lilxyzw.github.io/lilToon/), and
[Modular Avatar](https://modular-avatar.nadena.dev/) (with NDMF). Add their
listings to your client once: `https://lilxyzw.github.io/vpm-repos/vpm.json`
and `https://vpm.nadena.dev/vpm.json`

Test (editor): Window > General > Test Runner, EditMode tab

```bash
scripts/check-meta.sh          # every visible file has a committed .meta
scripts/check-manifests.py     # package.json sanity across Packages/
scripts/format.sh --check      # dotnet-format over the CI-visible C#
scripts/test.sh                # headless NUnit run of Runtime/Core + Tests
scripts/build-packages.sh      # VPM .zip + .unitypackage per package -> dist/
```

Everything in `Runtime/Core/` is engine-free C# (`System.Numerics` for
vectors) and is what `scripts/test.sh` compiles; the `GaitProfileAsset`
wrapper, the baker and the controller generator are Unity code and are
exercised by `LocomotionBuild.Verify()` instead.

### Advanced

Pre-commit hook: `git config core.hooksPath .githooks` (runs the meta and
manifest checks)

Release:

1. Bump `version` in the package's `package.json`
2. Commit and push
3. Create a GitHub release tagged with that version — CI attaches the `.zip`
   and `.unitypackage` for every package whose version matches the tag
4. Add the repository to your listing's `githubRepos`
   ([CassidyPrather/vpm](https://github.com/CassidyPrather/vpm)) once; every
   later release is picked up from the release assets automatically

See [docs/GETTING_STARTED.md](docs/GETTING_STARTED.md) for curated links:
tooling, docs, avatar frameworks, asset sources, and so on and so forth.

## Legal

Copyright 2026 Cassidy Prather <cassidy@wirenook.net>

Everything in this repository henceforth is licensed under
[AGPLv3](https://www.gnu.org/licenses/agpl-3.0.html) (See `LICENSE`) unless
otherwise specified.

As a special exception, the copyright holders give permission to link this
Program with the VRChat SDK and with Unity Technologies' proprietary engine
components, and to convey the resulting work under terms of your choice,
provided that you also convey the Corresponding Source of this Program under
the terms of the GNU Affero General Public License version 3 in all other
respects. (This exception also in `LICENSE_ADDENDUM`.)

## Credits

Descended from https://github.com/vrchat-community/template-package
([VRCHAT DISTRO LICENSE FILE](https://github.com/vrchat-community/template-package/blob/d9cf13fe9f56867cbf7315a4dbbf1901bc1537ec/Packages/com.vrchat.core.bootstrap/License.md))

The shipped bake was made by posing the robot avatar from the VRChat Avatars
SDK's samples; the generated clips hold only humanoid muscle values, and
nothing from that model is redistributed here. The generated controllers
reference the SDK's `proxy_*` clips and its `VRCAnimatorTrackingControl`
behaviour, which every project with the SDK already has.

Vendored third-party material is inventoried in [CREDITS.md](CREDITS.md).
