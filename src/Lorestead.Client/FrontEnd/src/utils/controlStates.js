// Shared by Button and NavButton so the tools rail, the editor toolbar and the
// sidebar nav can't drift apart again.
//
// The accent-soft fill carries "active" on its own and the glyph stays a normal
// on-surface tone: tinting the icon accent as well put accent on accent (and, on
// the tools rail, an accent badge on top of that) and read as low contrast.
export const CONTROL_ACTIVE = 'bg-accent-soft text-on-surface'

// The hover tint is an alpha wash over whatever is underneath, not `surface-alt`.
// `surface-alt` and `surface-elevated` resolve to the same color, so a ghost
// control sitting on a card, a menu or a dialog painted its own hover invisible
// - the same bug the context menus had. An alpha reads on every plane. The wash
// strength lives in the `hover-wash` token because it needs tuning per theme.
//
// `enabled:` because Button can be disabled; a NavButton never is, and a
// non-disabled button matches :enabled either way. It also means these strings
// only work on form elements - a `<span role="button">` never matches :enabled,
// so the tree row's nested hover icons still carry their tones by hand.
export const CONTROL_GHOST = 'text-on-surface-muted enabled:hover:bg-hover-wash enabled:hover:text-on-surface'

// Destructive ghost: rests in the same muted tone as its neighbours and only
// turns red under the pointer, so a row of icons doesn't shout before you aim.
export const CONTROL_GHOST_DANGER = 'text-on-surface-muted enabled:hover:bg-red-500/10 enabled:hover:text-red-500'
