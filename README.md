# LB Locker – System til pakkeallokering

En batchbaseret løsning til behandling af pakker, der skal leveres til pakkebokse.

Systemet importerer pakkedata fra CSV-filer, allokerer pakker til modtagernes ønskede pakkebokse, genererer afhentnings-PIN-koder og sender besked til modtagerne via SMS og/eller e-mail.

Løsningen integrerer med eksterne Locker- og Notification-API'er og vedligeholder en historik over pakkernes status, behandlingsforsøg og relevante hændelser.

Der er lagt vægt på robust behandling, sporbarhed og muligheden for at genoptage afbrudte arbejdsgange uden unødigt at gentage allerede gennemførte handlinger.

Dette repository indeholder løsningen udviklet som led i LB Forsikrings coding assignment til stillingen som Senior Software Engineer.

## Løsningsarkitektur

Løsningen er opdelt i fire projekter med hver deres ansvarsområde. Opdelingen skal adskille domænemodellen, applikationslogikken, de tekniske integrationer og brugerinteraktionen.

| Projekt | Ansvarsområde |
|---|---|
| `LBLocker.Domain` | Domænemodeller, pakkernes livscyklus, allokeringer og centrale domæneregler. |
| `LBLocker.BusinessLogic` | Applikationskontrakter, abstraktioner og orkestrering af CSV-import. |
| `LBLocker.Infrastructure` | Databaseadgang, eksterne API-integrationer, CSV-læsning, PIN-generering og -beskyttelse samt pakkeprocessering. |
| `LBLocker.Console` | Konsolbaseret brugergrænseflade til betjening og administration af løsningen. |

### Arkitektonisk tilgang

Løsningen anvender dependency injection til at sammensætte applikationens komponenter og holde tekniske implementeringer adskilt fra de abstraktioner, der anvendes af applikationslogikken.

Domænelaget indeholder de centrale pakkekoncepter og regler for pakkernes livscyklus. BusinessLogic stiller abstraktioner og applikationsfunktionalitet til rådighed, mens Infrastructure implementerer persistens og kommunikation med eksterne services.

Console-projektet udgør brugerens indgang til løsningen.

## Implementeret funktionalitet

### Import af pakkedata

Systemet importerer pakkedata fra CSV-filer leveret af kernesystemet. Data indeholder hændelser og oplysninger om pakker, modtagere og ønskede pakkebokse.

Importen omfatter:

- Validering af CSV-data og obligatoriske felter.
- Kronologisk behandling af hændelser.
- Inkrementel import med deduplikering via event-ID.
- Opdatering af eksisterende pakke- og modtageroplysninger.
- Registrering af ændringer i pakkens hændelseshistorik.

Hver importhændelse behandles i sin egen databasetransaktion, så data og importstatus opdateres samlet.

### Pakkeprocessering og allokering

Når en pakke er klar til behandling på depotet, forsøger systemet at reservere et passende rum i den pakkeboks, som afsenderen har valgt.

- Den ønskede pakkeboks valideres mod Locker API.
- En PIN-kode genereres og medsendes ved reservationen.
- Hvis pakkeboksen ikke findes, sættes pakken til returnering til afsender.
- Hvis der ikke er et ledigt rum med den nødvendige størrelse, forbliver pakken på depotet.
- Systemet skifter ikke automatisk til en anden pakkeboks.

En gennemført reservation registreres sammen med pakkens øvrige historik.

### PIN-håndtering

Systemet genererer en afhentnings-PIN i forbindelse med reservationen af et rum i pakkeboksen.

PIN-koden:

- Genereres med en kryptografisk sikker tilfældighedsgenerator.
- Sendes med i reservationskaldet til Locker API.
- Opbevares krypteret i databasen.
- Genbruges ved efterfølgende behandling og notifikationer for samme reservation.

Det sikrer, at den PIN-kode, der sendes til modtageren, er den samme som den, der er knyttet til reservationen.

### Notifikationer

Når en pakke er reserveret i en pakkeboks, forsøger systemet at informere modtageren via de tilgængelige kanaler.

- SMS sendes, hvis modtagerens telefonnummer er registreret.
- E-mail sendes, hvis modtagerens e-mailadresse er registreret.
- Begge kanaler forsøges uafhængigt af hinanden.
- Beskeden indeholder pakkereference, afhentningsadresse, pakkeboks, rum og PIN-kode.

Resultatet af hvert afsendelsesforsøg registreres separat. En accepteret besked fra Notification API betragtes som godkendt til afsendelse, men er ikke en garanti for, at modtageren har modtaget den.

### Hændelseshistorik og fejlhåndtering

Systemet registrerer løbende relevante hændelser og behandlingsforsøg for hver pakke.

Det giver mulighed for at følge pakkens forløb, se resultatet af de enkelte behandlingstrin og identificere fejl.

Allerede gennemførte trin gentages ikke unødigt ved genoptagelse af en behandling. Ved afbrudte eksterne kald, hvor resultatet er ukendt, kræves der derimod afklaring, før behandlingen kan fortsætte.

## Afhængigheder

Løsningen er afhængig af LB's Locker API og Notification API til henholdsvis reservation af pakkebokse og afsendelse af beskeder til modtagere.

## Afvikling og test

Løsningen forudsætter, at Locker API og Notification API er tilgængelige.

**Start applikationen**

Applikationen startes fra `LBLocker.Console` i **Debug-konfiguration**. Debug mode er nødvendig for at anvende den indbyggede udviklingsnøgle til PIN-kryptering.

```bash
dotnet run --project src/LBLocker.Console/LBLocker.Console.csproj --configuration Debug
```

**Kør tests**

Alle testprojekter kan afvikles fra solution-filen:

```bash
dotnet test LBLocker.sln
```

## Arkitektonisk vurdering

Den overordnede implementering af databaseadgang og eksterne API-integrationer er solid og velfungerende.

Den væsentligste arkitektoniske svaghed findes i samspillet mellem Console og BusinessLogic. Console fungerer ikke konsekvent som et tyndt præsentationslag, men overtager flere steder ansvar for dataadgang og applikationslogik, som burde være placeret i BusinessLogic.

Det betyder, at den etablerede lagdeling ikke konsekvent bliver overholdt. Det gør løsningen mindre overskuelig og vanskeligere at vedligeholde og videreudvikle.

En central forbedring vil derfor være at styrke separationen mellem brugergrænseflade og applikationslogik.

## Videreudvikling

Følgende områder er identificeret som relevante forbedringer i et videre udviklingsforløb:

- **Arkitektur og ansvarsfordeling:** Gør Console til et tyndt præsentationslag, saml applikationsfunktionalitet i BusinessLogic, og gennemgå service- og menustrukturen ([#1](https://github.com/jacobhostrup/lb_locker_case/issues/1), [#6](https://github.com/jacobhostrup/lb_locker_case/issues/6), [#7](https://github.com/jacobhostrup/lb_locker_case/issues/7), [#8](https://github.com/jacobhostrup/lb_locker_case/issues/8), [#9](https://github.com/jacobhostrup/lb_locker_case/issues/9)).
- **PIN-sikkerhed:** Sikr unikhed blandt aktive reservationer ([#11](https://github.com/jacobhostrup/lb_locker_case/issues/11)).
- **Notifikationer:** Udskil opbygningen af beskedindhold i en selvstændig komponent ([#13](https://github.com/jacobhostrup/lb_locker_case/issues/13)).
- **Konfiguration:** Flyt eksterne API-adresser fra koden til konfiguration ([#14](https://github.com/jacobhostrup/lb_locker_case/issues/14)).
- **Robusthed og kvalitet:** Udvid testdækningen, og styrk valideringen af CSV-data og brugerinput ([#10](https://github.com/jacobhostrup/lb_locker_case/issues/10), [#12](https://github.com/jacobhostrup/lb_locker_case/issues/12), [#4](https://github.com/jacobhostrup/lb_locker_case/issues/4), [#5](https://github.com/jacobhostrup/lb_locker_case/issues/5)).

Derudover bør håndteringen af notifikationer, hvor ingen kanal accepterer beskeden, afklares og tilpasses den ønskede forretningsproces. Den nuværende implementering lader pakken forblive allokeret og kræver operationel håndtering.
