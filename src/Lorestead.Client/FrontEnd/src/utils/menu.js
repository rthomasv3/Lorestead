// Shared by every Reka menu surface: tree and board context menus, the split
// add button's dropdown, the select popup.
//
// The highlight is accent-tinted rather than bg-surface-alt - the app's usual
// hover tint - because menus sit on bg-surface-elevated, and surface-alt and
// surface-elevated resolve to the same color in both themes (white / zinc-800).
// bg-surface-alt is only a visible hover over bg-surface. accent/10 is
// translucent, so it reads on any surface, and matches the tree's selected row.
export const MENU_ITEM_CLASS =
  'flex items-center gap-2 px-2.5 py-1.5 text-sm rounded-md cursor-default select-none outline-none data-highlighted:bg-accent/10'

// Touch long-press delay for context menus (Reka default: 700ms). Touch drag
// is disabled (useFinePointer gates dnd registration), so long-press has one
// owner and this is purely a feel setting - tune freely, applies to the notes
// tree and board list alike.
export const MENU_PRESS_DELAY = 450
