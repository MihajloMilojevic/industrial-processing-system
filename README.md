# Industrial Processing System

Thread-safe servis za simulaciju obrade industrijskih zadataka sa prioritetima, retry logikom i event-driven logom. Implementiran kao producer-consumer sistem sa async/await i custom scheduler nitom.

## Zahtevi

- .NET 8 SDK ili noviji
- Windows / Linux / macOS

## Pokretanje

```bash
git clone https://github.com/MihajloMilojevic/industrial-processing-system.git
cd IndustrialProcessingSystem

dotnet run --project src/IndustrialProcessingSystem.Console
```

Sistem se pokreće, učitava konfiguraciju iz `SystemConfig.xml`, startuje scheduler i producer niti. Pritisnite `Enter` za zaustavljanje.

## Konfiguracija

Konfiguracija se čita iz `src/IndustrialProcessingSystem.Console/SystemConfig.xml` i kopira u output direktorijum pri buildu.

```xml
<?xml version="1.0" encoding="utf-8"?>
<SystemConfig>
  <!-- Broj slotova u globalnom bazenu niti -->
  <WorkerCount>5</WorkerCount>

  <!-- Maksimalan broj poslova u redu čekanja -->
  <MaxQueueSize>100</MaxQueueSize>

  <!-- Timeout po poslu u sekundama od trenutka Submit-a. Default: 2 -->
  <JobTimeoutSeconds>2</JobTimeoutSeconds>

  <!--
    Threshold za preskakanje: ako je posao čekao više od
    (JobTimeoutSeconds × PrioritySkipThreshold) sekundi,
    scheduler ga ne sme preskočiti. Default: 0.5 (50%)
  -->
  <PrioritySkipThreshold>0.5</PrioritySkipThreshold>

  <!--
    Ako true: scheduler nikad ne preskače posao višeg prioriteta —
    blokira dok se ne oslobode potrebni slotovi.
    Ako false: koristi threshold logiku. Default: false
  -->
  <StrictPriority>false</StrictPriority>

  <!-- Inicijalni poslovi koji se submituju pri startu sistema -->
  <Jobs>
    <Job Type="Prime" Payload="numbers:10_000,threads:3" Priority="1"/>
    <Job Type="Prime" Payload="numbers:20_000,threads:2" Priority="2"/>
    <Job Type="IO"    Payload="delay:1_000"              Priority="3"/>
    <Job Type="IO"    Payload="delay:3_000"              Priority="3"/>
    <Job Type="IO"    Payload="delay:15_000"             Priority="3"/>
  </Jobs>
</SystemConfig>
```

### Tipovi poslova

| Tip   | Payload format              | Opis                                              |
|-------|-----------------------------|---------------------------------------------------|
| Prime | `numbers:{N},threads:{T}`   | Broji proste do N, paralelno sa T niti (max 8)    |
| IO    | `delay:{ms}`                | Simulira I/O čekanje, vraća slučajan broj 0–100   |

### Prioriteti

Manji broj znači veći prioritet. Posao sa `Priority=1` ide pre `Priority=3`.

### Zauzimanje slotova

`WorkerCount` je ukupan broj dostupnih niti za sve poslove:

| Tip posla   | Zauzima slotova            |
|-------------|----------------------------|
| IO          | 1                          |
| Prime       | `threads` iz Payload-a     |

Posao ne počinje dok ne može atomično da uzme sve potrebne slotove.

## Output

### Log fajl

Svaki događaj se asinhrono upisuje u `logs/jobs.log`:

```
[2026-04-16 14:32:01] [COMPLETED] 3f2a1b04-..., 1229
[2026-04-16 14:32:03] [FAILED]    7c8d2e11-..., attempt 1
[2026-04-16 14:32:05] [ABORT]     7c8d2e11-...
```

### Izveštaji

Na svakih 60 sekundi generiše se XML izveštaj u `reports/report_N.xml` (N = 0..9, kružno):

```xml
<Report GeneratedAt="2026-04-16 14:33:00" TotalJobs="42">
  <JobType Type="IO"    Completed="18" Failed="2" AvgDurationMs="1024.50"/>
  <JobType Type="Prime" Completed="20" Failed="2" AvgDurationMs="312.80"/>
</Report>
```

## Pokretanje testova

```bash
dotnet test
```

Testovi su time-independent — koriste `await handle.Result.WaitAsync(timeout)` i `TaskCompletionSource` umesto `Thread.Sleep`.

## Struktura projekta

```
IndustrialProcessingSystem/
├── src/
│   ├── IndustrialProcessingSystem.Core/        # Modeli, interfejsi, eventi
│   ├── IndustrialProcessingSystem.Services/    # Sva logika sistema
│   │   ├── Collections/                        # JobPriorityQueue
│   │   ├── Configuration/                      # SystemConfig, XmlConfigReader
│   │   ├── Logging/                            # JobLogger (async Channel)
│   │   ├── Processors/                         # Prime i IO procesori
│   │   ├── Reporting/                          # ReportGenerator (LINQ + XML)
│   │   └── ProcessingSystem.cs                 # Glavni servis
│   └── IndustrialProcessingSystem.Console/     # Entry point
└── tests/
    └── IndustrialProcessingSystem.Tests/       # xUnit testovi
```

## Arhitekturalne odluke

**Thread pool vs dedicated threads** — sistem koristi .NET ThreadPool (`Task.Run`) za dispatch, ali `WorkerCount` se poštuje kroz brojanje slobodnih slotova (`_freeSlots`). Ovo eliminiše overhead kreiranja niti dok zadržava kontrolu nad konkurentnošću.

**Scheduler nit** — jedna dedicated nit čita snapshot iz `JobPriorityQueue` (kratki `ReadLock`), donosi odluke o dispatchingu bez držanja locka, i oslobađa slotove kroz `Interlocked.Add`. Wakes up na `SemaphoreSlim` signal pri novom poslu ili oslobođenim slotovima.

**Idempotentnost** — `ConcurrentDictionary<Guid, JobHandle>` čuva handle za svaki submitovani posao. Isti `Id` uvek vraća isti handle bez ponovnog izvršavanja.

**Retry** — pri svakom fail-u posao se ponovo enqueue-uje sa svežim deadline-om (resetovanje). Nakon trećeg fail-a `TCS.SetCanceled()` i `ABORT` u logu.

**Logging** — `Channel<string>` kao unbounded producer-consumer red za log poruke. Pisanje u fajl je sekvencijalno na jednoj niti, nikad ne blokira producer.
