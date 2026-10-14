# Five FPGA design and instruction review rounds

These are five additional, independent AI role-based review rounds requested
by the project owner after the initial review. The same design reviewer and
instructor re-examined successive revisions. They are not external human
certifications or evidence of a physical classroom trial.

| Round | Design review and revision | Instruction review and revision |
|---|---|---|
| 1 | Fixed AXI keyword/clock/internal-name handling; reviewer exposed the additional `s_always` core keyword defect. | Corrected the Lesson 3 to 4 add-two restoration sequence; added an explicitly unperformed physical-station checklist. |
| 2 | Fixed strong-property keyword escaping; full core regression passed. Checked new edge selection, ns conversion and pre-edge transfer semantics. | Accepted rising-edge navigation, readable time units and pilot checklist; refined edge-count and no-transfer wording. |
| 3 | Reviewed the guided one/two-point shot counter and all seven browser transfer summaries; requested a held-button reset-release test. | Added backward Ready arrows, handshake table and independent prediction exercise; found page overflow, missing Release row and command prerequisites. |
| 4 | Added reset-release/hold checks and all keyword/collision cases; independent Icarus reruns passed. | Fixed sequence and prerequisites; identified an overflowed blank page, then fixed the transition with heading page-break-before. |
| 5 | Final regenerated artifacts and regression evidence reviewed before commit. | Final rendered workbook and residual teaching limitations reviewed before commit. |

## Requirements added or strengthened

- AXI-NAME-01/02 in `STREAMS_AND_MATH.md`: valid HDL identifiers, explicit
  clock/reset reservations, and collision-safe generated names.
- EDU-WAVE-01/02 in `WASM_WORKBOOK.md`: rising-edge controls, reset-aware counts,
  readable time labels and sampled transfers separated from settled values.
- EDU-SHOT-01/02/03 in `examples/LearningLab/SHOT_COUNTER.md`: a working small
  template, directed behavior tests, and explicit physical-integration limits.
- Workbook regression checks cover restoration instructions and new teaching
  sections as well as all seven experiment links.

## Remaining boundaries

The shot-counter extension is a tested simulation template, not a wired Go Board
feature or an additional WASM experiment. A dedicated learner edit/run interface
would make it more independent; current use is teacher-led. Native and WASM
Icarus agreement establishes integration consistency, not independent proof.
Expected-value models and scoreboards provide separate functional checks.

Physical USB/programming, switch behavior, actual display operation, second-station
acceptance, and a child-observed teaching pilot remain unperformed. Complete
`boards/GoBoard/CLASSROOM_PILOT.md` before advertising a turnkey hardware class.
Formal stream proofs remain a future engineering improvement, not a claim of
this release. Suggested session lengths have not been validated with children.

## Final disposition

Both reviewers accepted round 5 with no remaining release blocker in the reviewed
scope. The final workbook has 30 pages. The complete automated suite passed;
`generated/audit/run-all.txt` records every stage. After the declaration-formatting
cleanup, core/F# outputs were regenerated and affected wrapper compilation rechecked.

Three synthesis logs appeared incomplete during the final artifact audit despite
passing stage results. The affected TinyRisc, multiplier and divider stages were
rerun; each completed successfully, produced complete backend logs and passed
its post-synthesis simulation. No claim relies on the incomplete copies.

## Subsequent learner course review

The 30-page count and full-suite result above describe that completed release.
Later learner-led chapter additions and their separate five review rounds are
recorded in [the learner review log](docs/LEARNER_REVIEW_ITERATIONS.md), including
their own validation limits.
