// Turning files - dropped, picked or pasted - into attachments, and attachments
// into the markdown that links them.

import { readAttachmentFile } from '../services/attachmentService.js'
import { base64ToBlob, createImageThumbnail } from './thumbnails.js'

export const MAX_ATTACHMENT_SIZE = 100 * 1024 * 1024

// Everything addAttachment needs, for a note or a task alike. The base64 read
// and the thumbnail happen here so every entry point produces the same record.
export async function toAttachmentPayload(file) {
  return {
    filename: file.name,
    mimeType: file.type || 'application/octet-stream',
    dataBase64: await readBase64(file),
    thumbnailBase64: await createImageThumbnail(file, file.type),
  }
}

// Images embed so they render inline in the preview; everything else is a plain
// link. The same shape the `[[` completion and an attachment drop produce.
export function attachmentLink({ id, filename, mimeType }) {
  const embed = (mimeType || '').startsWith('image/') ? '!' : ''
  return `${embed}[${filename || 'Attachment'}](attachment://${id})`
}

// The files on a paste, named. A screenshot arrives as a File the browser calls
// "image.png" every time, which would fill the attachment list with identical
// names, so those get a timestamp instead; a file copied from a file manager
// keeps the name it already has.
export function pastedFiles(event) {
  const data = event.clipboardData
  if (!data) return []

  // files is what Chromium fills in. WebKit - the GTK desktop build and iOS -
  // has been known to carry the bitmap only as an item, and getAsFile has to be
  // called now, while the event is still live.
  let files = [...(data.files ?? [])]
  if (files.length === 0) {
    files = [...(data.items ?? [])]
      .filter((item) => item.kind === 'file')
      .map((item) => item.getAsFile())
      .filter(Boolean)
  }

  return files.map((file) => (!file.name || GENERIC_NAME.test(file.name) ? renamed(file) : file))
}

// What Chromium and WebKit both name a bitmap taken straight off the clipboard.
const GENERIC_NAME = /^image\.[a-z0-9]+$/i

// The async clipboard, for the webviews whose paste event hides the bitmap.
// WebKitGTK could not put image data on a paste event's DataTransfer at all
// until webkit.org/b/218519 was fixed in October 2025, and served it through
// this API the whole time; WebKit allows the read without a permission prompt
// when it happens during an explicit paste gesture, which is where this runs.
export async function clipboardPayload() {
  if (!navigator.clipboard?.read) return { files: [], paths: [], types: [] }

  const items = await navigator.clipboard.read()
  const files = []
  for (const item of items) {
    const type = item.types.find((candidate) => candidate.startsWith('image/'))
    if (type) files.push(fileFromBlob(await item.getType(type), type))
  }

  // The paste event's DataTransfer is sanitized down to a couple of types by
  // some webviews, so a file manager's uri-list only shows up here.
  const paths = []
  if (files.length === 0) {
    for (const item of items) {
      if (!item.types.includes('text/uri-list')) continue
      const list = await (await item.getType('text/uri-list')).text()
      paths.push(...filePathsFromUris(list.split(/\r?\n/)))
    }
  }

  return { files, paths, types: items.flatMap((item) => item.types) }
}

function renamed(file) {
  return fileFromBlob(file, file.type)
}

function fileFromBlob(blob, mimeType) {
  return new File([blob], `pasted-image-${stamp()}.${extensionOf(mimeType)}`, { type: mimeType })
}

function stamp() {
  const now = new Date()
  const pad = (value) => String(value).padStart(2, '0')
  return `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}`
    + `-${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`
}

function extensionOf(mimeType) {
  if (mimeType === 'image/jpeg') return 'jpg'
  if (mimeType === 'image/svg+xml') return 'svg'
  const subtype = (mimeType || '').split('/')[1] || 'bin'
  return subtype.replace(/[^a-z0-9]/gi, '') || 'bin'
}

function readBase64(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result.split(',')[1])
    reader.onerror = reject
    reader.readAsDataURL(file)
  })
}

// A file manager hands files over as file:// URIs, never as bytes, so the paths
// are all the frontend can learn and the bytes have to be read natively. Empty on
// WebKit, which announces the uri-list type on a drop and then serves it empty -
// there the paths arrive out of band instead (see nativeFileDrop.js).
export function filePathsFrom(transfer) {
  return filePathsFromUris((transfer?.getData?.('text/uri-list') || '').split(/\r?\n/))
}

export function filePathsFromUris(uris) {
  return uris
    // A uri-list comments with '#', and drags often carry a trailing blank.
    .filter((line) => line && !line.startsWith('#') && line.startsWith('file://'))
    .map(pathOf)
    .filter(Boolean)
}

export async function filesFromPaths(paths) {
  const files = []
  for (const path of paths) {
    const file = await readAttachmentFile({ path })
    // Null for a path that is not a readable file or is over the limit - skipped
    // the way an oversized dropped file is.
    if (!file?.dataBase64) continue
    files.push(new File([base64ToBlob(file.dataBase64, file.mimeType)], file.filename, {
      type: file.mimeType || 'application/octet-stream',
    }))
  }
  return files
}

function pathOf(uri) {
  try {
    const { pathname } = new URL(uri)
    const path = decodeURIComponent(pathname)
    // Windows arrives as /C:/Users/... - the leading slash is part of the URL,
    // not of the path.
    return /^\/[a-z]:/i.test(path) ? path.slice(1) : path
  } catch {
    return null
  }
}
