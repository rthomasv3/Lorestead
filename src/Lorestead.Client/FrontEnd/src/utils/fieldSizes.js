// One source of form-control metrics, so an input and a select sitting in the
// same settings row are exactly the same height and can't drift apart.
//
// The split follows PrimeVue's Aura formField tokens: small steps the *font*
// down a notch as well as the padding, rather than only shrinking the box. Our
// normal is already text-sm because the app is denser than PrimeVue's default,
// so small lands on text-xs.
export const FIELD_SIZES = {
  normal: { field: 'h-9 px-2.5 gap-3 text-sm', icon: 'size-4', hotkey: 'text-xs' },
  small: { field: 'h-7 px-2 gap-2 text-xs', icon: 'size-3.5', hotkey: 'text-[10px]' },
}

export function fieldSize(size) {
  return FIELD_SIZES[size] ?? FIELD_SIZES.normal
}
