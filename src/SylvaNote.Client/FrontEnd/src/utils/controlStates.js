// Shared by Button and NavButton so the tools rail, the editor toolbar and the
// sidebar nav can't drift apart again.
//
// The accent-soft fill carries "active" on its own and the glyph stays a normal
// on-surface tone: tinting the icon accent as well put accent on accent (and, on
// the tools rail, an accent badge on top of that) and read as low contrast.
export const CONTROL_ACTIVE = 'bg-accent-soft text-on-surface'

// `enabled:` because Button can be disabled; a NavButton never is, and a
// non-disabled button matches :enabled either way.
export const CONTROL_GHOST = 'text-on-surface-muted enabled:hover:bg-surface-alt enabled:hover:text-on-surface'
