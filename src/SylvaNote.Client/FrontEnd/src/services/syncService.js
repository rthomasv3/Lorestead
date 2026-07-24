import { invoke } from "./invoke.js";

export async function getSyncStatus() {
    return invoke("getSyncStatus");
}

export async function saveSyncServerUrl(request) {
    return invoke("saveSyncServerUrl", { request });
}

export async function saveSyncToken(request) {
    return invoke("saveSyncToken", { request });
}

export async function syncNow() {
    return invoke("syncNow");
}
