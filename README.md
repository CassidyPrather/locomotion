# Wirenook Locomotion

Procedural walking and running for VRChat avatars. A parametric model of a
biped's gait is baked into humanoid animation clips, and two generated animator
controllers replace VRChat's default locomotion with them: the Base layer plays
the gaits, the Additive layer leans the body into acceleration and turns. The
same model crouches, and gives the body a shape for jumping, falling and
landing, so that only lying prone is left to VRChat's own animation.

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

To choose a style for an avatar by wearing it:

- **Bake Every Style For Selected Avatar**, or
  `LocomotionBuild.BakeStyles(avatarPrefabAssetPath, outputFolder)`, bakes
  every named style (see [Styles](#styles)) for that avatar into one pair of
  controllers and writes a `Wirenook Locomotion Styles.prefab` that installs
  them with a **Walk style** menu: one switch per style, one synced integer
  (eight bits). Put it on the avatar in place of the locomotion prefab, not
  beside it, and switch between the styles in game.

  It is a tool for choosing, not something to keep on an avatar: the Base
  controller holds every style's clips, about 15 MB of text apiece.
- `LocomotionBuild.BakeForInStyle(avatarPrefabAssetPath, outputFolder, "Sway")`
  then makes the one chosen that avatar's own: it sets the folder's
  `Gait Profile.asset` to the style and bakes it as `BakeFor` does.

Other entry points, also under **Tools > Wirenook > Locomotion**:

- `LocomotionBuild.Rebuild()` regenerates the shipped bake, controllers and
  prefab from `Profiles/Weighty Gait.asset`. The prefab is edited in place, so
  its GUID and component ids are stable.
- `LocomotionBuild.Verify()` runs every check described under
  [Verification](#verification) against the shipped bake and returns the report.

## Also in the package

Three more drop-in prefabs. Each is independent of the gaits and of the
others.

| Prefab | What it does | Synced bits | Rank cost |
| --- | --- | --- | --- |
| `Wirenook Flight` | A cloud under the feet to stand on in mid-air (see `Editor/FlightBuild.cs`). PC only. | 1 | 1 collider, 1 constraint |
| `Wirenook Stepladder` | A ladder that comes out under your feet when you have lifted yourself off the floor by moving your play space. | 2 | 11 constraints, 9 bones |
| `Wirenook Nap` | While you are away (VRChat's AFK, or the "AFK" switch it adds to the menu) the avatar stands asleep, nodding off, with Zs rising from its head, instead of VRChat's kneel. | 1 | 3 bones on PC; nothing on Android and iOS |

### Stepladder

VRChat keeps an avatar's root on the ground under the player while tracking
carries the body wherever the headset is, so someone who has dragged their
play space up half a metre is seen hovering. The ladder fills the gap: a
base on the ground under the feet, a platform under the soles, and seven
steps spread evenly between.

Nothing about its shape is animated or synced. Each step is held by a
position constraint at a fixed share of the way from the base to the
platform, both of which follow the feet as each viewer sees them, so it is
the right height on every client by construction. It reaches 2.9 m; lower
than that the lengths of rail overlap, which a flat colour hides, and the
lowest length runs into the floor.

Whether it is out is one synced bit, and the wearer's client decides it. A
contact on the ground reads how high the soles are (the sender-position
output of SDK 3.10.6). The ladder comes out once they have been more than
0.30 m up for 0.4 s, with the player grounded and not seated, so hops and
chairs do not count; it goes away below 0.18 m. The gap between the two
heights keeps it from flickering at the edge, and deciding in one place
keeps every viewer in agreement. The menu's "Stepladder" switch (on by
default) is permission for it to come out by itself.

It also serves for sitting on the floor in the air: the reading is the
midpoint of the two ankles less an ankle's height, which is the lowest part
of a figure that is standing, or sitting with its legs down.

The model is skinned and joins the avatar's mesh, so it costs no mesh or
material slot. The price is that a merged mesh is culled by the avatar's
own bounds: the foot of a tall ladder can blink out for a viewer who can
see it but not the wearer above it.

### Nap

`NapCycle` (in `Runtime/Core`, beside the gaits) is the pose: the ordinary
stand let go, with the chest slumped, the arms hanging plumb, the head
forward and to one side, three slow breaths to a loop and the head sinking
until it is caught with a small start. It is baked like the gaits and has
the same kind of tests.

It starts on either of two things: VRChat's own `AFK`, or the "AFK" switch
the prefab adds to the menu (`WN/Away`, one synced bit, the same switch the
other ways of being away use; see `WirenookAway` in `net.wirenook.assets`).

It goes into the Action playable layer as a layer added on top of whatever
is there, which for most avatars is VRChat's default, on a layer whose
weight is faded up while the wearer is away and down after. At weight zero
it changes nothing, so emotes play as before. VRChat's default raises the
Action layer and hands the body from tracking to animation for its own AFK
and for nothing else, so the nap does both itself as well, for the switch,
and does them again every two seconds while it lasts: VRChat hands the
body back when its own AFK ends, whatever the switch says.

The Zs are a small model on the head, switched by the same two flags. They
are left out of Android and iOS builds, where the nap is an animation and
nothing more.

While it naps the prefab holds the animator parameter `WN/Asleep` at one
(`WirenookSignals.Asleep`), from an animation, so on every client and with
no synced bit. An avatar's own animator can declare it as a float and
close its eyes by it; where nothing naps it rests at nought.

The nap is one of three ways of being away, with the crash and the
vanishing of `net.wirenook.party-tricks`. The prefab is marked with its
flavour (`WirenookAfk`), and an avatar whose root names another
(`WirenookAfkChoice`) is built without it.

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

- **Crouching.** `GaitCycle` and `StandingCycle` both take a `crouched` flag,
  and the result is the same gait held low: the pelvis comes down by
  `CrouchDepth`, the hips go back and the chest forward by `CrouchLean` to
  stay over the feet, the knees part, the steps shorten to four fifths, and
  the pelvis is carried nearly level where an upright walker vaults over each
  straight leg. The hands are carried bent in front and swing half as far.
  The constants that are not style are in `Crouching`. Feet are planted and
  swung exactly as upright, so every guarantee about them still holds.
- **The air.** `AirCycle` is a held shape for each of three moments, with the
  limbs working a little so it is not a statue: going up (stretched, one knee
  ahead, toes pointed), coming down (legs gathering, arms out by `AirReach`)
  and a long drop (knees up and apart, arms about twice as wide, the chest
  tipped to see the ground). The pelvis stays at standing height and the legs
  are drawn up under it, because the avatar's root travels with the body.
- **Landing.** `LandingCycle` plays once: the knees take the fall by
  `LandingGive`, the hips go back and the chest forward, and the body stands
  up again. A hard landing sinks 2.6 times as deep, stays down a moment and
  takes 0.9 s where a soft one takes 0.4 s.

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
| `CrouchDepth` | fraction of leg length | 0.2 to 0.5 | 0.36 | How far the pelvis comes down in a crouch. Not `Crouch`, which only keeps upright knees from locking. |
| `CrouchLean` | degrees | 0 to 45 | 26 | How far the chest tips forward over the knees in a crouch; landings scale with it. |
| `AirReach` | degrees | 0 to 60 | 30 | How far the arms go out for balance while falling; a long drop about doubles it, and the chest tips with them. |
| `LandingGive` | fraction of leg length | 0.05 to 0.2 | 0.12 | How far the knees give on a soft landing; a hard one sinks 2.6 times as deep. |
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

### Styles

`GaitStyles.All` names six profiles to start from. Each is the default with a
handful of numbers changed, all inside the ranges above, and the tests hold
every one to the same guarantees as the default. They are what
`BakeStyles` puts behind the Walk style menu.

| Style | What it looks like | What it changes |
| --- | --- | --- |
| Weighty | The body sinks into each step and the upper body trails it. | Nothing: it is the default. |
| Light | Quick and upright; the feet come off the ground easily. | `Weight` 0.6, less loading drop, sway and lag, higher steps, more arm swing, a faster and smaller lean. |
| Sway | From the hips: feet nearly in one line, the pelvis swinging and the shoulders answering. | Pelvis sway, list and yaw up by half or more, `StepWidth` 0.25, more counter-rotation, arms held wider. |
| Bouncy | Springs off every step: high feet, a deep give at the knee, a run that leaves the ground. | `StepHeight` 0.11, `LoadingDrop` 0.035, `RunBounce` 1.35, `ToeOffAngle` 68, more arm swing, wider in the air. |
| Swagger | Long strides on a wide stance, toes out, arms held off the body. | `Weight` 1.35, `StrideScale` 1.1, `StepWidth` 1, `ToeOut` 13, more lag and pelvis yaw. |
| Dainty | Small neat steps on a narrow track, feet low, elbows in. | `StrideScale` 0.84, `StepWidth` 0.3, `ToeOut` 2, lower steps, a flatter foot roll, less arm swing. |

## Motions of your own

`CustomLocomotion` (`Editor/CustomLocomotion.cs`) is for a body that
should not walk like a person. It takes motions the caller wrote, as
`IPoseCycle`s that return a `GaitPose` (a pelvis, two feet, a chest, a
head and two arms, in the space the avatar's root moves through), and
does the rest of what the package does for its own walk: bakes them into
humanoid clips on a rig, lays them out over `VelocityX` and `VelocityZ`,
and puts them in the same state machine, with the same prefab to install
it. A set names a motion for standing, any number of moving ones with the
velocity each is for, the same crouched, three for the air and two
landings; optionally one for lying down (without it, VRChat's crawl, or
its still pose at any speed) and a fidget to lay over standing still at a
desktop.

What it leaves out is what makes the package's walk a person's: the gait
model, the cadence that rises with speed, the lean of the Additive layer,
and the checks that hold a bake to them. It keeps the checks that are true
of any motion: that sampled feet are where the motion put them, that
nothing goes through the ground, that a planted foot does not creep, that
`Upright`, `Grounded` and `VelocityY` move the state machine as VRChat's
default does. `net.wirenook.bot` in
[cass-avatar](https://github.com/CassidyPrather/cass-avatar) is the one
user so far.

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
- stroll at 0.7 m/s forward, backward, right, and the diagonals (0.5, 0.5) and
  (0.5, -0.5); amble at 1.2 m/s forward, backward and right;
- walk at 2 m/s forward, backward, right, and the diagonals (2, 2) and (2, -2);
- jog at 3 m/s forward and backward, and the diagonal (2, 3);
- run at 4 m/s forward, backward, right, and the diagonals (2, 4) and (2, -4);
- sprint at 6 m/s forward;
- crouched: an idle, a creep at 0.6 m/s forward, 1.25 m/s forward, backward,
  right and the diagonals (1.25, 1.25) and (1.25, -1.25), and 2 m/s forward;
- three loops for the air (rising, falling, a long drop) and two landings
  that play once;
- seven standing poses for the lean: neutral, forward, back, left, right, bank
  left, bank right.

The clips are stored inside the two generated controllers, about 16 MB of text
between them.

Leftward travel uses the rightward clips mirrored and offset half a cycle,
which the model's symmetry makes exact. The walk and run speeds are VRChat's
defaults, and (2, 4) is what a desktop player produces running diagonally,
because the strafe speed caps sideways motion.

A thumbstick produces every speed between, and a blend tree only gets the
speed right at its samples: between two of them it mixes their stride lengths
and their cadences each in a straight line, and the product of those is not
one. So the samples are closest where stride and cadence change fastest with
speed (below a walk) and where two different gaits meet (3 m/s, between a
walk and a run).

### Base controller

Parameters: `VelocityX`, `VelocityY`, `VelocityZ`, `Upright`, `Grounded`,
`Seated`, `AFK` (VRChat built-ins), `VRMode` declared as a float so it can
drive a blend tree, and `Wirenook/Locomotion/Cadence`, which the controller
computes.

- **Locomotion** has the states and thresholds of VRChat's default controller.
  Standing and Crouching are 2D freeform directional blend trees on
  `VelocityX` / `VelocityZ` with each clip at the velocity it was baked for.
  Prone is VRChat's tree of `proxy_*` clips. `Upright` switches between them
  (below 0.68 to crouch, below 0.41 to go prone, above 0.43 and 0.7 to come
  back). SmallHop and RestoreToHop play the rising shape, Short Fall the
  falling one, LongFall the long drop, QuickLand and HardLand the two
  landings, and RestoreTracking a still stand; they are driven by `Grounded`
  and `VelocityY` as in the default. The default's jump-and-fall sub-state
  machine is flattened into the layer.

  One thing differs from the default. It leaves RestoreTracking by an exit
  time of zero, which works because its clip has no length. A clip with a
  length would have passed any exit time while the state was still fading
  in, and would then hold a still pose for a whole loop; here that
  transition has a condition that is always true instead.
- With several styles (`BakeStyles`) every state's motion is a 1D blend tree
  over `Wirenook/Locomotion/Style`, declared as a float like `VRMode`, with
  one style at each whole number. The menu's parameter is an integer; VRChat
  converts a parameter to the type the animator declares, as its
  documentation of mismatched parameter types says. At a whole number such a tree plays exactly
  one child, so styles never mix; changing style switches without a fade.
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

`scripts/test.sh` runs 56 engine-free tests of the model: periodicity and
continuity, planted feet moving at exactly minus the root velocity, no sole
point below the ground, left and right half a cycle apart and mirrored, stride
over duration equal to the requested speed, double support when walking and
flight when running, legs always reaching their targets, direction handling,
the weight parameters' effects, and the IK solver on reachable, unreachable
and degenerate targets. The crouch is held to the same footing as the upright
walk, and to being lower, leveller and shorter-stepped; the air shapes to
lifted feet within reach and to growing wider from a hop to a long drop; the
landings to sinking once, early, and standing up again. Every named style is
run through all of it, tracked variant included, and checked against the
ranges the profile documents.

`LocomotionBuild.Verify()` checks the bake in the editor, without play mode:

- **Clips.** Every clip that stands on the ground (gaits upright and
  crouched, idles, landings) is sampled back onto a fresh copy of the rig at 60
  phases between its keys. On the shipped bake the worst ankle is 7.3 mm from
  where the model put it (limit 20 mm), the lowest heel or ball is 2.8 mm below
  the ground (limit 10 mm) and a flat foot creeps at most 5.1 mm over a stance
  (limit 10 mm). The air clips are held to the ankles alone. This is also what
  confirms the curve names and clip settings.
- **Controllers.** The Base and Additive controllers are run in a playable
  graph stacked as VRChat stacks them, with the built-in parameters driven by
  hand. Checked: the blend tree reproduces the model at baked velocities,
  including mirrored and VR sets (7.4 mm), and the crouched tree does the same
  below the crouching threshold (3.3 mm, hips at 63% of standing height);
  planted-foot speed is within 1.8% of ground speed at baked velocities and
  within 11% between them; the cadence
  multiplier is 1.00 inside the baked speeds and 2.00 at twice the fastest;
  posture follows `Upright`, and a jump, a short fall, a long drop and both
  landings follow `Grounded` and `VelocityY` and come back to Standing;
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
    graph;
  - how the crouch sits under a tracked head: the pose puts the hips at about
    63% of standing height, whatever height the player's head is really at,
    and whether the air shapes read at the speed a jump really goes by.
- Between baked velocities the blend tree mixes neighbouring gaits, whose
  stride lengths and cadences do not combine into exactly the requested speed.
  The planted foot is up to 11% off ground speed in the worst places measured,
  which are now the diagonals between baked ones: (1.4, 1.4), and (1, 3).
  Straight ahead, behind and sideways it is within 8%, worst at 1 m/s. Below
  0.7 m/s the idle is blended in and the steps are slower than the ground.
- The crouch, the air shapes and the landings are drawn, not derived: where
  the gaits follow from footfalls and the reach of the legs, these are poses
  with numbers chosen by eye on two rigs in the editor. Which air shape plays
  is decided by VRChat's default thresholds, so a jump shows the rising shape
  until it is falling faster than 2 m/s. Lying prone is still VRChat's crawl.
- Changing style with the Walk style menu switches at once, with no fade, and
  the lean and bank are the first style's whichever is chosen.
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

## Stepladder and nap: what was checked

Rebuild them with `Tools > Wirenook > Locomotion > Rebuild Stepladder` and
`Rebuild Nap`.

Neither has been run in the VRChat client. Checked in the editor: both
build onto an avatar through Modular Avatar and Avatar Optimizer with every
curve arriving at an object that exists; the nap lands in the Action layer
after VRChat's default layers with its weight control pointing at its own
pose layer; the nap pose was looked at on an avatar from three cameras at
four moments of its loop; `NapCycle` has three headless tests.

Not checked, because only a client can:

- That the ground contact's reading is the soles' height in metres. If it
  is scaled otherwise the ladder comes out at the wrong height or not at
  all; the two thresholds in `StepladderBuild` are the place to correct it.
- That an avatar's root stays on the ground while the play space is moved,
  which is what the whole idea rests on, and how `Grounded` behaves then.
- The constraints. Two weighted sources should put a step at their
  weighted average; `net.wirenook.world-sync` relies on the same thing and
  has not seen it in a client either.
- How the nap sits over VRChat's own AFK pose in the legs and feet, which
  come from both, and whether 0.25 s is long enough to fade out under
  VRChat's own blend back to tracking.
- The nap on the menu's switch alone, with the wearer still tracked: that
  taking the body from tracking every two seconds holds it, and that
  nothing shows at each taking.

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
3. Push a tag named for that version (`v` and the number) — CI makes the GitHub release and attaches the `.zip`
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
reference the SDK's `proxy_low_crawl_*` clips, for lying prone, and its
`VRCAnimatorTrackingControl` behaviour, which every project with the SDK
already has.

Vendored third-party material is inventoried in [CREDITS.md](CREDITS.md).
