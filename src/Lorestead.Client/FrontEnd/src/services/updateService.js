import { invoke } from './invoke.js'

export async function getUpdateStatus() {
  return invoke('getUpdateStatus')
}

export async function checkForUpdate() {
  return invoke('checkForUpdate')
}

export async function downloadUpdate() {
  return invoke('downloadUpdate')
}

export async function applyUpdateAndRestart() {
  return invoke('applyUpdateAndRestart')
}
