import { invoke } from './invoke.js'

export async function getAbout() {
  return invoke('getAbout')
}

export async function getLog() {
  return invoke('getLog')
}

// The desktop webview has no console anyone can open - this is how the frontend
// gets a line into the log Settings shows.
export async function logMessage(request) {
  return invoke('logMessage', { request })
}

export async function getThirdPartyNotices() {
  return invoke('getThirdPartyNotices')
}
