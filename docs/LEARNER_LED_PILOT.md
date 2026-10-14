# Let the learners steer: a nine-year-old pilot

**Status: proposed teaching experiment, informed by an AI-simulated focus group. No children have participated yet.** These are hypotheses to test, not child testimony or measured learning outcomes. This supplement offers a different route through the existing workbook; it does not replace its technical explanations or accepted station checks.

## What the simulated group challenged

| Plausible learner perspective | Possible friction to observe | Change to try |
|---|---|---|
| Hands-on tinkerer | Wants to press something before hearing about binary | Start with a working experiment; introduce bits when they explain a surprise. |
| Cautious beginner | Public predictions feel like being graded; partner takes the controls | Allow pointing, drawing or a private guess; give each child a protected turn. |
| Story/game builder | Wants a purpose for the circuit and a choice of theme | Let the pair name their mission: hockey drill, robot exercise or parcel delivery. The circuit rules stay the same. |

These perspectives are design prompts, not fixed categories for children. Do not assign children labels or assume every nine-year-old likes the same activities.

## The children control the questions

Offer two feasible missions, then let each pair choose. If a pair disagrees, give both missions a short turn rather than taking a class-wide majority vote. Let them choose the next prediction, who operates first, and whether to draw, explain or act out the answer. They may ask to repeat, slow down or take a movement break.

A learner may say “I do not know yet” and still operate the experiment; ask them to notice one change and revise their explanation afterward. Hand out only the chosen activity page and needed cards rather than requiring the entire workbook at once.

The adult handles installation, programming, safe board connections and recovery. The adult also preserves the experiment's rules: changing a clock rate or pausing a receiver is allowed; claiming an unobserved result is not. Ask “What could we try to find out?” before supplying an explanation.

## A proposed 40-minute first session

Times are trial allocations, not measured attention spans. Shorten or split the session when observation calls for it.

| Time | Learner decision/action | Adult's job |
|---|---|---|
| 0–4 min | Watch one working example. Pick controller or card-delivery mission. | Show only the controls needed; avoid starting with a vocabulary lecture. |
| 4–12 min | Choose a prediction, run it, and try a second idea. Swap operator and predictor after one experiment. | Ask what changed and what stayed the same. Record help needed. |
| 12–16 min | Act out the circuit: be a stored number or pass a card on a clap. | Introduce “clock edge” and “remember” at the moment they matter. |
| 16–24 min | Choose a challenge below. Invent a test that could reveal a mistake. | Keep the mechanism accurate; model how to recover from a wrong guess. |
| 24–29 min | Use four number cards to make a number of their choice. | Introduce 8, 4, 2, 1 only as needed; decimal remains alongside binary. |
| 29–35 min | Investigate one prepared waveform or repeat the hands-on experiment. | Clearly distinguish a completed trace from an interactive circuit. |
| 35–40 min | Teach the partner one discovery. Choose what to do next time. | Collect feedback privately before discussing it as a group. |

## Mission A: you are the controller

Use [the offline Clock Lab](interactive/SharpHDL_Learning_Lab.html), **Hockey drill controller** tab. This evaluates the exported hardware graph in JavaScript; it is distinct from running the Verilog in WASM.

1. Reset. Select Start and Enable, then press Clock. Notice that entering Running starts at zero ticks.
2. Release Start. Let the child choose when to turn Enable off and back on. Predict before each Clock press.
3. Offer a challenge: “Can you make time pass without adding a tick?” or “Can you reach Done?”
4. Change roles. The new operator chooses a different pause pattern.

An input checkbox alone is not a clock edge. Four following enabled edges reach Done. Inspect the visible state and history rather than assuming every click increments the count. Treat Stop as a later prediction to investigate, not an extra rule to memorize at the beginning.

## Mission B: can you deliver every parcel?

Use number cards 10, 11 and 12, Ready/Valid cards and two marked waiting places. Children choose the story and a receiver pause of three or four claps. A third participant calls the clock; with two children the adult can do this.

- First try a direct connection. The sender holds its offered card while the receiver waits.
- Then try the registered two-place skid buffer. Decide transfers from the state **before** each clap; update the slots after the clap.
- At each interface, transfer only when Valid and Ready are both on at the clap. An accepted input is not necessarily a delivered output.
- If the buffer is full before the clap, it cannot accept a new card on that clap, even if its first card leaves. That is the behavior of this specific buffer.
- Challenge: “Can we finish with every card delivered exactly once?” Try Ready without Valid too: no card is transferred.

Use the workbook's worked three-clap table and four-clap practice answers to check the acting. The children choose the pause in the **card activity**. The prepared browser stream tests always use the existing three-edge pause unless the teacher changes and rebuilds the bench.

## What can learners actually control today?

| Resource | Real learner control | Boundary to explain |
|---|---|---|
| Clock Lab | Inputs and explicit Clock presses; other tabs offer numeric inputs | Graph simulation, not the WASM engine or physical FPGA. |
| Ready/Valid cards | Pause pattern, offered cards, role and story | Follow the documented registered-buffer rules. |
| WASM workbook lab | Select experiment, run RTL, navigate rising edges and inspect signals | Prepared input sequence; cursor navigation replays a completed run. No free-input editor. |
| Accepted Go Board station | Physical buttons for the installed adder/counter design | Adult validates/programs the station first. Changing the circuit needs rebuilding and programming. |
| Shot-counter template | Predict one-point and two-point configurations with a guide | Native teacher-run example; not a selectable WASM lesson or installed board image. |

When using the board, put the 0–15 display key beside it before the first press. The four-bit board adder wraps at 16; the eight-bit browser add-two example wraps at 256. They illustrate the same idea at different widths, not the same numeric range.

Avoid promising “choose any inputs and run them” in the WASM lab. A future learner-editable test interface would need its own implementation and verification.

## Let actual children overrule this plan

Run a small supervised pilot before calling the class suitable for nine-year-olds. Ask each child separately, with no preferred answer:

1. What would you like to do again?
2. Where did you want help or want to stop?
3. Which thing should we remove or change?
4. What would you build next?

Observe rather than infer: did both children operate, choose an experiment, recover after a wrong prediction, and distinguish an input change from a clock? Can each show one correct transfer and one wait using the cards? They may answer with actions instead of technical vocabulary.

| Session record | Fill in after observation |
|---|---|
| Mission chosen and order | |
| Setup time / activity time / unplanned waiting | |
| Each learner's independent turn and choice | |
| Exact confusing action or instruction | |
| Help given and whether it worked | |
| Demonstrated understanding (specific action) | |
| Learner's requested change | |
| Next session change and reason | |

Record anonymous observations, not invented quotations or scores. If one child never gets the controls, change turn-taking before adding content. If the waveform view requires continuous adult navigation, return to acting and introduce only one signal next time. If explanation is crowding out experiments, move terminology to the teacher notes. Decide the next lesson from these observations rather than from the AI personas.

## Requirements and acceptance evidence

| ID | Requirement | Current evidence / remaining check |
|---|---|---|
| EDU-AGENCY-01 | Every learner chooses a feasible mission or test and has an operating turn. | Choices and turn swaps specified here; observe in a real pilot. |
| EDU-AGENCY-02 | Predictions accept drawing, pointing, acting or speech; no forced public ranking. | Facilitator procedure specified; observe its use. |
| EDU-AGENCY-03 | Advertised choices match available simulator and hardware controls. | Cross-checked against Clock Lab, WASM guide and station checklist. |
| EDU-AGENCY-04 | Simulated feedback is labeled and never reported as child research. | Explicit status and hypothesis labels in this supplement. |
| EDU-AGENCY-05 | Actual learner feedback changes the next session plan. | Blank observation record supplied; not yet performed. |

Teacher preparation: [station acceptance](../boards/GoBoard/CLASSROOM_PILOT.md) and [WASM setup](../WASM_WORKBOOK.md). Physical station acceptance and actual learner trials remain unperformed.
