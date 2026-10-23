/// Rewrites a desktop-targeted assembly (an F# program compiled against the
/// net10.0 reference pack, or the netstandard2.1 FSharp.Core) so that every
/// reference to a framework assembly points at NetWasm.CoreLib instead.
/// NetWasm then compiles only the reachable code, so an API that CoreLib lacks
/// fails the NetWasm compile, not this step.
///
///   Retarget <input.dll> <output.dll> <NetWasm.CoreLib.dll> [--pack <dir>] [--message-args]
///
/// A reference is retargeted when it names netstandard, mscorlib,
/// System.Private.CoreLib, System.Runtime or any assembly in the reference
/// pack directory.
module Retarget

open System
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

/// FSharp.Core builds most argument-error messages with
/// String.Format(string, object[]) right before it throws. On NetWasm that one
/// call pulls the whole composite-formatting machinery into the closed world
/// (about 220 KB), and every Array.zeroCreate with a non-constant length
/// reaches such an error path. Calls followed closely by `throw` are redirected
/// to an injected helper. By default it returns the format string alone (the
/// smallest output); with --message-args it returns
/// `format + " " + String.Join(" ", args)`, about 16 KB more, so the message
/// keeps its arguments.
let private patchThrowFormatting (messageArgs: bool) (m: ModuleDefinition) =
    let isFormat (i: Instruction) =
        i.OpCode = OpCodes.Call
        && (match i.Operand with
            | :? MethodReference as r ->
                r.Name = "Format" && r.DeclaringType.FullName = "System.String" && r.Parameters.Count = 2
                && r.Parameters.[1].ParameterType.FullName = "System.Object[]"
            | _ -> false)
    let throwsSoon (code: Mono.Collections.Generic.Collection<Instruction>) k =
        seq { k + 1 .. min (code.Count - 1) (k + 8) } |> Seq.exists (fun j -> code.[j].OpCode = OpCodes.Throw)
    let sites =
        [ for t in m.GetTypes() do
              for meth in t.Methods do
                  if meth.HasBody then
                      let code = meth.Body.Instructions
                      for k in 0 .. code.Count - 1 do
                          if isFormat code.[k] && throwsSoon code k then yield code.[k] ]
    if not sites.IsEmpty then
        let format = (sites.Head.Operand :?> MethodReference)
        let stringType = format.ReturnType
        let objectArray = format.Parameters.[1].ParameterType
        let join =
            MethodReference("Join", stringType, format.DeclaringType, HasThis = false)
        join.Parameters.Add(ParameterDefinition(stringType))
        join.Parameters.Add(ParameterDefinition(objectArray))
        let concat =
            MethodReference("Concat", stringType, format.DeclaringType, HasThis = false)
        for _ in 1..3 do
            concat.Parameters.Add(ParameterDefinition(stringType))
        let holder =
            TypeDefinition(
                "Microsoft.FSharp.Core", "NetWasmMessages",
                TypeAttributes.NotPublic ||| TypeAttributes.Sealed ||| TypeAttributes.Abstract ||| TypeAttributes.Class,
                m.TypeSystem.Object)
        let helper =
            MethodDefinition("Format", MethodAttributes.Assembly ||| MethodAttributes.Static ||| MethodAttributes.HideBySig, stringType)
        helper.Parameters.Add(ParameterDefinition("format", ParameterAttributes.None, stringType))
        helper.Parameters.Add(ParameterDefinition("args", ParameterAttributes.None, objectArray))
        let il = helper.Body.GetILProcessor()
        il.Emit(OpCodes.Ldarg_0)
        if messageArgs then
            il.Emit(OpCodes.Ldstr, " ")
            il.Emit(OpCodes.Ldstr, " ")
            il.Emit(OpCodes.Ldarg_1)
            il.Emit(OpCodes.Call, join)
            il.Emit(OpCodes.Call, concat)
        il.Emit(OpCodes.Ret)
        holder.Methods.Add helper
        m.Types.Add holder
        for site in sites do
            site.Operand <- helper
    sites.Length

/// For `x.ToString()` or `x.GetHashCode()` on a primitive, fsc emits
/// `constrained. P; callvirt Object::ToString`, where C# emits a direct
/// `call P::ToString`. ECMA-335 defines the two as equivalent when P overrides
/// the method, as every primitive does, but NetWasm compiles the constrained
/// form slower: about 7% of ui-churn. This rewrites it to the C# form.
/// (fsc's `ldelem.any`/`stelem.any` of reference types and Array.zeroCreate of
/// a constant length were measured too: rewriting them changed nothing.)
/// FSWASM_PEEPHOLE=0 turns it off, for measuring.
let private peephole (m: ModuleDefinition) =
    let primitives =
        set [ "System.Boolean"; "System.Char"; "System.SByte"; "System.Byte"; "System.Int16"; "System.UInt16"
              "System.Int32"; "System.UInt32"; "System.Int64"; "System.UInt64"; "System.Single"; "System.Double" ]
    let mutable count = 0
    if Environment.GetEnvironmentVariable "FSWASM_PEEPHOLE" <> "0" then
        for t in m.GetTypes() do
            for meth in t.Methods do
                if meth.HasBody then
                    let code = meth.Body.Instructions
                    for k in 1 .. code.Count - 1 do
                        let prefix, i = code.[k - 1], code.[k]
                        match prefix.Operand, i.Operand with
                        | (:? TypeReference as p), (:? MethodReference as r) when
                            prefix.OpCode = OpCodes.Constrained && i.OpCode = OpCodes.Callvirt
                            && primitives.Contains p.FullName
                            && r.DeclaringType.FullName = "System.Object"
                            && (r.Name = "ToString" || r.Name = "GetHashCode")
                            && r.Parameters.Count = 0 ->
                            // Nop in place keeps a branch to the prefix valid.
                            prefix.OpCode <- OpCodes.Nop
                            prefix.Operand <- null
                            i.OpCode <- OpCodes.Call
                            i.Operand <- MethodReference(r.Name, r.ReturnType, p, HasThis = true)
                            count <- count + 1
                        | _ -> ()
    count

let private alwaysRetargeted =
    [ "netstandard"; "mscorlib"; "System.Private.CoreLib"; "System.Runtime"; "System.Runtime.Numerics" ]

let private frameworkAssemblies (referencePack: string option) =
    let fromPack =
        match referencePack with
        | Some dir when Directory.Exists dir ->
            Directory.GetFiles(dir, "*.dll") |> Array.map Path.GetFileNameWithoutExtension |> Array.toList
        | _ -> []
    set (alwaysRetargeted @ fromPack)

let private retarget (input: string) (output: string) (coreLibPath: string) referencePack messageArgs =
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
            assembly.Modules |> Seq.sumBy (fun m -> patchResourceStrings m + patchThrowFormatting messageArgs m)
        else
            assembly.Modules |> Seq.sumBy peephole
    assembly.Write output
    eprintfn "FSharp.NetWasm: retargeted %d reference(s) in %s%s" changed (Path.GetFileName input)
        (if patched > 0 then sprintf ", patched %d call site(s)" patched else "")

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    | input :: output :: coreLib :: options ->
        let rec parse pack messageArgs options =
            match options with
            | "--pack" :: dir :: rest -> parse (Some dir) messageArgs rest
            | "--message-args" :: rest -> parse pack true rest
            | [] -> Some(pack, messageArgs)
            | _ -> None
        match parse None false options with
        | Some(pack, messageArgs) ->
            retarget input output coreLib pack messageArgs
            0
        | None ->
            eprintfn "usage: Retarget <input.dll> <output.dll> <NetWasm.CoreLib.dll> [--pack <dir>] [--message-args]"
            2
    | _ ->
        eprintfn "usage: Retarget <input.dll> <output.dll> <NetWasm.CoreLib.dll> [--pack <dir>] [--message-args]"
        2
