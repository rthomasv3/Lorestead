// Tying rendered HTML back to the markdown that produced it.

// markdown-it drops the source positions it parsed with, so a click on the
// preview cannot say which line it came from. This core rule copies every block
// token's `map` onto its element, making the nearest [data-line] ancestor of any
// node the line that rendered it.
export function sourceLines(md) {
  md.core.ruler.push('source-lines', (state) => {
    for (const token of state.tokens) {
      // Nesting -1 is a closing tag, which carries no attributes of its own;
      // hidden tokens are the paragraphs elided inside tight lists, and render
      // nothing to hang the attribute on.
      if (token.map && token.nesting >= 0 && !token.hidden) {
        token.attrSet('data-line', String(token.map[0]))
      }
    }
    return true
  })
}

const TASK_ITEM = /^\s*(?:[-*+]|\d+[.)])\s+\[([ xX])\]/

// Flips `- [ ]` to `- [x]` (and back) on one line of `markdown`, returning the
// whole document. Null when that line is not a task item at all - which means
// the HTML clicked was rendered from different text than the caller is holding,
// and guessing at a line to rewrite would corrupt it.
export function toggleTaskLine(markdown, line) {
  const lines = (markdown ?? '').split('\n')
  const text = lines[line]
  if (text === undefined) return null

  const match = TASK_ITEM.exec(text)
  if (!match) return null

  // The match ends at the closing bracket, so the state character is two back.
  const at = match[0].length - 2
  lines[line] = `${text.slice(0, at)}${match[1] === ' ' ? 'x' : ' '}${text.slice(at + 1)}`
  return lines.join('\n')
}
