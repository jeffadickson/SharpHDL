# Five learner course improvement rounds

Scope: five additional review and revision rounds following the simulated nine-year-old focus group. The FPGA design reviewer and FPGA instructor are independent AI role-based reviewers, not human participants. No children have taken this course in this session. Their feedback is a hypothesis about usability, not research evidence.

The existing technical workbook and compiler were already reviewed. These rounds focus on the learner-led route, chapter structure, teaching accuracy and available simulator controls.

## Round 1 Structure and repeatability

Both reviewers requested a 25–30 minute first session with one mission. The design reviewer required a reproducible Reset/Stop/Start sequence and precise simultaneous buffer moves. The instructor requested short learner pages and separate teacher setup/evidence.

Implemented eight learner chapters, an explicit short first-session route, controller startup correction, accessibility alternatives and mission-specific observations. Preserved the longer technical lessons as later resources rather than first-session requirements.

## Round 2 Match the instructions to the activity

Reviewers found direct-delivery instructions still introduced the buffer prematurely, a four-row ledger could not finish the experiment, and chapters lacked prerequisite routing. The design reviewer asked for a worked buffer state table and deterministic waveform navigation. The instructor requested honest framing for the input-only experiment on the existing loop screen.

Split direct and buffered missions; supplied the seven-edge buffer table, eight ledger rows, chapter prerequisites and First event navigation. Added requirements EDU-CH-01 through EDU-CH-06 and a teacher Clock Lab rehearsal. Clarified UI Reset and pause wording. While inspecting the actual companion, corrected an unrelated fixed-point example typo from 0.135 to 0.125; no arithmetic implementation changed.

## Round 3 Remove ambiguity during enactment

The instructor found the buffer checkpoint came after all cards had departed, partner-disagreement advice contradicted the single-mission limit, and handwriting was implicitly required for the ledger. The designer found the sender was never explicitly told to lower Valid after its last card, risking duplicate-transfer enactment.

Moved the checkpoint to the second waiting edge, kept one agreed mission per session, allowed a guide to scribe, explicitly lowered Valid after final acceptance, and added an extra empty edge. Replaced a vague clock-rate choice with the actual manual-clock control.

## Round 4 Confirm the complete instructions

Both reviewers accepted the revised mechanics and teaching sequence with a small ambiguity: “After 12 transfers” could mean twelve events rather than card 12. Changed it to “After the receiver takes card 12.” The instructor also required the printed teacher schedule to retain private feedback and a next-session choice, with optional repetition outside the core time budget. The design reviewer inspected the new browser-control assertions and found their expected behavior matched the implemented controller.

The workbook builder imports the chapter source directly and preserves the illustrated technical lessons. A full rendered-page pass identified an orphaned experiment link in the older Clock Lab section; its illustration sizing was adjusted before the final render.

## Round 5 Final acceptance and evidence

The FPGA design reviewer approved the documentation/prototype scope with no further technical correction and independently reran the 168 graph-route assertions. The instructor approved the material for a supervised pilot after inspecting rendered pages 2–10 (route and all eight learner chapters) and 23–24 (teacher plan and preparation). Both retained the explicit limits below.

The final workbook has 40 pages. The builder visually inspected every page and checked final changed pages again; unchanged pages were confirmed by image hashes against the inspected render. No blank pages, clipped content, orphan links or broken tables remained. The short route is imported from one Markdown source, preserving the longer illustrated lessons and technical references.

## Validation and limits

Scoped results are recorded in `generated/audit/learner-review.txt`:

- 168 graph-only learner-route assertions passed; generated JavaScript parses.
- 11,139 existing graph/reference comparisons passed.
- Seven prepared WASM workbook fixtures, expected values, trace timing and encoded VeriSim payload checks passed. These were fixture checks, not fresh WASM executions this round.
- Generated HDL/document excerpt checks, board hashes and existing workbook links passed. All eight new chapter headings were separately confirmed in the DOCX, and changed local Markdown links resolved.
- Shell syntax and diff whitespace checks passed.

A new actual-browser test covers checkbox preservation, automatic-clock cancellation, startup from all eight input combinations, count/hold/completion and immediate combinational outputs. **It did not execute successfully:** Chromium failed before page load because the environment denied a required socket. The test remains a required `--browser` stage in the full runner. Graph checks do not establish UI acceptance. The full suite was not rerun for this documentation revision; do not carry forward the previous release's full-suite pass as a result for this one.

Before using the controller with learners, rehearse its controls on the actual station. The direct card activity does not depend on a browser. Automated trace or browser checks cannot establish classroom timing, comprehension or enjoyment. Physical board acceptance, actual school-device rehearsal and a supervised learner pilot remain unperformed.
