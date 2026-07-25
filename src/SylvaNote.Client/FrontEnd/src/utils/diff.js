import { diffLines, diffWordsWithSpace } from 'diff'

// Unchanged runs longer than this collapse to CONTEXT lines either side plus an
// expander, git-style.
const CONTEXT = 3

function splitLines(text) {
  const lines = text.split('\n')
  // A trailing newline yields a final empty element that is not a real line.
  if (lines.length > 0 && lines[lines.length - 1] === '') lines.pop()
  return lines
}

// Added and removed CHARACTER counts, which is not the same as a length delta:
// replacing ten characters with ten others is 10 added and 10 removed at zero
// delta. Word granularity so the number agrees with the highlights the detail
// view draws (decisions.md).
export function changeCounts(oldText, newText) {
  let added = 0
  let removed = 0
  for (const part of diffWordsWithSpace(oldText ?? '', newText ?? '')) {
    if (part.added) added += part.value.length
    else if (part.removed) removed += part.value.length
  }
  return { added, removed }
}

function wordSegments(oldLine, newLine, want) {
  const segments = []
  for (const part of diffWordsWithSpace(oldLine, newLine)) {
    const isChange = want === 'added' ? part.added : part.removed
    const isOther = want === 'added' ? part.removed : part.added
    if (isOther) continue
    segments.push({ text: part.value, changed: !!isChange })
  }
  return segments
}

// A removed block immediately followed by an added block is a modification, so the
// lines pair up and each pair gets word-level highlights. Leftovers on either side
// are plain removals or additions.
function pushChange(rows, removedLines, addedLines) {
  const paired = Math.min(removedLines.length, addedLines.length)
  for (let i = 0; i < paired; i += 1) {
    rows.push({ kind: 'removed', segments: wordSegments(removedLines[i], addedLines[i], 'removed') })
    rows.push({ kind: 'added', segments: wordSegments(removedLines[i], addedLines[i], 'added') })
  }
  for (let i = paired; i < removedLines.length; i += 1) {
    rows.push({ kind: 'removed', segments: [{ text: removedLines[i], changed: false }] })
  }
  for (let i = paired; i < addedLines.length; i += 1) {
    rows.push({ kind: 'added', segments: [{ text: addedLines[i], changed: false }] })
  }
}

function pushContext(rows, lines, isFirst, isLast) {
  // Only the run's outer edges collapse - a gap at the very start or end of the
  // document has nothing above or below worth keeping as context.
  const head = isFirst ? 0 : CONTEXT
  const tail = isLast ? 0 : CONTEXT
  if (lines.length <= head + tail + 1) {
    for (const line of lines) rows.push({ kind: 'context', text: line })
    return
  }
  for (let i = 0; i < head; i += 1) rows.push({ kind: 'context', text: lines[i] })
  rows.push({ kind: 'gap', lines: lines.slice(head, lines.length - tail) })
  for (let i = lines.length - tail; i < lines.length; i += 1) rows.push({ kind: 'context', text: lines[i] })
}

// A unified single-column diff of the markdown source: rows the panel renders
// directly. `oldText` is the historical version, `newText` the live editor buffer,
// so the result reads as "what restoring would undo".
export function unifiedDiff(oldText, newText) {
  const parts = diffLines(oldText ?? '', newText ?? '')
  const rows = []
  let index = 0

  while (index < parts.length) {
    const part = parts[index]
    if (!part.added && !part.removed) {
      pushContext(rows, splitLines(part.value), index === 0, index === parts.length - 1)
      index += 1
      continue
    }
    const removedLines = part.removed ? splitLines(part.value) : []
    let addedLines = part.added ? splitLines(part.value) : []
    if (part.removed && index + 1 < parts.length && parts[index + 1].added) {
      addedLines = splitLines(parts[index + 1].value)
      index += 1
    }
    pushChange(rows, removedLines, addedLines)
    index += 1
  }

  return rows
}
