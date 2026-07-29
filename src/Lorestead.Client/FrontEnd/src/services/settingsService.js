import { invoke } from "./invoke.js";

export async function getSettings() {
    return invoke("getSettings");
}

export async function saveApplicationSettings(request) {
    return invoke("saveApplicationSettings", { request });
}

export async function saveEditorSettings(request) {
    return invoke("saveEditorSettings", { request });
}
