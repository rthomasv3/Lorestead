// A label's colour is derived from its name, never chosen: the same word is the
// same colour on every board and every device with nothing to sync or manage.
// The names index the --label-* tokens in style.css.
export const LABEL_COLORS = [
  'indigo', 'violet', 'blue', 'cyan', 'emerald', 'rose', 'rust', 'olive',
  'amber', 'orange', 'pink', 'teal',
]

// djb2 over the normalized name, so "Agent" and "agent" land on one colour.
export function labelColor(label) {
  const text = String(label ?? '').trim().toLowerCase()
  let hash = 5381
  for (let i = 0; i < text.length; i++) {
    hash = ((hash * 33) ^ text.charCodeAt(i)) >>> 0
  }
  return LABEL_COLORS[hash % LABEL_COLORS.length]
}

export function labelStyle(label) {
  return { '--label': `var(--label-${labelColor(label)})` }
}
