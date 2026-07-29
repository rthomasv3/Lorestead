import { invoke } from './invoke.js'

export async function pickImportFile(destinationParentId) {
  return invoke('pickImportFile', { request: { destinationParentId: destinationParentId || null } })
}

export async function pickImportFolder(destinationParentId) {
  return invoke('pickImportFolder', { request: { destinationParentId: destinationParentId || null } })
}

export async function previewImport(destinationParentId) {
  return invoke('previewImport', { request: { destinationParentId: destinationParentId || null } })
}

export async function runImport(destinationParentId) {
  return invoke('runImport', { request: { destinationParentId: destinationParentId || null } })
}
