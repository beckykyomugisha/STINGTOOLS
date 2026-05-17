// Preload — exposes a safe contextBridge API to the renderer.
// Only methods explicitly listed here are callable from React code.

import { contextBridge, ipcRenderer } from 'electron';

contextBridge.exposeInMainWorld('planscape', {
  ifc: {
    listPending: (folder: string)     => ipcRenderer.invoke('ifc:listPending', folder),
    openFolder:  (folder: string)     => ipcRenderer.invoke('ifc:openFolder',  folder),
  },
  settings: {
    get: (key: string)                => ipcRenderer.invoke('settings:get', key),
    set: (key: string, value: unknown)=> ipcRenderer.invoke('settings:set', key, value),
  },
  notify: (title: string, body: string) => ipcRenderer.invoke('notify', title, body),
});
