# Healthcare Room Data Sheet template stub

The actual `healthcare_rds.docx` is a Word document that should be
authored and committed alongside the other H-8 source templates
(`deliverable_standard.docx`, etc.). The token contract below is
enforced by `RdsContextBuilder`:

## Header tokens
- {{room.number}} {{room.name}} {{room.area}} {{room.height}}
- {{room.health_class}} {{room.hbn_ref}} {{room.adb_code}}

## Environmental
- {{room.press.regime}} / {{room.press.delta_pa}}
- {{room.ach.req}} / {{room.ach.outside}} / {{room.hepa.grade}}
- {{room.temp.design_c}} / {{room.rh.design_pct}}
- {{room.noise.db}} / {{room.noise.nr}}
- {{room.lighting.lux}} / {{room.lighting.cct}}

## Loops (MiniWord {{#each X}}…{{/each}})
- {{#each services}} {{type}} {{count}} {{notes}} {{/each}}
- {{#each equipment}} {{prod_code}} {{description}} {{make_model}} {{notes}} {{/each}}
- {{#each finishes}} {{element}} {{spec}} {{notes}} {{/each}}

## Project / project-organisation
- {{prj.code}} {{prj.name}} {{prj.client}} {{prj.facility_type}}
- {{prj.ae_vent}} {{prj.ae_mgas}} {{prj.ae_water}} {{prj.ae_elec}}

## Sign-off
- {{#each signatures}} {{role}} {{name}} {{date}} {{/each}}
