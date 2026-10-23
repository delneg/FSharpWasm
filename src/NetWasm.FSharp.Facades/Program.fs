module NetWasm.FSharp.Facades.Program

open System
open NetWasm.FSharp.Facades.ReferenceSet

[<EntryPoint>]
let main argv =
    match argv with
    | [| coreLibReference; referencePack; outputDirectory |] ->
        for name, forwarded, stubs in writeReferenceSet coreLibReference referencePack outputDirectory do
            printfn $"NetWasm F# facade {name}: {forwarded} CoreLib types forwarded, {stubs} stubs"
        0
    | _ ->
        eprintfn "usage: NetWasm.FSharp.Facades <NetWasm.CoreLib reference dll> <.NET reference pack dir> <output dir>"
        2
