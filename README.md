# GiTIA

Versjonskontroll av TIA Portal-prosjekter for et lite team, litt som GitHub Desktop.
Ett felles git-repo (Bitbucket eller GitHub) med én mappe per prosjekt. Hver mappe har det nyeste arkivet
(`Prosjekt.zapNN`, lagret i Git LFS) og en `lock.txt` når noen har sjekket prosjektet ut.
Historikken er git-historikken. Det trengs ingen egen server.

## Arbeidsflyt

1. **Morgen:** velg prosjekt → **Sjekk ut**. Prosjektet låses i ditt navn, og nyeste versjon lastes ned som
   `dato_hvem_prosjekt.zapNN` til nedlastingsmappen. Åpne den i TIA (Retrieve).
2. **Kveld:** **Last opp ny versjon** → velg prosjekt (eller *Nytt prosjekt…*), skriv hva som er endret og hvem som laster opp.
   Appen kobler seg til TIA som kjører, kompilerer alle enheter (spør om du vil fortsette hvis det feiler), lagrer,
   arkiverer til arkivmappen og laster opp. Låsen din frigjøres.
   Feiler TIA-delen, viser appen hva som gikk galt og hvordan du gjør det manuelt, og lar deg velge .zap-filen selv.
   På anlegg der oppetid er viktigst: slå på *Spør hvor godt endringen er testet* under **Innstillinger**, så må du også
   skrive hvor godt endringen er testet på anlegget (vises som f.eks. `[Testet: kjørt med pumpe 2 i manuell]` i historikken).

Andres utsjekker vises i rødt. Laster du ned et prosjekt noen andre har sjekket ut, får du en tydelig advarsel.
Glemt utsjekk kan brytes med **Bryt låsen**.

## Oppsett

- Ett nytt, tomt repo på Bitbucket eller GitHub, med Git LFS (begge har det). Alle må ha skrivetilgang.
- På hver PC:
  - [Git for Windows](https://git-scm.com) (har Git LFS og Git Credential Manager; første gang åpnes et innloggingsvindu).
  - TIA Portal **V21** med Openness, og Windows-brukeren i gruppen **Siemens TIA Openness** (logg av/på etterpå).
    Første gang appen kobler til, spør TIA om tilgang: svar *Yes*.
- Start `GiTIA.exe`, fyll inn repo-adressen og navnet ditt under **Innstillinger**.

Innstillinger ligger i `%APPDATA%\GiTIA\settings.xml`, appens egen kopi av repoet i `%LOCALAPPDATA%\GiTIA\`.

## Bygge

Krever .NET SDK og TIA Portal V21 installert (koden refererer Openness-DLL-en derfra).

```
dotnet build GiTIA.sln
dotnet test tests/GiTIA.Core.Tests
```

Del `src/GiTIA/bin/Release/net48/GiTIA.exe` med kollegaene (bygg med `dotnet build GiTIA.sln -c Release`). Exe-en er én fil uten andre DLL-er: Siemens-DLL-ene lastes fra TIA-installasjonen.

## Begrensninger

- Bare TIA Portal V21. En annen versjon krever at `Siemens.Engineering.Base`-referansen og registernøkkelen i `Tia.cs` endres og appen bygges på nytt.
- Arkivene er store. Hver opplasting lagres i Git LFS, så følg med på lagringskvoten hos Bitbucket/GitHub.
