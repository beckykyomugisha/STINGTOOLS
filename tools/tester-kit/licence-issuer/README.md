# STING tester licences - Planscape only

Do not send this folder to testers. It works with your `private.pem`, and a leaked key
would let anyone issue licences.

Every new install runs a built-in 90-day trial, then locks until a signed licence is
installed. A licence either names one PC (its machine code) or is portable (any PC); both
have an expiry date. This kit issues 90-day machine licences for a list of testers.

**Portable licence** (no machine codes needed): from the folder holding `private.pem`,

```powershell
dotnet run --project C:\Dev\STINGTOOLS\StingTools.LicenseIssuer -- issue --any-machine --name "Tester group Oct" --days 90
```

Anyone holding that file can activate any PC until it expires, so keep `--days` short,
give each group its own file, and treat it like a key. Put it next to `install.bat` (the
installer copies it) or send it for `Install-Licence.cmd`.

1. Each tester runs `Get-MachineCode.cmd` from the tester kit and sends you the code
   (`XXXX-XXXX-XXXX-XXXX-XXXX`).
2. Put their names and codes in `testers.csv` (replace the example row).
3. In PowerShell, from this folder:

   ```powershell
   .\Issue-TesterLicences.ps1 -Repo C:\Dev\STINGTOOLS -KeyDir C:\path\to\folder-with-private.pem
   ```

   It builds the issuer once. It then writes `licences\<Name>\StingTools.lic` for each
   tester and appends each licence to `issued-licenses.csv` next to your key. Use
   `-Days 120` for a different length.
4. Send each tester **their own** `StingTools.lic`. They install it with
   `Install-Licence.cmd`.

- **Use your existing key.** `private.pem` must be the key whose public half is built into
  the plugin (`LicensePublicKey.cs`). Running `keygen` again would create a new key, and
  the shipped build would reject every licence signed with it.
- **Code mismatch.** If a tester's code differs from what the Activate dialog in Revit
  shows, trust the dialog. The issuer refuses a code that is not in the right shape.
- **Expiry.** Each licence stops working 90 days after you issue it, not after the tester
  installs it. Re-run the script with the same code to extend.
