// Thumbnails are generated in the frontend because the webview is the only
// image decoder in the app - no native imaging dependency needed. PNG keeps
// transparency; at this size the files stay small.
const MAX_DIMENSION = 256

export async function createImageThumbnail(blob, mimeType) {
  let result = null
  if ((mimeType || '').startsWith('image/')) {
    try {
      const bitmap = await createImageBitmap(blob)
      const scale = Math.min(1, MAX_DIMENSION / Math.max(bitmap.width, bitmap.height))
      const canvas = document.createElement('canvas')
      canvas.width = Math.max(1, Math.round(bitmap.width * scale))
      canvas.height = Math.max(1, Math.round(bitmap.height * scale))
      canvas.getContext('2d').drawImage(bitmap, 0, 0, canvas.width, canvas.height)
      bitmap.close()
      result = canvas.toDataURL('image/png').split(',')[1]
    } catch {
      // Undecodable image (corrupt, unsupported format) - card falls back to the
      // type icon.
      result = null
    }
  }
  return result
}

export function base64ToBlob(base64, mimeType) {
  const bytes = Uint8Array.from(atob(base64 || ''), (c) => c.charCodeAt(0))
  return new Blob([bytes], { type: mimeType || 'application/octet-stream' })
}
