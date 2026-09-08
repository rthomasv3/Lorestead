// Copy-to-clipboard buttons on rendered code blocks.

import copyIcon from '~icons/lucide/copy?raw'
import checkIcon from '~icons/lucide/check?raw'

// The icons come from the same @iconify-json/lucide set the <i-lucide-*>
// components resolve against, as strings: the button is injected into markup
// that MarkdownPreview hands to v-html, where a Vue component cannot go.
const BUTTON =
  '<button type="button" class="copy-code" aria-label="Copy code" title="Copy code">' +
  `<span class="copy-code-idle">${copyIcon}</span>` +
  `<span class="copy-code-done">${checkIcon}</span>` +
  '</button>'

// Wraps every code block in a positioning parent carrying the button. The
// single-line class is what moves the button from the top corner to the middle
// of the right edge - a one-line block has no corner to speak of.
export function copyButtons(md) {
  for (const rule of ['fence', 'code_block']) {
    const base = md.renderer.rules[rule]
    md.renderer.rules[rule] = (tokens, idx, options, env, self) => {
      const pre = base(tokens, idx, options, env, self)
      const single = !tokens[idx].content.trimEnd().includes('\n')
      return `<div class="code-block${single ? ' code-block-single' : ''}">${pre}${BUTTON}</div>`
    }
  }
}

// Reads back what is on screen rather than the source token, so what lands on
// the clipboard is the code as rendered - highlight.js spans included in the
// markup, excluded from textContent.
export function codeTextOf(button) {
  const block = button.closest('.code-block')
  const code = block?.querySelector('pre code') ?? block?.querySelector('pre')
  return code?.textContent ?? ''
}

// The async clipboard needs a secure context. Galdr's desktop origin
// (http://galdr.localhost) and Android's (https://appassets.androidplatform.net)
// both qualify; iOS is served from the galdr:// custom scheme, which does not,
// and lands on the legacy path below.
export async function copyText(text) {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text)
      return true
    }
  } catch {
    // Permission denied or no clipboard - the fallback may still work.
  }
  return legacyCopy(text)
}

function legacyCopy(text) {
  const area = document.createElement('textarea')
  area.value = text
  // Off-screen rather than hidden: display:none or visibility:hidden leaves
  // nothing selectable, and the copy silently does nothing.
  area.setAttribute('readonly', '')
  area.style.position = 'fixed'
  area.style.top = '0'
  area.style.left = '-9999px'
  document.body.appendChild(area)

  const active = document.activeElement
  area.select()
  area.setSelectionRange(0, text.length)

  let copied = false
  try {
    copied = document.execCommand('copy')
  } catch {
    copied = false
  }

  area.remove()
  // The editor is usually what had focus; taking it away for a copy and not
  // giving it back would drop the caret.
  if (active instanceof HTMLElement) active.focus()
  return copied
}
