/// F# port of compiler-qualification/runtime-performance/workloads/transaction.
/// Same scenarios, arguments, output lines, checksum and return codes:
/// transactions are validated, deduplicated by id and their decimal amounts,
/// less a rounded fee, summed per normalized account.
module Transaction

open System
open System.Collections.Generic
open System.Diagnostics
open System.Runtime.CompilerServices

[<Literal>]
let private AccountCount = 2048

[<Sealed; AllowNullLiteral>]
type ProbeTransaction(id: int, account: string, cents: int) =
    member _.Id = id
    member _.Account = account
    member _.Cents = cents

[<Struct; NoEquality; NoComparison>]
type Counts =
    val Accepted: int
    val Rejected: int
    val Duplicates: int
    new(accepted, rejected, duplicates) = { Accepted = accepted; Rejected = rejected; Duplicates = duplicates }

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private aggregate
    (transactions: ProbeTransaction[])
    (normalized: string[])
    (seen: HashSet<int>)
    (balances: Dictionary<string, decimal>)
    =
    let mutable accepted = 0
    let mutable rejected = 0
    let mutable duplicates = 0
    for index in 0 .. transactions.Length - 1 do
        let transaction = transactions.[index]
        if transaction.Cents <= 0 || transaction.Account.Length = 0 then
            rejected <- rejected + 1
        elif not (seen.Add transaction.Id) then
            duplicates <- duplicates + 1
        else
            let account = normalized.[index]
            // Decimal(100), not 100m: F# builds every decimal literal with the
            // five-argument constructor, which validates its scale each time.
            let amount = Decimal(transaction.Cents) / Decimal(100)
            let fee = Decimal.Round(amount * 0.0125m, 2, MidpointRounding.ToEven)
            // TryGetValue overwrites it, so skip the 0m constructor call.
            let mutable balance = Unchecked.defaultof<decimal>
            balances.TryGetValue(account, &balance) |> ignore
            balances.[account] <- balance + amount - fee
            accepted <- accepted + 1
    Counts(accepted, rejected, duplicates)

let private checksumOf (counts: Counts) (balances: Dictionary<string, decimal>) =
    let mutable checksum =
        (int64 counts.Accepted <<< 32) ^^^ (int64 counts.Rejected <<< 16)
        ^^^ int64 (uint32 counts.Duplicates) ^^^ int64 balances.Count
    for balance in balances.Values do
        checksum <- checksum ^^^ int64 (Decimal.GetBits balance).[0]
    checksum

let private createTransactions count (seed: uint32) =
    let transactions: ProbeTransaction[] = Array.zeroCreate count
    let mutable random = seed
    for index in 0 .. transactions.Length - 1 do
        random <- random ^^^ (random <<< 13)
        random <- random ^^^ (random >>> 17)
        random <- random ^^^ (random <<< 5)
        let account = String.Concat(" acct-", (random % uint32 AccountCount).ToString(), " ")
        let cents = int (random % 100_000u) + 1
        let cents = if index % 37 = 0 then -cents else cents
        let id = if index > 0 && index % 10 = 0 then index - 1 else index
        transactions.[index] <- ProbeTransaction(id, account, cents)
    transactions

let private normalize (transactions: ProbeTransaction[]) =
    let normalized: string[] = Array.zeroCreate transactions.Length
    for i in 0 .. transactions.Length - 1 do
        normalized.[i] <- transactions.[i].Account.Trim().ToUpperInvariant()
    normalized

let private report (scenario: string) (elapsed: int64) (checksum: int64) (collections: int) (allocated: int64) =
    Console.WriteLine(
        String.Join("|", [| "PROBE"; scenario; elapsed.ToString(); Stopwatch.Frequency.ToString(); checksum.ToString() |]))
    Console.WriteLine(
        String.Join("|", [| "GC"; collections.ToString(); allocated.ToString(); GC.GetTotalMemory(false).ToString() |]))

/// Times everything the preallocated control moves out: creating the
/// transactions, normalizing every account and growing the containers.
let private full count seed =
    let collectionsBefore = GC.CollectionCount(0)
    let allocatedBefore = GC.GetTotalAllocatedBytes(true)
    let start = Stopwatch.GetTimestamp()
    let transactions = createTransactions count seed
    let normalized = normalize transactions
    let seen = HashSet<int>()
    let balances = Dictionary<string, decimal>(StringComparer.Ordinal)
    let counts = aggregate transactions normalized seen balances
    let elapsed = Stopwatch.GetTimestamp() - start
    let allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore
    let collections = GC.CollectionCount(0) - collectionsBefore
    let checksum = checksumOf counts balances
    report "full" elapsed checksum collections allocated
    if count = 100000 && seed = 101u && checksum <> 377261514315152L then 5 else 0

let private preallocated count seed =
    let transactions = createTransactions count seed
    let normalized = normalize transactions
    let seen = HashSet<int>(count)
    let balances = Dictionary<string, decimal>(AccountCount, StringComparer.Ordinal)
    // Warm the exact path and containers; equal strings stay distinct.
    let expected = checksumOf (aggregate transactions normalized seen balances) balances
    for _ in 1..8 do
        seen.Clear()
        balances.Clear()
        aggregate transactions normalized seen balances |> ignore
    seen.Clear()
    balances.Clear()
    Stopwatch.GetTimestamp() |> ignore
    GC.CollectionCount(0) |> ignore
    GC.GetTotalAllocatedBytes(true) |> ignore

    let collectionsBefore = GC.CollectionCount(0)
    let allocatedBefore = GC.GetTotalAllocatedBytes(true)
    let start = Stopwatch.GetTimestamp()
    let counts = aggregate transactions normalized seen balances
    let elapsed = Stopwatch.GetTimestamp() - start
    let allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore
    let collections = GC.CollectionCount(0) - collectionsBefore

    // The checksum may allocate, so it runs after both counters are read.
    let checksum = checksumOf counts balances
    report "preallocated" elapsed checksum collections allocated
    GC.KeepAlive(transactions)
    GC.KeepAlive(normalized)
    GC.KeepAlive(seen)
    GC.KeepAlive(balances)
    if allocatedBefore <= 0L then 6
    elif allocated <> 0L || collections <> 0 then 4
    elif checksum <> expected || count = 100000 && seed = 101u && checksum <> 377261514315152L then 5
    else 0

[<EntryPoint>]
let main args =
    let mutable count = 0
    let mutable seed = 0u
    if args.Length <> 3 || (args.[0] <> "preallocated" && args.[0] <> "full")
       || not (Int32.TryParse(args.[1], &count)) || count <= 0
       || not (UInt32.TryParse(args.[2], &seed)) || seed = 0u then
        2
    elif args.[0] = "full" then
        full count seed
    else
        preallocated count seed
