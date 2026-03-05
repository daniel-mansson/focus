---
status: diagnosed
trigger: "Shrink directions feel inverted"
created: 2026-03-02T00:00:00Z
updated: 2026-03-02T00:00:00Z
---

## Current Focus

hypothesis: ComputeShrink uses the wrong mental model — it moves the edge NAMED by the direction inward, but the user expects the direction key to indicate WHICH SIDE contracts (opposite edge moves)
test: Traced exact code path for "up" direction
expecting: Root cause confirmed by code reading
next_action: Return diagnosis to caller

## Symptoms

expected: Pressing Up in Shrink mode moves the BOTTOM edge upward (window gets shorter from the bottom)
actual: Pressing Up in Shrink mode moves the TOP edge downward (window gets shorter from the top)
errors: none — silent wrong behavior
reproduction: Activate Shrink mode, press Up arrow
started: Phase 11 implementation of ComputeShrink

## Eliminated

- hypothesis: Sign error in delta calculation (e.g., + vs -)
  evidence: The sign is correct for the edge it IS moving; the problem is WHICH edge is selected, not the direction of movement
  timestamp: 2026-03-02T00:00:00Z

## Evidence

- timestamp: 2026-03-02T00:00:00Z
  checked: ComputeShrink "up" case (lines 266-273)
  found: |
    case "up":
        // Top edge moves inward (downward); bottom edge stays fixed
        newVisTop = vis.top + stepY   // moves top edge DOWN (inward)
  implication: Pressing Up moves the TOP edge downward. The bottom edge is untouched.

- timestamp: 2026-03-02T00:00:00Z
  checked: User mental model vs implementation model
  found: |
    User model: direction key = the side that collapses inward
      "Up" = bottom edge moves up (contracts from bottom)
    Implementation model: direction key = the edge that moves
      "Up" = top edge moves (inward = downward)
    These are opposite mappings.
  implication: Every direction in ComputeShrink is inverted from the user's expectation.

- timestamp: 2026-03-02T00:00:00Z
  checked: ComputeGrow for comparison (lines 176-208)
  found: |
    case "up": top edge moves outward (upward) — direction = edge that moves
    case "down": bottom edge moves outward (downward) — direction = edge that moves
  implication: |
    Grow and Shrink use the SAME mapping (direction = edge that moves).
    But for Grow this is intuitive: "grow up" = top edge goes up = window expands upward.
    For Shrink the same mapping is counter-intuitive: "shrink up" = top edge goes down ≠ what users expect.
    Users expect "shrink up" = bottom edge goes up = window shrinks from below.

- timestamp: 2026-03-02T00:00:00Z
  checked: All four Shrink cases for symmetry
  found: |
    "right" → right edge moves left (inward)    [right edge collapses]
    "left"  → left edge moves right (inward)    [left edge collapses]
    "down"  → bottom edge moves up (inward)     [bottom edge collapses]
    "up"    → top edge moves down (inward)      [top edge collapses]

    User expects:
    "right" → left edge moves right             [contract from right side]
    "left"  → right edge moves left             [contract from left side]
    "down"  → top edge moves down               [contract from top]
    "up"    → bottom edge moves up              [contract from bottom]
  implication: All four directions are inverted. The fix is to swap which edge moves in each case.

## Resolution

root_cause: |
  ComputeShrink selects the SAME edge as the pressed direction (identical mapping to ComputeGrow).
  For Grow this is correct: "grow right" = right edge expands outward.
  For Shrink the expected mental model is the OPPOSITE edge: "shrink right" = LEFT edge moves rightward,
  contracting the window from the right side.

  Concretely for "up":
    Current:  top edge moves downward (vis.top + stepY)   — wrong
    Expected: bottom edge moves upward (vis.bottom - stepY) — correct

  The four cases need to be re-mapped so that each direction moves the OPPOSITE edge inward:
    "up"    → move bottom edge UP    (newVisBottom - stepY, was: newVisTop + stepY)
    "down"  → move top edge DOWN     (newVisTop + stepY,    was: newVisBottom - stepY)
    "left"  → move right edge LEFT   (newVisRight - stepX,  was: newVisLeft + stepX)
    "right" → move left edge RIGHT   (newVisLeft + stepX,   was: newVisRight - stepX)

fix: not applied (goal: find_root_cause_only)
verification: not applied
files_changed: []
