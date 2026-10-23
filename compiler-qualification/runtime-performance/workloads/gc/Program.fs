/// F# port of compiler-qualification/runtime-performance/workloads/gc.
/// Same scenarios, arguments, output line and checksums: object-graph builds a
/// retained graph of small objects, ui-churn replaces a slice of a retained
/// array every frame.
module GcWorkloads

open System
open System.Diagnostics
open System.Runtime.CompilerServices

// A plain class, not a record: records generate printf-based ToString and
// structural equality that this workload does not need.
[<Sealed; AllowNullLiteral>]
type ProbeObject(account: string, cents: int) =
    member _.Account = account
    member _.Cents = cents

[<Sealed; AllowNullLiteral>]
type Result
    (
        name: string,
        elapsedTicks: int64,
        checksum: int64,
        allocatedBytes: int64,
        automaticCollections: int,
        retainedBytes: int64,
        retainedModels: int
    ) =
    member _.Line =
        String.Join(
            "|",
            [| "GCWORK"; name; elapsedTicks.ToString(); Stopwatch.Frequency.ToString(); checksum.ToString()
               allocatedBytes.ToString(); automaticCollections.ToString(); retainedBytes.ToString()
               retainedModels.ToString() |]
        )

let inline private xorshift (random: uint32) =
    let r = random ^^^ (random <<< 13)
    let r = r ^^^ (r >>> 17)
    r ^^^ (r <<< 5)

let inline private account (random: uint32) =
    String.Concat(" acct-", (random % 2048u).ToString(), " ")

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private validateGraph (objects: ProbeObject[]) =
    let mutable random = 1u
    for value in objects do
        random <- xorshift random
        if isNull value || value.Cents <> int (random % 100_000u) + 1 || value.Account <> account random then
            raise (InvalidOperationException "Retained graph validation failed.")

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private validateUi (retained: ProbeObject[]) frames =
    let identities: int[] = Array.zeroCreate retained.Length
    let cents: int[] = Array.zeroCreate retained.Length
    for index in 0 .. retained.Length - 1 do
        identities.[index] <- index
        cents.[index] <- index
    for frame in 0 .. frames - 1 do
        for index in 0..63 do
            let slot = (frame * 67 + index) &&& 2047
            identities.[slot] <- (frame * 31 + index) &&& 2047
            cents.[slot] <- frame + index
    for index in 0 .. retained.Length - 1 do
        let value = retained.[index]
        if isNull value || value.Cents <> cents.[index] || value.Account <> "node-" + identities.[index].ToString() then
            raise (InvalidOperationException "Retained UI validation failed.")

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private objectGraph count =
    let objects: ProbeObject[] = Array.zeroCreate count
    let mutable random = 1u
    let collectionsBefore = GC.CollectionCount(0)
    let allocatedBefore = GC.GetTotalAllocatedBytes()
    let started = Stopwatch.GetTimestamp()
    for index in 0 .. count - 1 do
        random <- xorshift random
        objects.[index] <- ProbeObject(account random, int (random % 100_000u) + 1)
    let last = objects.[count - 1]
    let checksum = objects.Length + last.Cents + last.Account.Length
    let elapsed = Stopwatch.GetTimestamp() - started
    let automatic = GC.CollectionCount(0) - collectionsBefore
    let allocated = GC.GetTotalAllocatedBytes() - allocatedBefore
    GC.Collect()
    let retainedBytes = GC.GetTotalMemory(false)
    validateGraph objects
    GC.KeepAlive(objects)
    Result("object-graph", elapsed, int64 checksum, allocated, automatic, retainedBytes, count)

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private uiChurn frames =
    let retained: ProbeObject[] = Array.zeroCreate 2048
    for index in 0 .. retained.Length - 1 do
        retained.[index] <- ProbeObject("node-" + index.ToString(), index)
    let mutable checksum = 0L
    let collectionsBefore = GC.CollectionCount(0)
    let allocatedBefore = GC.GetTotalAllocatedBytes()
    let started = Stopwatch.GetTimestamp()
    for frame in 0 .. frames - 1 do
        let transient: ProbeObject[] = Array.zeroCreate 512
        for index in 0 .. transient.Length - 1 do
            let identity = (frame * 31 + index) &&& 2047
            transient.[index] <- ProbeObject("node-" + identity.ToString(), frame + index)
        for index in 0..63 do
            retained.[(frame * 67 + index) &&& 2047] <- transient.[index]
        checksum <-
            checksum
            + int64 (transient.[transient.Length - 1].Cents + retained.[frame &&& 2047].Account.Length)
    let elapsed = Stopwatch.GetTimestamp() - started
    let automatic = GC.CollectionCount(0) - collectionsBefore
    let allocated = GC.GetTotalAllocatedBytes() - allocatedBefore
    GC.Collect()
    let retainedBytes = GC.GetTotalMemory(false)
    validateUi retained frames
    GC.KeepAlive(retained)
    Result("ui-churn", elapsed, checksum, allocated, automatic, retainedBytes, retained.Length)

[<EntryPoint>]
let main args =
    let mutable count = 0
    if args.Length <> 2 || not (Int32.TryParse(args.[1], &count)) || count <= 0 then
        2
    else
        let result =
            match args.[0] with
            | "object-graph" -> objectGraph count
            | "ui-churn" -> uiChurn count
            | _ -> null
        if isNull result then
            3
        else
            Console.WriteLine(result.Line)
            0
