import { invoke } from './invoke.js'

export async function getAttachments(request) {
  return invoke('getAttachments', { request })
}

export async function addAttachment(request) {
  return invoke('addAttachment', { request })
}

export async function renameAttachment(request) {
  return invoke('renameAttachment', { request })
}

export async function deleteAttachment(request) {
  return invoke('deleteAttachment', { request })
}

export async function getAttachmentData(request) {
  return invoke('getAttachmentData', { request })
}

export async function getAttachmentThumbnail(request) {
  return invoke('getAttachmentThumbnail', { request })
}

export async function saveAttachmentThumbnail(request) {
  return invoke('saveAttachmentThumbnail', { request })
}

export async function downloadAttachment(request) {
  return invoke('downloadAttachment', { request })
}
