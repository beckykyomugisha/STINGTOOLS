# stingtools-bonsai/handlers/

Empty in the Day-1 scaffold. MVP Week 4–7 work lands here:

| File | Trigger | Purpose |
|---|---|---|
| `on_load.py` | `bpy.app.handlers.load_post` | Reload `EnumRegistry` + `PsetRegistry` when a `.blend` opens; surface drift in status bar |
| `on_save.py` | `bpy.app.handlers.save_pre` | Run IDS validation against `Pset_Sting*` before save; block save on hard failures |
| `on_depsgraph.py` | `bpy.app.handlers.depsgraph_update_post` | Stale-tag detection — flag any element whose geometry changed but tag wasn't updated |

See `docs/MVP_SCOPE_BONSAI.md § Module structure` for the full layout.
