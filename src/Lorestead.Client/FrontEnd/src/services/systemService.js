import { invoke } from './invoke.js'

export async function getAbout() {
  return invoke('getAbout')
}

export async function getLog() {
  return invoke('getLog')
}
