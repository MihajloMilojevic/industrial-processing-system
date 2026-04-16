# Industrial Processing System — Specifikacija

## 1. Pregled sistema

Producer-consumer sistem za asinhronu obradu industrijskih poslova sa prioritetima, event-driven logom i periodičnim izveštajima. Sistem se inicijalizuje iz XML konfiguracionog fajla.

---

## 2. Struktura projekta

```
IndustrialProcessingSystem/
├── IndustrialProcessingSystem.sln
├── src/
│   ├── IndustrialProcessingSystem.Core/
│   │   ├── Models/
│   │   │   ├── Job.cs
│   │   │   └── JobHandle.cs
│   │   ├── Enums/
│   │   │   └── JobType.cs
│   │   ├── Interfaces/
│   │   │   └── IProcessingSystem.cs
│   │   └── Events/
│   │       └── JobEventArgs.cs
│   ├── IndustrialProcessingSystem.Services/
│   │   ├── ProcessingSystem.cs
│   │   ├── Processors/
│   │   │   ├── IJobProcessor.cs
│   │   │   ├── PrimeJobProcessor.cs
│   │   │   └── IoJobProcessor.cs
│   │   ├── Collections/
│   │   │   └── JobPriorityQueue.cs
│   │   ├── Configuration/
│   │   │   ├── SystemConfig.cs
│   │   │   ├── JobConfig.cs
│   │   │   └── XmlConfigReader.cs
│   │   ├── Reporting/
│   │   │   └── ReportGenerator.cs
│   │   └── Logging/
│   │       └── JobLogger.cs
│   └── IndustrialProcessingSystem.App/
│       ├── Program.cs
│       └── SystemConfig.xml
└── tests/
    └── IndustrialProcessingSystem.Tests/
        ├── ProcessingSystemTests.cs
        └── JobProcessorTests.cs
```

Zavisnosti: `App → Services → Core`. `Tests` referencira `Core` i `Services`.

---

## 3. Domenski modeli (Core)

### Job
```csharp
public class Job
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public JobType Type { get; init; }
    public string Payload { get; init; } = string.Empty;
    public int Priority { get; init; }  // manji broj = veći prioritet
}
```

### JobHandle
```csharp
public class JobHandle
{
    public Guid Id { get; init; }
    public Task<int> Result { get; init; }  // TCS koji se resoluje kad posao završi
}
```

### JobType (enum)
```csharp
public enum JobType { Prime, IO }
```

### JobEventArgs
```csharp
public class JobCompletedEventArgs : EventArgs
{
    public Guid JobId { get; init; }
    public int Result { get; init; }
    public DateTime CompletedAt { get; init; }
}

public class JobFailedEventArgs : EventArgs
{
    public Guid JobId { get; init; }
    public int AttemptNumber { get; init; }
    public Exception? Exception { get; init; }
    public DateTime FailedAt { get; init; }
}
```

### IProcessingSystem
```csharp
public interface IProcessingSystem
{
    event EventHandler<JobCompletedEventArgs> JobCompleted;
    event EventHandler<JobFailedEventArgs> JobFailed;

    JobHandle Submit(Job job);
    IEnumerable<Job> GetTopJobs(int n);
    Job? GetJob(Guid id);
}
```

---

## 4. Konfiguracija

### SystemConfig.xml format
```xml
<SystemConfig>
    <WorkerCount>5</WorkerCount>
    <MaxQueueSize>100</MaxQueueSize>
    <JobTimeoutSeconds>2</JobTimeoutSeconds>
    <PrioritySkipThreshold>0.5</PrioritySkipThreshold>
    <Jobs>
        <Job Type="Prime" Payload="numbers:10_000,threads:3" Priority="1"/>
        <Job Type="IO" Payload="delay:1_000" Priority="3"/>
    </Jobs>
</SystemConfig>
```

- `JobTimeoutSeconds` — koliko sekundi posao ima od Submit-a do završetka. Default: `2`.
- `PrioritySkipThreshold` — ako je posao odčekao više od `threshold × timeout` vremena, scheduler ga ne sme preskočiti čak i ako nema dovoljno slobodnih slotova. Default: `0.5` (50% od timeout-a).

### SystemConfig (POCO sa XmlSerializer atributima)
```csharp
[XmlRoot("SystemConfig")]
public class SystemConfig
{
    [XmlElement("WorkerCount")]
    public int WorkerCount { get; set; }

    [XmlElement("MaxQueueSize")]
    public int MaxQueueSize { get; set; }

    [XmlElement("JobTimeoutSeconds")]
    public double JobTimeoutSeconds { get; set; } = 2.0;

    [XmlElement("PrioritySkipThreshold")]
    public double PrioritySkipThreshold { get; set; } = 0.5;

    [XmlArray("Jobs")]
    [XmlArrayItem("Job")]
    public List<JobConfig> Jobs { get; set; } = [];

    // Izvedene vrednosti — koristi ProcessingSystem
    public TimeSpan JobTimeout => TimeSpan.FromSeconds(JobTimeoutSeconds);
    public TimeSpan SkipThreshold => TimeSpan.FromSeconds(JobTimeoutSeconds * PrioritySkipThreshold);
}

public class JobConfig
{
    [XmlAttribute("Type")]   public JobType Type { get; set; }
    [XmlAttribute("Payload")] public string Payload { get; set; } = string.Empty;
    [XmlAttribute("Priority")] public int Priority { get; set; }

    public Job ToJob() => new Job { Type = Type, Payload = Payload, Priority = Priority };
}
```

Napomena: `Jobs` lista iz konfiguracije se **ne prosleđuje** u `ProcessingSystem`. Čita se u `Program.cs` i submituje kroz standardni `Submit()` mehanizam zajedno sa ostalim producer nitima.

### XmlConfigReader
```csharp
public static class XmlConfigReader
{
    private static readonly XmlSerializer Serializer = new(typeof(SystemConfig));

    public static SystemConfig Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Serializer.Deserialize(stream) as SystemConfig
            ?? throw new InvalidDataException("Failed to deserialize config.");
    }
}
```

---

## 5. Model niti i upravljanje resursima

### Ključna odluka

`WorkerCount` iz konfiguracije predstavlja **ukupan broj niti** dostupnih celom sistemu — obuhvata sve: dispatcher niti, `Parallel.For` niti unutar Prime poslova i sve ostalo što može da se dogodi (ne računajući producer niti).

Niti se posmatraju kao resurs iz globalnog bazena veličine `WorkerCount`.

### Šta ProcessingSystem prima iz konfiguracije

`ProcessingSystem` konstruktor prima ceo `SystemConfig` objekat (samo relevantna polja):
- `WorkerCount` — kapacitet globalnog bazena niti
- `MaxQueueSize` — maksimalan broj poslova u redu čekanja
- `JobTimeout` — ukupno vreme koje posao ima od Submit-a (`TimeSpan`)
- `SkipThreshold` — koliko dugo posao mora da čeka pre nego što scheduler mora da ga pokrene (`TimeSpan`)

### Bazen niti

Implementira se kroz `SemaphoreSlim(workerCount, workerCount)`. Svaki posao uzima onoliko slotova koliko mu treba:

| Tip posla | Slotovi |
|-----------|---------|
| IO        | 1       |
| Prime     | `threads` iz Payload-a (clamped na [1,8]) |

### Atomično uzimanje slotova

Posao **ne sme da počne** dok ne može da uzme sve potrebne slotove odjednom. Nema parcijalnog dodeljivanja.

```
WorkerCount=5, slobodnih=1:
  Prime threads:3 → čeka dok se ne oslobode 3 slota
  IO              → može odmah (treba 1 slot)
```

### Scheduler nit

Jedna dedicated nit koja:
1. Uzima snapshot reda kroz `GetSnapshot()` (`ReadLock` samo dok se pravi kopija)
2. Prolazi kroz snapshot sortiran po prioritetu
3. Za svaki posao primenjuje logiku preskakanja (videti ispod)
4. Ako posao može da se pokrene — uzme slotove iz semafora i pokrene ga
5. Pokreće posao kao `Task.Run` — `Task.Run` nit se **ne računa** kao slot

### Logika preskakanja i threshold

```csharp
foreach (var entry in snapshot)
{
    var waited   = DateTime.UtcNow - entry.EnqueuedAt;
    var canRun   = freeSlots >= SlotsNeeded(entry);
    var mustRun  = waited >= _config.SkipThreshold;  // čekao 50% od timeout-a

    if (!canRun && !mustRun)
        continue;  // preskoči — nije čekao dovoljno dugo, nema slobodnih slotova

    if (!canRun && mustRun)
    {
        // posao je čekao previše — blokiraj scheduler dok se ne oslobode slotovi
        // u ovom slučaju se ne preskaču niti poslovi nižeg prioriteta
        WaitForSlots(SlotsNeeded(entry));
    }

    // pokreni posao
    freeSlots -= SlotsNeeded(entry);
    _queue.TryRemove(entry);
    DispatchJob(entry);
}
```

Efekat sa defaultom `SkipThreshold = 1s` (50% od 2s timeout-a):

```
t=0.0s  Prime threads:3 uđe u red, freeSlots=1 → preskočen (čekao 0s < 1s)
t=0.0s  IO uđe u red, freeSlots=1 → pokrenut odmah
t=1.0s  Prime threads:3 čekao 1s = threshold → scheduler blokira dok se ne oslobode 3 slota
t=1.0s  IO ne može da upadne ispred Prime-a više
```

Threshold sprečava starvation bez aging-a: svaki posao ima garantovano vreme čekanja ≤ `SkipThreshold` pre nego što dobija prednost nad nižim prioritetima.

### Parallel.For za Prime

`Task.Run` preuzima dispatch i odmah pokreće `Parallel.For`:

```csharp
_ = Task.Run(() =>
{
    try
    {
        Parallel.For(0, limit, new ParallelOptions
        {
            MaxDegreeOfParallelism = threads  // tačno onoliko slotova koliko je rezervisano
        }, i => { /* računanje */ });
    }
    finally
    {
        _semaphore.Release(threads);
        SignalScheduler();
    }
});
```

`Task.Run` nit je kratkotrajna (samo startuje `Parallel.For`) i ne ulazi u bazen.

---

## 6. Obrada poslova

### Payload format

| Tip   | Format                          | Primer                    |
|-------|---------------------------------|---------------------------|
| Prime | `numbers:{N},threads:{T}`       | `numbers:10_000,threads:3` |
| IO    | `delay:{ms}`                    | `delay:1_000`             |

Payload je uvek u validnom formatu — ne treba validacija.

### PrimeJobProcessor

Izračunava broj prostih brojeva do `numbers`. Koristi `Parallel.For` sa `MaxDegreeOfParallelism = threads`. Broj niti se clampa na interval [1, 8] pri parsiranju. Vraća `int` — broj prostih.

### IoJobProcessor

`Thread.Sleep(delay)`. Vraća nasumičan broj između 0 i 100.

---

## 7. JobPriorityQueue

Thread-safe priority queue koji **interno enkapsulira `ReaderWriterLockSlim`** — pozivalac ne zna i ne brine o lockovanju.

Koristi `SortedSet<JobEntry>` sa custom `IComparer` kao internu strukturu — `SortedSet` održava sortiranost pri svakom umetanju u O(log n), bez potrebe za ručnim sortiranjem pri čitanju.

```csharp
internal class JobPriorityQueue : IDisposable
{
    private readonly SortedSet<JobEntry> _set;
    private readonly ReaderWriterLockSlim _lock = new();

    public JobPriorityQueue()
    {
        _set = new SortedSet<JobEntry>(JobEntryComparer.Instance);
    }

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try   { return _set.Count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    public bool TryEnqueue(JobEntry entry)
    {
        _lock.EnterWriteLock();
        try   { return _set.Add(entry); }
        finally { _lock.ExitWriteLock(); }
    }

    public bool TryRemove(JobEntry entry)
    {
        _lock.EnterWriteLock();
        try   { return _set.Remove(entry); }
        finally { _lock.ExitWriteLock(); }
    }

    // Vraća snapshot — scheduler iterira bez držanja locka
    public IReadOnlyList<JobEntry> GetSnapshot()
    {
        _lock.EnterReadLock();
        try   { return _set.ToList(); }
        finally { _lock.ExitReadLock(); }
    }

    public IEnumerable<Job> GetTopJobs(int n)
    {
        _lock.EnterReadLock();
        try   { return _set.Take(n).Select(e => e.Job).ToList(); }
        finally { _lock.ExitReadLock(); }
    }

    public JobEntry? FindById(Guid id)
    {
        _lock.EnterReadLock();
        try   { return _set.FirstOrDefault(e => e.Job.Id == id); }
        finally { _lock.ExitReadLock(); }
    }

    public void Dispose() => _lock.Dispose();
}
```

### Comparer

```csharp
internal class JobEntryComparer : IComparer<JobEntry>
{
    public static readonly JobEntryComparer Instance = new();

    public int Compare(JobEntry? x, JobEntry? y)
    {
        if (x is null || y is null) return 0;

        // manji Priority broj = veći prioritet = dolazi prvi
        int cmp = x.Job.Priority.CompareTo(y.Job.Priority);
        if (cmp != 0) return cmp;

        // isti prioritet → stariji posao ide prvi (FIFO unutar iste grupe)
        cmp = x.Deadline.CompareTo(y.Deadline);
        if (cmp != 0) return cmp;

        // SortedSet zahteva konzistentan tiebreaker da ne odbaci duplikate
        return x.Job.Id.CompareTo(y.Job.Id);
    }
}
```

### Zašto snapshot u scheduleru

Scheduler poziva `GetSnapshot()` koji drži `ReadLock` samo dok pravi kopiju liste, pa ga odmah pušta. Scheduler zatim iterira slobodno kroz snapshot bez ikakvog locka. Kada odluči koji posao može da se izvrši, poziva `TryRemove` koji uzima `WriteLock` samo na trenutak uklanjanja.

`ReadLock` dozvoljava više čitalaca istovremeno — `Submit` iz producer niti i `GetSnapshot` iz schedulera mogu da se izvršavaju paralelno. `WriteLock` je ekskuzivan i drži se samo tokom kratkih `TryEnqueue`/`TryRemove` operacija.

---

## 8. Submit i JobHandle

```csharp
public JobHandle Submit(Job job)
{
    lock (_queueLock)
    {
        // idempotentnost — isti Id ne sme biti izvršen više puta
        if (_executedIds.Contains(job.Id) || _queuedIds.Contains(job.Id))
            return _existingHandles[job.Id];

        // MaxQueueSize
        if (_queue.Count >= _maxQueueSize)
            throw new InvalidOperationException("Queue is full.");

        var tcs = new TaskCompletionSource<int>();
        var handle = new JobHandle { Id = job.Id, Result = tcs.Task };
        var entry = new JobEntry { Job = job, Tcs = tcs, Deadline = DateTime.UtcNow.Add(_config.JobTimeout) };

        _queue.Enqueue(entry);  // priority queue
        _queuedIds.Add(job.Id);
        _existingHandles[job.Id] = handle;

        return handle;
    }
}
```

---

## 9. Timeout i retry logika

### Timeout

Meri se **od trenutka Submit-a**. Svaki `JobEntry` nosi `Deadline = DateTime.UtcNow.AddSeconds(2)`.

Posao je "failed" ako:
- Scheduler ga vidi u redu, ali je `DateTime.UtcNow > Deadline` pre nego što počne
- Izvršavanje traje duže od preostalog vremena (`deadline - now` se prosleđuje kao `CancellationToken` timeout)

### Retry

```
Attempt 1 (originalni Submit): fail → retry
Attempt 2 (retry 1): fail → retry
Attempt 3 (retry 2): fail → ABORT, upiši u log, TCS.SetCanceled()
```

Deadline se **resetuje** na svakom retry-u (svaki pokušaj dobija punih 2 sekunde).

```csharp
if (entry.RetryCount < 2)
{
    var retryEntry = new JobEntry
    {
        Job = entry.Job,
        Tcs = entry.Tcs,                          // isti TCS = isti JobHandle kod klijenta
        Deadline = DateTime.UtcNow.Add(_config.JobTimeout), // resetovan deadline
        RetryCount = entry.RetryCount + 1
    };
    _queue.Enqueue(retryEntry);
}
else
{
    _logger.LogAbort(entry.Job.Id);
    entry.Tcs.SetCanceled();
}
```

---

## 10. Event sistem i logovanje

### Eventi

```csharp
event EventHandler<JobCompletedEventArgs> JobCompleted;
event EventHandler<JobFailedEventArgs> JobFailed;
```

Pretplata u `Program.cs` kroz lambda izraze:

```csharp
system.JobCompleted += async (_, e) => await logger.LogAsync(e);
system.JobFailed    += async (_, e) => await logger.LogAsync(e);
```

### Log format

```
[2026-04-16 14:32:01] [COMPLETED] 3f2a1b..., 1229
[2026-04-16 14:32:03] [FAILED]    7c8d2e..., attempt 1
[2026-04-16 14:32:05] [ABORT]     7c8d2e...
```

`JobLogger` upisuje **asinhrono** u fajl koristeći `StreamWriter` sa `AutoFlush` ili `Channel<string>` za thread-safe async upis.

---

## 11. Dodatne metode

```csharp
// Prvih N poslova po prioritetu iz trenutno aktivnog reda
IEnumerable<Job> GetTopJobs(int n);

// Vraća Job objekat za zadati Id (iz reda ili izvršenih)
Job? GetJob(Guid id);
```

---

## 12. Periodični izveštaj

Svaki minut, `System.Timers.Timer` okida `ReportGenerator` koji LINQ-om agregira:

```csharp
var report = completedJobs
    .GroupBy(j => j.Type)
    .Select(g => new
    {
        Type = g.Key,
        Count = g.Count(),
        AvgDurationMs = g.Average(j => j.DurationMs),
        FailedCount = g.Count(j => j.Failed)
    })
    .OrderBy(g => g.Type);
```

Izveštaj se upisuje u XML fajl. Čuva se poslednjih 10:

```csharp
var index = _reportCounter % 10;   // 0..9 kružno
var path = $"reports/report_{index}.xml";
_reportCounter++;
```

---

## 13. Program.cs

```csharp
var config = XmlConfigReader.Read("SystemConfig.xml");
var system = new ProcessingSystem(config.WorkerCount, config.MaxQueueSize);

// event pretplata
system.JobCompleted += async (_, e) => await logger.LogCompletedAsync(e);
system.JobFailed    += async (_, e) => await logger.LogFailedAsync(e);

// inicijalni poslovi iz konfiguracije
foreach (var jobConfig in config.Jobs)
    system.Submit(jobConfig.ToJob());

// producer niti (WorkerCount niti koje nasumično dodaju poslove)
var producers = Enumerable.Range(0, config.WorkerCount)
    .Select(_ => Task.Run(async () =>
    {
        while (true)
        {
            try
            {
                var job = RandomJobFactory.Create();
                var handle = system.Submit(job);
                var result = await handle.Result;
            }
            catch (InvalidOperationException)  { /* queue full */ }
            catch (OperationCanceledException) { /* job aborted */ }
            catch (Exception ex)               { /* log */ }

            await Task.Delay(Random.Shared.Next(100, 500));
        }
    }));

await Task.WhenAll(producers);
```

---

## 14. Testiranje

Koristi `xUnit`. Ne koristiti `Thread.Sleep` za čekanje rezultata — koristiti `TaskCompletionSource`, `SemaphoreSlim.WaitAsync`, ili `await handle.Result` direktno.

Ključni test slučajevi:
- Idempotentnost — isti `Id` ne sme biti dvaput u sistemu
- `MaxQueueSize` — submit odbijen kad je red pun
- Prioriteti — posao sa `Priority=1` mora biti obrađen pre `Priority=3`
- Retry — job koji uvek fail-uje treba da dobije `ABORT` posle 3 pokušaja
- Timeout — job koji traje duže od 2s treba da postane failed
