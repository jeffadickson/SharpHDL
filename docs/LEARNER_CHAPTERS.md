# Choose your hardware adventure

A short route through the SharpHDL workbook for learners around nine. Choose one ready-to-try chapter for today with your guide. You do not need to finish the whole book or learn binary before trying a circuit. A grown-up prepares the experiment. You choose a question and get a turn at the controls.

Start with Chapter 1, 2, 4 or 7. Chapter 3 follows 2; Chapter 5 follows 4; Chapter 6 follows 5; Chapter 8 follows 2. Pick by what you can already show, not by speed.

You can say “I do not know yet.” Try, notice, and change your prediction. You may explain by talking, drawing, pointing or moving cards. You may ask to repeat, slow down or stop.

## Chapter 1 Make a change

**Your mission:** find which number controls the answer while B stays zero.

**You need:** the Clock Lab, opened by your guide on “Where did the loop go?” Choose “for · 12 additions.” Your guide starts A at 2 and B at 0. This screen also shows code about repeating additions. Today, leave that explanation to your guide and look just at A, B and Output.

1. Notice the answer, 2.
2. Choose a new whole number for A between 0 and 9. Keep B at 0. Predict or say what you notice.
3. Change A. Does the answer change without a Clock press?
4. Swap roles. Your partner chooses another A.

**Show me:** point to the input you changed and the answer it affected.

**Your next question:** What if A is zero? What if we return to the first number? Choose one or invent another test within 0 to 9.

**Keep this discovery:** this circuit responds to its inputs without waiting for a clock. The guide will explain loops in a later lesson.

## Chapter 2 Wait for the clock

**Your mission:** discover a circuit that waits before changing its stored state.

**You need:** the Clock Lab, “Hockey drill controller.” You may call it a robot drill instead. Your guide prepares Running with zero completed ticks. Use only “Practice enabled” and “Clock +1” today.

1. Turn Practice enabled on. Do not press Clock yet. Notice the count.
2. Predict what one Clock press will do, then press Clock +1.
3. Turn Practice enabled off. Press Clock +1 twice. Does the count change?
4. Turn it on again. Press Clock +1 once.

**Show me:** make one clock edge that changes the count and one that keeps it still. Ask your guide to restart before your partner takes a turn.

**Your next question:** choose one, two or three disabled clocks. Will waiting longer change the count?

**Keep this discovery:** while this drill is Running, an enabled clock edge adds one. A disabled clock edge keeps the count still.

## Chapter 3 Finish your drill

**Your mission:** reach Done and find out what the next clock does.

**You need:** the controller and your guide's fresh Running at zero setup. Keep Start off and Stop off. Use manual Clock presses.

1. Turn Practice enabled on. Press Clock +1 once. Read the count and state.
2. Continue one edge at a time. Stop pressing when the state becomes Done.
3. Draw or point to the count at Done.
4. Predict the next two states, then press Clock +1 twice, looking after each edge.

| Moment | State I saw | Count I saw |
|---|---|---|
| Finished | | |
| One more edge | | |
| Another edge | | |

**Show me:** find Done again after your guide restarts. It takes four enabled edges after entering Running.

**Your next question:** on another run, choose one pause before finishing. Does the pause add a practice tick?

## Chapter 4 Pass a parcel

**Your mission:** deliver cards in order without losing or copying one.

**You need:** cards 10, 11, 12; an Offer card (Valid); a Can take card (Ready); a clock caller. Sit at a table if you prefer. The caller may turn a printed upward-arrow card instead of clapping. The arrow marks one clock edge.

1. The sender offers 10 and shows Valid. The receiver chooses Wait for one or two edges, so Ready is off.
2. At each edge, transfer only if Valid and Ready are both on. Otherwise keep an offered card still.
3. The receiver turns Ready on. At the next edge take 10. The sender offers 11 for a later edge, then 12.
4. After the receiver takes card 12, turn Valid off. Keep Ready on for one extra edge: nothing should arrive. Swap sender and receiver, choose a new wait, and repeat.

| Offered now | Can take now | At the edge |
|---|---|---|
| No | No or yes | No parcel moves |
| Yes | No | Hold the same parcel |
| Yes | Yes | Transfer one parcel |

**Show me:** one transfer and one wait. Can take without an offer must not invent a parcel.

**Your next question:** does a longer wait change the parcel's number? Try it.

## Chapter 5 Add two waiting places

**Your mission:** find out how two storage places help a sender during a short wait.

**You need:** the same cards; two places labeled Head (next to leave) and Spill (extra place). Your guide manages these places at first. Begin empty; offer 10; receiver Ready off.

1. Choose three or four waiting edges, then leave the receiver Ready on.
2. Before each edge, your guide marks which cards may enter and leave. At the edge, make both agreed moves together.
3. Stop after the second waiting edge. **Show me:** point to the next card to leave and the card still waiting with the sender.
4. Turn sender Valid off as soon as 12 enters the buffer. Continue until all three arrive; keep receiver Ready on for an extra empty edge. Then swap roles and repeat. A full buffer refuses another card until its stored state shows room.

**Your next question:** try the same wait without the two places. Which sender can hand over more cards before the receiver starts?

**For your guide:** the exact move rules and a worked table are in docs/LEARNER_LED_PILOT.md, under “Later chapters with a buffer.”

**Keep this discovery:** storage can absorb a short wait. It cannot make the receiver work faster. A newly accepted card cannot also leave an empty registered buffer at that same edge.

## Chapter 6 Be the parcel detective

**Your mission:** prove that every accepted parcel is accounted for.

**You need:** the buffered card activity, starting empty with counters at zero. Do not reset during this experiment. Let your guide manage the buffer while you keep the ledger. You may point or say the numbers and ask your guide to write them, or use counters to show each row.

1. Count a parcel as accepted when it enters the buffer.
2. Count a parcel as delivered when it leaves the buffer.
3. After every edge, count the cards still stored. Write down each delivered number in order.
4. Check: accepted = delivered + stored. Stored must be 0, 1 or 2.

| After edge | Accepted total | Delivered total | Still stored | Delivered number this edge |
|---|---|---|---|---|
| 1 | | | | |
| 2 | | | | |
| 3 | | | | |
| 4 | | | | |
| 5 | | | | |
| 6 | | | | |
| 7 | | | | |
| 8 | | | | |

These eight rows cover either pause choice and an extra empty check. A card still with the sender has not been accepted.

**Show me:** explain one row using actual cards. Check the delivery list is 10, 11, 12 as well as checking the totals.

**Your next question:** have your guide make a pretend ledger mistake. Can your cards reveal it?

## Chapter 7 Numbers made of lights

**Your mission:** show a number with four on-or-off cards.

**You need:** cards labeled 8, 4, 2, 1. Keep the labels visible. Mark each ON or OFF using words, not color alone.

1. Choose a number from 0 to 15. Turn ON the cards whose values add to your number.
2. Your partner adds the ON values. Do you agree?
3. Record ON as 1 and OFF as 0, in the order 8, 4, 2, 1.
4. Swap roles with a different number.

| Number | ON cards | Pattern |
|---|---|---|
| 5 | 4 and 1 | 0101 |
| 9 | 8 and 1 | 1001 |
| Your choice | | |

**Show me:** make a number and explain why its cards add up correctly.

**Your next question:** what is the biggest number these four cards can make? What new card would we need for 16?

**Keep this discovery:** binary uses the same numbers, written with place values that double. A display may use A for ten through F for fifteen; ask for the display key if you use the board.

## Chapter 8 Read a circuit recording

**Your mission:** find one count and one pause in a finished waveform recording.

**You need:** the WASM workbook lab, “Count, pause, resume,” opened by your guide. Begin with its recorded trace. Your guide can run the prepared RTL test to produce a fresh trace.

1. Press First event, then Next rising edge. This first edge at 2 ns resets the count to zero. Your guide points out reset. After that, watch clk, s_enable and s_value.
2. Use Next rising edge to visit enabled edges at 6, 10 and 14 ns. Watch the count reach 1, 2 and 3.
3. Visit 18 and 22 ns. Enable is off. What stays the same?
4. Visit 26 ns. Enable is on again. What happens?

**Show me:** choose an edge and point to evidence that it counted or held.

**Your next question:** choose a different edge for your partner to explain. If you prefer a new input experiment, return to Chapter 2.

**Keep this discovery:** moving the cursor explores a finished recording. It does not clock a live circuit or change the prepared inputs. The time labels arrange this test; they do not measure an FPGA's speed.
