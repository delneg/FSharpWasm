/// Rewrites a desktop-targeted assembly (an F# program or FSharp.Core, both
/// compiled against netstandard 2.1) so that every reference to a desktop
/// framework assembly points at NetWasm.CoreLib instead. NetWasm then compiles
/// only the reachable code, so an API that CoreLib lacks fails the NetWasm
/// compile, not this step.
///
///   Retarget <input.dll> <output.dll> <NetWasm.CoreLib.dll>
module Retarget

open System
open System.IO
open Mono.Cecil

let private desktopAssemblies =
    set [ "netstandard"; "mscorlib"; "System.Runtime"; "System.Private.CoreLib"
          "System.Runtime.Numerics"; "System.Runtime.Extensions"; "System.Runtime.InteropServices"
          "System.Collections"; "System.Console" ]

[<EntryPoint>]
let main argv =
    match argv with
    | [| input; output; coreLibPath |] ->
        let coreLib = AssemblyDefinition.ReadAssembly(coreLibPath).Name
        // Read from memory so the input may be rewritten in place.
        use stream = new MemoryStream(File.ReadAllBytes input)
        use assembly = AssemblyDefinition.ReadAssembly(stream)
        let mutable changed = 0
        for m in assembly.Modules do
            // Cecil adds an mscorlib reference on write when it cannot find a
            // corlib it recognises by name; resolve it now so that it caches
            // the reference we are about to rename.
            m.TypeSystem.CoreLibrary |> ignore
            for r in m.AssemblyReferences do
                if desktopAssemblies.Contains r.Name then
                    r.Name <- coreLib.Name
                    r.Version <- coreLib.Version
                    r.PublicKeyToken <- (match coreLib.PublicKeyToken with null -> [||] | t -> t)
                    r.HasPublicKey <- false
                    changed <- changed + 1
            // Several desktop names may have collapsed into one; keep one row.
            for group in m.AssemblyReferences |> Seq.groupBy (fun r -> r.Name) |> Seq.toList do
                match Seq.toList (snd group) with
                | keep :: duplicates when not duplicates.IsEmpty ->
                    let isDuplicate (scope: IMetadataScope) =
                        match scope with
                        | :? AssemblyNameReference as a -> List.exists (fun d -> obj.ReferenceEquals(d, a)) duplicates
                        | _ -> false
                    for t in m.GetTypeReferences() do
                        if isDuplicate t.Scope then t.Scope <- keep
                    for e in m.ExportedTypes do
                        if isDuplicate e.Scope then e.Scope <- keep
                    for d in duplicates do
                        m.AssemblyReferences.Remove d |> ignore
                | _ -> ()
        assembly.Write output
        eprintfn "FSharp.NetWasm: retargeted %d reference(s) in %s" changed (Path.GetFileName input)
        0
    | _ ->
        eprintfn "usage: Retarget <input.dll> <output.dll> <NetWasm.CoreLib.dll>"
        2
