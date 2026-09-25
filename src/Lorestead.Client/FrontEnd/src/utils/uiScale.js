// Readers for the interface scale (see --ui-scale / --ui-user-scale in style.css),
// for the few sizes that live in JS rather than in rem.

// The Interface scale setting as a factor (1 = 100%), without the mobile bump.
export function userScale() {
  const value = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--ui-user-scale'))
  return Number.isFinite(value) && value > 0 ? value : 1
}

// A px length that follows the Interface scale setting, for CSS written in JS.
export function scaledPx(px) {
  return `calc(${px}px * var(--ui-user-scale, 1))`
}
