/// F# port of compiler-qualification/runtime-performance/workloads/compute.
/// Same scenarios, arguments, output lines and exact checksums: computation
/// with no library work and no allocation in the timed region.
module ComputeKernels

open System
open System.Diagnostics
open System.Runtime.CompilerServices

type IKernel =
    /// Restores the kernel's input; not timed.
    abstract Reset: unit -> unit
    /// The timed work.
    abstract Run: unit -> unit
    abstract Checksum: unit -> int64

// An explicit struct rather than a record: records generate a printf-based
// ToString that would pull FSharp.Core's formatting code into the Wasm.
[<Struct; NoEquality; NoComparison>]
type Body =
    val mutable X: float
    val mutable Y: float
    val mutable Z: float
    val mutable Vx: float
    val mutable Vy: float
    val mutable Vz: float
    val mutable Mass: float

// ---- n-body: the Computer Language Benchmarks Game simulation --------------

[<Literal>]
let private DaysPerYear = 365.24

[<Literal>]
let private TimeStep = 0.01

let private solarMass = 4.0 * Math.PI * Math.PI

let private planet x y z vx vy vz mass =
    let mutable b = Body()
    b.X <- x
    b.Y <- y
    b.Z <- z
    b.Vx <- vx * DaysPerYear
    b.Vy <- vy * DaysPerYear
    b.Vz <- vz * DaysPerYear
    b.Mass <- mass * solarMass
    b

let private createSystem () =
    let mutable sun = Body()
    sun.Mass <- solarMass
    let bodies =
        [| sun
           planet 4.84143144246472090e+00 -1.16032004402742839e+00 -1.03622044471123109e-01
               1.66007664274403694e-03 7.69901118419740425e-03 -6.90460016972063023e-05
               9.54791938424326609e-04
           planet 8.34336671824457987e+00 4.12479856412430479e+00 -4.03523417114321381e-01
               -2.76742510726862411e-03 4.99852801234917238e-03 2.30417297573763929e-05
               2.85885980666130812e-04
           planet 1.28943695621391310e+01 -1.51111514016986312e+01 -2.23307578892655734e-01
               2.96460137564761618e-03 2.37847173959480950e-03 -2.96589568540237556e-05
               4.36624404335156298e-05
           planet 1.53796971148509165e+01 -2.59193146099879641e+01 1.79258772950371181e-01
               2.68067772490389322e-03 1.62824170038242295e-03 -9.51592254519715870e-05
               5.15138902046611451e-05 |]
    // Give the Sun the momentum that makes the system's total zero.
    let mutable px = 0.0
    let mutable py = 0.0
    let mutable pz = 0.0
    for body in bodies do
        px <- px + body.Vx * body.Mass
        py <- py + body.Vy * body.Mass
        pz <- pz + body.Vz * body.Mass
    bodies.[0].Vx <- -px / solarMass
    bodies.[0].Vy <- -py / solarMass
    bodies.[0].Vz <- -pz / solarMass
    bodies

[<MethodImpl(MethodImplOptions.NoInlining)>]
let advance (bodies: Body[]) (steps: int) =
    for _ in 1..steps do
        for i in 0 .. bodies.Length - 1 do
            let a = &bodies.[i]
            for j in i + 1 .. bodies.Length - 1 do
                let b = &bodies.[j]
                let dx = a.X - b.X
                let dy = a.Y - b.Y
                let dz = a.Z - b.Z
                let distanceSquared = dx * dx + dy * dy + dz * dz
                let magnitude = TimeStep / (distanceSquared * Math.Sqrt distanceSquared)
                let aScale = a.Mass * magnitude
                let bScale = b.Mass * magnitude
                a.Vx <- a.Vx - dx * bScale
                a.Vy <- a.Vy - dy * bScale
                a.Vz <- a.Vz - dz * bScale
                b.Vx <- b.Vx + dx * aScale
                b.Vy <- b.Vy + dy * aScale
                b.Vz <- b.Vz + dz * aScale
        for i in 0 .. bodies.Length - 1 do
            let body = &bodies.[i]
            body.X <- body.X + TimeStep * body.Vx
            body.Y <- body.Y + TimeStep * body.Vy
            body.Z <- body.Z + TimeStep * body.Vz

[<MethodImpl(MethodImplOptions.NoInlining)>]
let energy (bodies: Body[]) =
    let mutable e = 0.0
    for i in 0 .. bodies.Length - 1 do
        let a = &bodies.[i]
        e <- e + 0.5 * a.Mass * (a.Vx * a.Vx + a.Vy * a.Vy + a.Vz * a.Vz)
        for j in i + 1 .. bodies.Length - 1 do
            let b = &bodies.[j]
            let dx = a.X - b.X
            let dy = a.Y - b.Y
            let dz = a.Z - b.Z
            e <- e - a.Mass * b.Mass / Math.Sqrt(dx * dx + dy * dy + dz * dz)
    e

[<Sealed>]
type NBody(steps: int) =
    let initial = createSystem ()
    let bodies: Body[] = Array.zeroCreate 5

    interface IKernel with
        member _.Reset() = Array.Copy(initial, bodies, initial.Length)
        member _.Run() = advance bodies steps
        member _.Checksum() = BitConverter.DoubleToInt64Bits(energy bodies)

// ---- Quicksort of 100,000 integers -----------------------------------------

[<Literal>]
let private SortLength = 100_000

let private sortSeed () =
    let values: int[] = Array.zeroCreate SortLength
    let mutable state = 0x9E3779B9u
    for i in 0 .. values.Length - 1 do
        state <- state ^^^ (state <<< 13)
        state <- state ^^^ (state >>> 17)
        state <- state ^^^ (state <<< 5)
        values.[i] <- int state
    values

let inline private swap (values: int[]) a b =
    let t = values.[a]
    values.[a] <- values.[b]
    values.[b] <- t

let rec private sort (values: int[]) low high =
    let mutable low = low
    let mutable high = high
    // Recurse into the smaller side and loop over the larger, so depth stays logarithmic.
    while high - low >= 16 do
        let middle = low + (high - low) / 2
        if values.[middle] < values.[low] then swap values middle low
        if values.[high] < values.[low] then swap values high low
        if values.[high] < values.[middle] then swap values high middle
        let pivot = values.[middle]
        let mutable i = low
        let mutable j = high
        while i <= j do
            while values.[i] < pivot do
                i <- i + 1
            while values.[j] > pivot do
                j <- j - 1
            if i <= j then
                swap values i j
                i <- i + 1
                j <- j - 1
        if j - low < high - i then
            sort values low j
            low <- i
        else
            sort values i high
            high <- j
    for i in low + 1 .. high do
        let value = values.[i]
        let mutable j = i - 1
        while j >= low && values.[j] > value do
            values.[j + 1] <- values.[j]
            j <- j - 1
        values.[j + 1] <- value

[<MethodImpl(MethodImplOptions.NoInlining)>]
let sortRounds (source: int[]) (values: int[]) rounds =
    let mutable checksum = 0L
    for round in 0 .. rounds - 1 do
        // Each round sorts different values, so no round repeats another's work.
        for i in 0 .. values.Length - 1 do
            values.[i] <- source.[i] ^^^ round
        sort values 0 (values.Length - 1)
        for i in 0 .. values.Length - 1 do
            checksum <- checksum * 31L + int64 values.[i]
    checksum

[<Sealed>]
type Sorting(rounds: int) =
    let source = sortSeed ()
    let values: int[] = Array.zeroCreate SortLength
    let mutable checksum = 0L

    interface IKernel with
        member _.Reset() = checksum <- 0L
        member _.Run() = checksum <- sortRounds source values rounds
        member _.Checksum() = checksum

// ---- Table-driven CRC-32 over 1 MiB ----------------------------------------

let private crcTable () =
    let table: uint32[] = Array.zeroCreate 256
    for i in 0u .. 255u do
        let mutable value = i
        for _ in 1..8 do
            value <- if value &&& 1u <> 0u then 0xEDB88320u ^^^ (value >>> 1) else value >>> 1
        table.[int i] <- value
    table

let private crcSeed () =
    let data: byte[] = Array.zeroCreate (1 <<< 20)
    let mutable state = 0x2545F491u
    for i in 0 .. data.Length - 1 do
        state <- state ^^^ (state <<< 13)
        state <- state ^^^ (state >>> 17)
        state <- state ^^^ (state <<< 5)
        data.[i] <- byte state
    data

[<MethodImpl(MethodImplOptions.NoInlining)>]
let crcPasses (table: uint32[]) (data: byte[]) passes =
    let mutable crc = 0u
    for _ in 1..passes do
        let mutable value = ~~~crc
        for i in 0 .. data.Length - 1 do
            value <- table.[int ((value ^^^ uint32 data.[i]) &&& 0xFFu)] ^^^ (value >>> 8)
        crc <- ~~~value
    crc

[<Sealed>]
type Crc32(passes: int) =
    let table = crcTable ()
    let data = crcSeed ()
    let mutable crc = 0u

    interface IKernel with
        member _.Reset() = crc <- 0u
        member _.Run() = crc <- crcPasses table data passes
        member _.Checksum() = int64 crc

// ---- Driver ----------------------------------------------------------------

[<EntryPoint>]
let main args =
    let mutable count = 0
    if args.Length <> 2 || not (Int32.TryParse(args.[1], &count)) || count <= 0 then
        2
    else
        let kernel: IKernel =
            match args.[0] with
            | "nbody" -> NBody(count)
            | "sort" -> Sorting(count)
            | "crc32" -> Crc32(count)
            | _ -> Unchecked.defaultof<_>
        if isNull (box kernel) then
            3
        else
            // Run the timed work untimed first, so a tiered JIT reaches its optimized code.
            for _ in 1..3 do
                kernel.Reset()
                kernel.Run()
            kernel.Reset()
            Stopwatch.GetTimestamp() |> ignore
            GC.CollectionCount(0) |> ignore
            GC.GetTotalAllocatedBytes(true) |> ignore

            let collectionsBefore = GC.CollectionCount(0)
            let allocatedBefore = GC.GetTotalAllocatedBytes(true)
            let start = Stopwatch.GetTimestamp()
            kernel.Run()
            let elapsed = Stopwatch.GetTimestamp() - start
            let allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore
            let collections = GC.CollectionCount(0) - collectionsBefore

            Console.WriteLine(
                String.Join(
                    "|",
                    [| "COMPUTE"; args.[0]; elapsed.ToString(); Stopwatch.Frequency.ToString()
                       kernel.Checksum().ToString() |]))
            Console.WriteLine(
                String.Join(
                    "|",
                    [| "GC"; collections.ToString(); allocated.ToString(); GC.GetTotalMemory(false).ToString() |]))
            if allocatedBefore <= 0L then 6
            elif allocated <> 0L || collections <> 0 then 4
            else 0
