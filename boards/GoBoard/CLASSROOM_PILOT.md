# Teacher station acceptance checklist

The software build has passed synthesis, placement, routing and simulation.
**No physical station has been accepted yet.** This checklist is a record to
complete on actual hardware, not a claim that programming or USB works here.
A simulation-only session can proceed without completing the hardware section.

## Prepare the simulation session

1. Use a laptop with a keyboard and a browser. Keep the repository folders together.
2. Follow `WASM_WORKBOOK.md` to download the verified engine and start the HTTP server.
3. Run the counter and skid experiments. Check the fresh-simulation message, not
   just a recorded trace. Disconnect the internet and repeat with the downloaded engine.
4. On each learner device, open the teacher's LAN address instead of localhost.
   Check school network restrictions before the session. Keep the recorded traces
   available as a fallback and label them as replay.

## Accept one physical station before planning a board class

Record the board revision, OS/version, USB cable, tool versions and operator/date.
Use the board maker's programming instructions for that revision. Do not guess a
programmer type or cable/interface setting. Add the **exact command or GUI steps
that worked** to this record after testing; no universal flashing command has
been validated by this project.

| Check | Observation to record | Status |
|---|---|---|
| Device enumeration | Expected device visible; data-capable USB cable | Not tested |
| Programming | Exact loader, version, command/options and successful completion | Not tested |
| Startup | Known-good add-two image: arithmetic display B=0 and A=2 | Not tested |
| Button 2 | One press increments B once; long hold adds no repeats | Not tested |
| Clear | Button 1 restores B/count; run/mode stay unchanged | Not tested |
| Run/pause | Button 3 changes running; button 4 selects counter view | Not tested |
| Display | Check 0..9 and A..F; four LEDs match B in arithmetic mode | Not tested |
| Power cycle | Startup returns to known state; record whether reprogramming is needed | Not tested |
| Recovery | Restore known-good source and program its rebuilt image | Not tested |
| Second station | Repeat with the learner laptop/cable to catch environment assumptions | Not tested |

Keep the known-good bitstream and its manifest (`generated/goboard`) together.
After Lesson 3's add-three experiment, restore the source constant to 2, run
`boards/GoBoard/lesson.sh 2` with the documented SDK/tool setup, and **program the
resulting image**. Building alone does not change the board. Before Lesson 4,
confirm B=3 gives A=5.

## Pilot the teaching sequence

Try Lessons 1, 3 and 5, then the counter waveform and Ready/Valid card activity.
Record actual setup time, activity time, prompts needed and misconceptions.
Have the learner explain why a held card is not transferred twice and why changing
an input does not require rebuilding. Use this evidence to set session lengths.
Do not treat suggested lesson durations as measured classroom results.

For a child-directed first session, use the [learner-led pilot guide](../../docs/LEARNER_LED_PILOT.md).
It includes an explicitly simulated focus-group critique, feasible learner choices,
and an observation sheet for an actual nine-year-old pilot. No child trial is claimed.
