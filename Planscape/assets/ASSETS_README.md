# Mobile assets — placeholder set

These four PNGs (`icon.png`, `adaptive-icon.png`, `notification-icon.png`,
`splash.png`) are **placeholder solid-colour images** generated to satisfy
Expo's build-time image-shape validator. The build pipeline will not
compile without them.

Replace before TestFlight / Play submission:

| File | Required size | Purpose |
|---|---|---|
| `icon.png` | 1024×1024 PNG, square | iOS app icon (Expo derives all sizes from this) |
| `adaptive-icon.png` | 1024×1024 PNG, square, foreground content within centre 66% | Android adaptive-icon foreground |
| `splash.png` | At least 1242×2436 PNG, portrait | Launch screen artwork |
| `notification-icon.png` | 96×96 PNG, monochrome with transparent background | Android notification tray icon (Expo applies the configured tint) |

Background colour is currently set to navy `#1A237E` in `app.json`; the
notification icon uses the brand orange `#E8912D`. Keep both in sync if
you change the icon palette.
