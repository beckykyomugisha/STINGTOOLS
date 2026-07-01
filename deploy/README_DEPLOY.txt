============================================================
 STING TOOLS - INSTALL & ACTIVATE  (Revit 2025 / 2026)
============================================================
 Short version. Full step-by-step is in  INSTALL_GUIDE.md
 (open it in any browser or text viewer) - please read that
 if anything is unclear.

WHAT THIS IS
  A compiled Revit add-in. You do NOT need Visual Studio,
  the .NET SDK, or any source code.

REQUIREMENTS
  - Windows 10/11 (64-bit)
  - Autodesk Revit 2025 or 2026 already installed
    (Revit ships with the .NET 8 runtime it needs.)

------------------------------------------------------------
STEP 1 - INSTALL  (about 60 seconds)
------------------------------------------------------------
  1. Extract this zip to a PERMANENT folder, e.g. C:\STINGTOOLS
     (keep the structure - install.bat must sit next to the
      CompiledPlugin folder).
  2. Double-click  install.bat
       (SmartScreen warning? -> More info -> Run anyway.)
  3. Fully CLOSE Revit, then reopen it.
  4. You will see a "STING Tools" ribbon tab with ONE button:
     "Activate STING". That is expected -> go to Step 2.

  DO NOT move/rename the folder after installing. If you must,
  run install.bat again from the new location.

------------------------------------------------------------
STEP 2 - ACTIVATE  (REQUIRED - buttons are locked until you do)
------------------------------------------------------------
  STING is licensed per machine. Until you activate, EVERY
  button is locked. This is normal.

  1. Revit ribbon -> STING Tools -> "Activate STING".
  2. The dialog shows your MACHINE CODE. Click Copy.
  3. Email that code to support@planscape.app (or Davis) with
     your name. We send back a licence file.
  4. Paste the licence into the box -> click "Apply license".
  5. "Activated. Please restart Revit." -> close & reopen Revit.
  6. The STING panels now appear on the right. You are ready.

  One machine = one code = one licence. Repeat on each PC.

------------------------------------------------------------
IF SOMETHING GOES WRONG  (the important part)
------------------------------------------------------------
  1. Screenshot the error.
  2. Double-click  collect-logs.bat  -> makes
     STING_logs_<date>.zip on your Desktop.
  3. Send BOTH back, and say WHICH button you clicked, what you
     expected, and what happened.

------------------------------------------------------------
COMMON FIXES
------------------------------------------------------------
  "Only Activate button shows / buttons do nothing"
     -> You are not activated. Do Step 2, restart Revit.
  "Licence expired / not valid for this machine"
     -> Send your machine code again for a fresh licence.
  "Nothing appears after install"
     -> Fully close ALL Revit windows first. Check this exists:
        %AppData%\Autodesk\Revit\Addins\2025\StingTools.addin
  "Add-in security warning at startup"
     -> Choose "Always Load".

  Plugin log:  <extract folder>\CompiledPlugin\StingTools.log
  Uninstall:   double-click uninstall.bat, restart Revit.
  Update:      replace the CompiledPlugin folder, run
               install.bat again. Your licence stays valid.

  Full guide:  INSTALL_GUIDE.md
============================================================
