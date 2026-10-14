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

Offer two feasible missions, then let each pair choose. If a pair disagrees, briefly preview each, then agree one mission for today and reserve the other for next time. Offer each child a private choice before discussion; do not let a confident partner choose every time. Let them choose the next prediction, who operates first, and whether to draw, explain or act out the answer. They may ask to repeat, slow down or take a movement break.

A learner may say “I do not know yet” and still operate the experiment; ask them to notice one change and revise their explanation afterward. Hand out only the chosen activity page and needed cards rather than requiring the entire workbook at once.

The adult handles installation, programming, safe board connections and recovery. The adult also preserves the experiment's rules: choosing manual clock presses or a receiver pause is allowed; claiming an unobserved result is not. Ask “What could we try to find out?” before supplying an explanation.

## A proposed first session of 25 to 30 minutes

Choose one mission and one goal. Use Chapters 2 or 4 from [the learner chapters](LEARNER_CHAPTERS.md). Chapter 1 is an optional simpler input experiment; Chapters 3 and 5–8 are later activities, not a first-session checklist. Binary and waveforms are optional branches. Give each learner the chosen page, not the full teacher guide.

| Time | Learner decision/action | Adult's job |
|---|---|---|
| 0–3 min | Choose controller pause or direct card delivery. | Show a working example and one recovery action. |
| 3–8 min | Try the relevant controls or cards. | Model one test and its immediate result. |
| 8–20 min | Each child chooses a change and operates its short test. Swap after each test. | Help only as needed; record the unaided attempt and response to help. |
| 20–25 min | Each demonstrates one discovery for their chosen mission. | Use the mission-specific observation below. |
| 25–28 min | Give feedback separately; choose what to try next time. | Record enjoyment separately from understanding. |

These are trial allocations, not measured attention spans. Stop earlier when needed. Offer up to ten more minutes only if both children want to continue; another chapter is never required to complete today's mission.

## Mission A: you are the controller

Use [the offline Clock Lab](interactive/SharpHDL_Learning_Lab.html), **Hockey drill controller** tab. This evaluates the exported hardware graph in JavaScript; it is distinct from running the Verilog in WASM.

1. The guide presses **Reset edge**, turns **Stop off**, turns **Start** and **Practice enabled** on, and presses **Clock +1**. Entering Running starts at zero ticks. Turn Start off before handing over. Reset clears registers but leaves checkbox selections unchanged.
2. With Start already off, let the child choose when to turn Practice enabled off and back on. Predict before each Clock press.
3. Offer a challenge: “Can you make time pass without adding a tick?” Stop at this pause challenge for the first session; reaching Done is Chapter 3.
4. Change roles. The new operator chooses a different pause pattern.

An input checkbox alone is not a clock edge. Four following enabled edges reach Done. Inspect the visible state and history rather than assuming every click increments the count. Treat Stop as a later prediction to investigate, not an extra rule to memorize at the beginning. Do not use Run slowly for this first test. Pause animation stops automatic clock presses; turning Practice enabled off allows manual clocks while holding the count.

With Start off, the edges after completion show **Done/4 → Ready/4 → Ready/0**. Both the state and counter use the old state when computing their next values. Chapter 3 deliberately explores this; Chapter 2 stays in Running and is restarted between learners.

## Mission B first session with direct delivery

Use Chapter 4 with cards 10, 11 and 12, Offer/Valid and Can take/Ready cards. No buffer slots are needed today. Sender offers 10 with Valid on; receiver chooses one or two waiting edges with Ready off, then Ready on. Keep the card and Valid stable while waiting. Only a clock edge with both Valid and Ready on transfers a card. Offer the next card for a later edge. After the receiver takes card 12, turn Valid off and keep Ready on for one more edge to check nothing extra arrives.

A third participant calls the clock; with two children the adult can do this. Offer a seated silent version using a printed upward-arrow clock card. Use words and shapes as well as color. Swap sender and receiver after one short test. Each learner can demonstrate a wait and a transfer; do not require buffer accounting yet.

## Later chapters with a buffer

Introduce Chapter 5 only after direct delivery is comfortable. The adult manages Head (next to leave) and Spill (extra place), initially empty. Sender offers 10 with Valid on; receiver Ready off. Children choose three or four waiting edges, then Ready stays on. Mark input and output decisions from the state before the edge, then move cards together. After card 12 is accepted at the buffer input, turn sender Valid off. Keep receiver Ready on through an extra empty edge after delivery.

The input is Ready when fewer than two cards are stored before the edge. Output is Valid when Head contains a card. On output transfer remove Head, moving Spill forward if present. On input transfer put the arriving card at the tail after those removals. This ordering describes the simultaneous result; it does not permit an input when the old state was full. A newly arriving card cannot depart at that same edge.

For a three-edge receiver wait, this is the complete teacher reference. Slot values below are after the edge. Input acceptance and output delivery are decisions made before it.

| Edge | Receiver Ready | Input accepted | Output delivered | Head after | Spill after | Accepted total | Delivered total |
|---|---|---|---|---|---|---|---|
| 1 | 0 | 10 | none | 10 | empty | 1 | 0 |
| 2 | 0 | 11 | none | 10 | 11 | 2 | 0 |
| 3 | 0 | none | none | 10 | 11 | 2 | 0 |
| 4 | 1 | none | 10 | 11 | empty | 2 | 1 |
| 5 | 1 | 12 | 11 | 12 | empty | 3 | 2 |
| 6 | 1 | none | 12 | empty | empty | 3 | 3 |
| 7 | 1 | none | none | empty | empty | 3 | 3 |

At edge 5, delivering 11 and accepting 12 happen together. With a four-edge wait, insert one extra full waiting edge after edge 3; delivery begins at edge 5 and finishes at 7. The bundled browser bench uses the three-edge pause, not the child's new pause pattern.

Chapter 6 adds the ledger. After each edge without reset, check **accepted = delivered + stored**, with stored between zero and two. Track the delivered numbers too: totals alone cannot detect reordering. Compare direct and buffered experiments with the same offered list and receiver pause but independent sender progress. The full buffer refuses input on its first draining edge; that behavior belongs to this particular registered design, not every possible buffer.

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

Observe the chosen mission only. A controller learner demonstrates a count and a hold while Running, distinguishing an input change from a clock. A direct-delivery learner demonstrates one transfer and one wait with the offered card held. Do not require both missions. Did each child choose and operate a short test and recover after a wrong prediction? They may answer with actions instead of technical vocabulary.

| Session record | Fill in after observation |
|---|---|
| Mission chosen and order | |
| Setup time / activity time / unplanned waiting | |
| Each learner's independent turn and choice | |
| Exact confusing action or instruction | |
| Help given and whether it worked | |
| Unaided attempt and prediction (or “not sure”) | |
| Control-finding difficulty versus circuit misconception | |
| What happened after help | |
| Independent retry with a changed input or wait | |
| Enjoyment or requested repeat (separate from understanding) | |
| Demonstrated understanding for chosen mission | |
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

## Chapter requirements

| ID | Requirement | Verification boundary |
|---|---|---|
| EDU-CH-01 | Controller setup works after any checkbox selections; reset preserves them. | Browser-control check and teacher station rehearsal. |
| EDU-CH-02 | Separate input changes, disabled clocks, enabled clocks and completion behavior. | Graph/control checks; actual learner explanation still to observe. |
| EDU-CH-03 | Model empty/full buffer and simultaneous acceptance/delivery using pre-edge state. | Teacher table matches prepared trace; card enactment needs a pilot. |
| EDU-CH-04 | Eight-row ledger accommodates both pause choices; check order and conservation. | Reference row review; child independent retry still to observe. |
| EDU-CH-05 | Each chapter states a goal and demonstration; route respects prerequisites. | Editorial review; actual time and engagement unmeasured. |
| EDU-CH-06 | Prepared waveform exploration starts at a defined event and never implies editable stimulus. | Trace/control tests and documented navigation. |
