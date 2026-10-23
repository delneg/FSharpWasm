/// Rewrites a desktop-targeted assembly (an F# program compiled against the
/// net10.0 reference pack, or the netstandard2.1 FSharp.Core) so that every
/// reference to a framework assembly points at NetWasm.CoreLib instead.
/// NetWasm then compiles only the reachable code, so an API that CoreLib lacks
/// fails the NetWasm compile, not this step.
///
///   Retarget <input.dll> <output.dll> <NetWasm.CoreLib.dll> [<reference pack dir>]
///
/// A reference is retargeted when it names netstandard, mscorlib,
/// System.Private.CoreLib, System.Runtime or any assembly in the reference
/// pack directory.
module Retarget

open System.IO
open Mono.Cecil
open Mono.Cecil.Cil

/// FSharp.Core reads every error message through a ResourceManager with
/// CultureInfo.CurrentUICulture, inlined as
///     call SR::get_ResourceManager; ldstr key; call CultureInfo::get_CurrentUICulture;
///     callvirt ResourceManager::GetString(string, CultureInfo)
/// NetWasm has neither satellite resources nor CurrentUICulture, and even
/// Array.zeroCreate reaches one of these messages. Each such sequence becomes
/// plain `ldstr key`, so an exception carries the resource key as its message.
let private patchResourceStrings (m: ModuleDefinition) =
    let isCall (i: Instruction) (declaringType: string) (name: string) =
        (i.OpCode = OpCodes.Call || i.OpCode = OpCodes.Callvirt)
        && (match i.Operand with
            | :? MethodReference as r -> r.Name = name && r.DeclaringType.FullName = declaringType
            | _ -> false)
    let mutable patched = 0
    for t in m.GetTypes() do
        for meth in t.Methods do
            if meth.HasBody then
                let code = meth.Body.Instructions
                for k in 0 .. code.Count - 4 do
                    let a, b, c = code.[k], code.[k + 1], code.[k + 2]
                    // An optional `tail.` prefix may precede the GetString call.
                    let prefix, d =
                        if code.[k + 3].OpCode = OpCodes.Tail && k + 4 < code.Count then
                            [ code.[k + 3] ], code.[k + 4]
                        else
                            [], code.[k + 3]
                    if isCall a "Microsoft.FSharp.Core.SR" "get_ResourceManager"
                       && b.OpCode = OpCodes.Ldstr
                       && isCall c "System.Globalization.CultureInfo" "get_CurrentUICulture"
                       && isCall d "System.Resources.ResourceManager" "GetString" then
                        // Nop in place keeps branch targets valid.
                        for i in a :: c :: d :: prefix do
                            i.OpCode <- OpCodes.Nop
                            i.Operand <- null
                        patched <- patched + 1
    patched

let private alwaysRetargeted =
    [ "netstandard"; "mscorlib"; "System.Private.CoreLib"; "System.Runtime"; "System.Runtime.Numerics" ]

let private frameworkAssemblies (referencePack: string option) =
    let fromPack =
        match referencePack with
        | Some dir when Directory.Exists dir ->
            Directory.GetFiles(dir, "*.dll") |> Array.map Path.GetFileNameWithoutExtension |> Array.toList
        | _ -> []
    set (alwaysRetargeted @ fromPack)

let private retarget (input: string) (output: string) (coreLibPath: string) referencePack =
    let framework = frameworkAssemblies referencePack
    let coreLib = AssemblyDefinition.ReadAssembly(coreLibPath).Name
    // Read from memory so the input may be rewritten in place.
    use stream = new MemoryStream(File.ReadAllBytes input)
    use assembly = AssemblyDefinition.ReadAssembly(stream)
    let mutable changed = 0
    for m in assembly.Modules do
        // Cecil adds an mscorlib reference on write when it finds no corlib it
        // recognises by name; resolve it now so it caches the reference we
        // are about to rename.
        m.TypeSystem.CoreLibrary |> ignore
        for r in m.AssemblyReferences do
            if framework.Contains r.Name then
                r.Name <- coreLib.Name
                r.Version <- coreLib.Version
                r.PublicKeyToken <- (match coreLib.PublicKeyToken with null -> [||] | t -> t)
                r.HasPublicKey <- false
                changed <- changed + 1
        // Several framework names collapse into one; keep a single row.
        match m.AssemblyReferences |> Seq.tryFind (fun r -> r.Name = coreLib.Name) with
        | Some keep ->
            let duplicates =
                m.AssemblyReferences
                |> Seq.filter (fun r -> r.Name = coreLib.Name && not (obj.ReferenceEquals(r, keep)))
                |> Seq.toList
            if not duplicates.IsEmpty then
                let isDuplicate (scope: IMetadataScope) =
                    match scope with
                    | :? AssemblyNameReference as a -> duplicates |> List.exists (fun d -> obj.ReferenceEquals(d, a))
                    | _ -> false
                for t in m.GetTypeReferences() do
                    if isDuplicate t.Scope then t.Scope <- keep
                for e in m.ExportedTypes do
                    if isDuplicate e.Scope then e.Scope <- keep
                for d in duplicates do
                    m.AssemblyReferences.Remove d |> ignore
        | None -> ()
    let patched =
        if assembly.Name.Name = "FSharp.Core" then
            assembly.Modules |> Seq.sumBy patchResourceStrings
        else
            0
    assembly.Write output
    eprintfn "FSharp.NetWasm: retargeted %d reference(s) in %s%s" changed (Path.GetFileName input)
        (if patched > 0 then sprintf ", patched %d resource lookup(s)" patched else "")

[<EntryPoint>]
let main argv =
    match argv with
    | [| input; output; coreLib |] ->
        retarget input output coreLib None
        0
    | [| input; output; coreLib; pack |] ->
        retarget input output coreLib (Some pack)
        0
    | _ ->
        eprintfn "usage: Retarget <input.dll> <output.dll> <NetWasm.CoreLib.dll> [<reference pack dir>]"
        2
