# MateAnimatorUpgrade
Tool to upgrade the old MateAnimator (as of 6-29-18) format to the new one (newSerialization)

To minimize issues, use branch "newSerialization". Once you complete the conversions, you can then switch to the "master" branch.

## How To
* Remove the previous MateAnimator
* Grab MateAnimator ("master" or "newSerialization")
* Grab MateAnimatorUpgrade
* There should be a new menu item: M8/Animator Convert Old
* Delete MateAnimatorUpgrade once you are satisfied with the entire process

## Things to Consider
* This tool won't be able to convert Animator (AnimatorData) fields in the scripts, so you will have to rehook these up.
* TriggerTracks are converted to EventTracks. The conversion tool will generate a TriggerSignal asset as a substitute for hooking up.
* EventTracks will only convert the first key's Component reference, so other keys in the EventTrack that reference a different Component will be removed.
