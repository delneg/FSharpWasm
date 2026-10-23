/// Reference facades for compiling F# against NetWasm.CoreLib.
///
/// fsc finds primitive types only in a "system" assembly: one named like
/// System.Runtime or netstandard, or one in the same folder as it. NetWasm has
/// a single CoreLib with a name of its own, and FSharp.Core is built against
/// netstandard. So the F# reference set is a folder with
///
/// - System.Runtime.dll: forwards every public CoreLib type to NetWasm.CoreLib;
/// - netstandard.dll: the same forwarders, plus an empty definition for each
///   netstandard type that CoreLib lacks, so FSharp.Core's signature data loads;
/// - NetWasm.CoreLib.dll: the reference assembly itself.
///
/// The NetWasm compiler then aliases System.Runtime and netstandard to
/// NetWasm.CoreLib. Code that really uses one of the empty types fails there,
/// with NetWasm's diagnostic for a missing type.
module NetWasm.FSharp.Facades.ReferenceSet

open System
open System.IO
open System.Reflection
open System.Reflection.Metadata
open System.Reflection.Metadata.Ecma335
open System.Reflection.PortableExecutable
open System.Security.Cryptography
open System.Text

type Shape =
    | Class
    | Interface
    | Struct
    | Enum
    | Delegate

/// The shape fsc needs to import a type: its kind and generic arity.
type Stub = { Namespace: string; Name: string; Shape: Shape; Arity: int }

let private openMetadata (path: string) =
    let pe = new PEReader(File.OpenRead path)
    pe, pe.GetMetadataReader()

let rec private fullName (reader: MetadataReader) (t: TypeDefinition) =
    let declaring = t.GetDeclaringType()
    if declaring.IsNil then
        let ns = reader.GetString t.Namespace
        let name = reader.GetString t.Name
        if ns = "" then name else ns + "." + name
    else
        fullName reader (reader.GetTypeDefinition declaring) + "/" + reader.GetString t.Name

let private isVisible (t: TypeDefinition) =
    let visibility = t.Attributes &&& TypeAttributes.VisibilityMask
    if t.GetDeclaringType().IsNil then
        visibility = TypeAttributes.Public
    else
        visibility = TypeAttributes.NestedPublic
        || visibility = TypeAttributes.NestedFamily
        || visibility = TypeAttributes.NestedFamORAssem

let private baseTypeName (reader: MetadataReader) (t: TypeDefinition) =
    if t.BaseType.Kind = HandleKind.TypeReference then
        let b = reader.GetTypeReference(TypeReferenceHandle.op_Explicit t.BaseType)
        reader.GetString b.Namespace + "." + reader.GetString b.Name
    else
        ""

let private shapeOf (reader: MetadataReader) (t: TypeDefinition) =
    if t.Attributes.HasFlag TypeAttributes.Interface then
        Interface
    else
        match baseTypeName reader t with
        | "System.Enum" -> Enum
        | "System.ValueType" -> Struct
        | "System.MulticastDelegate" -> Delegate
        | _ -> Class

/// Top-level types defined by the assemblies of a .NET reference pack folder.
let readShapes (referencePack: string) =
    let shapes = Collections.Generic.Dictionary<string, Shape * int>(StringComparer.Ordinal)
    for path in Directory.GetFiles(referencePack, "*.dll") |> Array.sort do
        let pe, reader = openMetadata path
        use _ = pe
        if reader.IsAssembly then
            for handle in reader.TypeDefinitions do
                let t = reader.GetTypeDefinition handle
                if t.GetDeclaringType().IsNil && isVisible t then
                    shapes.TryAdd(fullName reader t, (shapeOf reader t, t.GetGenericParameters().Count)) |> ignore
    shapes

/// Top-level type names that a facade assembly (such as the reference pack's
/// netstandard.dll) forwards or defines.
let readSurface (facade: string) =
    let pe, reader = openMetadata facade
    use _ = pe
    [ for handle in reader.ExportedTypes do
          let e = reader.GetExportedType handle
          if e.Implementation.Kind <> HandleKind.ExportedType then
              let ns = reader.GetString e.Namespace
              yield (if ns = "" then "" else ns + ".") + reader.GetString e.Name
      for handle in reader.TypeDefinitions do
          let t = reader.GetTypeDefinition handle
          if t.GetDeclaringType().IsNil && isVisible t && reader.GetString t.Name <> "<Module>" then
              yield fullName reader t ]
    |> List.distinct
    |> List.sort

let private split (name: string) =
    match name.LastIndexOf '.' with
    | -1 -> "", name
    | dot -> name.Substring(0, dot), name.Substring(dot + 1)

/// Writes facade <name>.dll, which forwards every visible type of the CoreLib
/// reference assembly and defines the given stubs.
let write (coreLibReference: string) (name: string) (stubs: Stub list) (output: string) =
    let pe, core = openMetadata coreLibReference
    use _ = pe
    let coreDefinition = core.GetAssemblyDefinition()
    let mb = MetadataBuilder()
    let str (s: string) = mb.GetOrAddString s
    let mvid = Guid(MD5.HashData(Encoding.UTF8.GetBytes("NetWasm.FSharp.Facades/" + name)))
    mb.AddModule(0, str (name + ".dll"), mb.GetOrAddGuid mvid, GuidHandle(), GuidHandle()) |> ignore
    mb.AddAssembly(str name, Version(10, 0, 0, 0), StringHandle(), BlobHandle(), AssemblyFlags(), AssemblyHashAlgorithm.Sha1)
    |> ignore
    let coreRef =
        mb.AddAssemblyReference(
            str (core.GetString coreDefinition.Name),
            coreDefinition.Version,
            StringHandle(),
            BlobHandle(),
            AssemblyFlags(),
            BlobHandle()
        )
    let systemType n =
        let h = mb.AddTypeReference(AssemblyReferenceHandle.op_Implicit coreRef, str "System", str n)
        (TypeReferenceHandle.op_Implicit: TypeReferenceHandle -> EntityHandle) h
    let objectType, valueType, enumType, delegateType =
        systemType "Object", systemType "ValueType", systemType "Enum", systemType "MulticastDelegate"
    let int32Field =
        let b = BlobBuilder()
        BlobEncoder(b).FieldSignature().Int32()
        mb.GetOrAddBlob b
    let firstMethod = MetadataTokens.MethodDefinitionHandle 1
    let mutable nextField = 1
    mb.AddTypeDefinition(TypeAttributes(), StringHandle(), str "<Module>", EntityHandle(), MetadataTokens.FieldDefinitionHandle 1, firstMethod)
    |> ignore
    for stub in stubs do
        let attributes, baseType =
            match stub.Shape with
            | Interface -> TypeAttributes.Public ||| TypeAttributes.Interface ||| TypeAttributes.Abstract, EntityHandle()
            | Struct -> TypeAttributes.Public ||| TypeAttributes.Sealed, valueType
            | Enum -> TypeAttributes.Public ||| TypeAttributes.Sealed, enumType
            | Delegate -> TypeAttributes.Public ||| TypeAttributes.Sealed, delegateType
            | Class -> TypeAttributes.Public ||| TypeAttributes.Abstract, objectType
        let definition =
            mb.AddTypeDefinition(
                attributes,
                str stub.Namespace,
                str stub.Name,
                baseType,
                MetadataTokens.FieldDefinitionHandle nextField,
                firstMethod
            )
        if stub.Shape = Enum then
            mb.AddFieldDefinition(
                FieldAttributes.Public ||| FieldAttributes.SpecialName ||| FieldAttributes.RTSpecialName,
                str "value__",
                int32Field
            )
            |> ignore
            nextField <- nextField + 1
        for i in 0 .. stub.Arity - 1 do
            mb.AddGenericParameter(TypeDefinitionHandle.op_Implicit definition, GenericParameterAttributes.None, str $"T{i}", i)
            |> ignore
    let forwarder = enum<TypeAttributes> 0x00200000
    let rec forward (handle: TypeDefinitionHandle) (implementation: EntityHandle) (topLevel: bool) =
        let t = core.GetTypeDefinition handle
        let ns = if topLevel then str (core.GetString t.Namespace) else StringHandle()
        let exported = mb.AddExportedType(forwarder, ns, str (core.GetString t.Name), implementation, 0)
        for nested in t.GetNestedTypes() do
            if isVisible (core.GetTypeDefinition nested) then
                forward nested (ExportedTypeHandle.op_Implicit exported) false
    let mutable forwarded = 0
    for handle in core.TypeDefinitions do
        let t = core.GetTypeDefinition handle
        if t.GetDeclaringType().IsNil && isVisible t then
            forwarded <- forwarded + 1
            forward handle (AssemblyReferenceHandle.op_Implicit coreRef) true
    let image = BlobBuilder()
    let contentId _ = BlobContentId(mvid, 0u)
    ManagedPEBuilder(
        PEHeaderBuilder(imageCharacteristics = Characteristics.Dll),
        MetadataRootBuilder(mb),
        BlobBuilder(),
        deterministicIdProvider = contentId
    )
        .Serialize(image)
    |> ignore
    File.WriteAllBytes(output, image.ToArray())
    forwarded

/// The facades of the F# reference set. System.Runtime is the primary
/// assembly fsc compiles against, so it has no stubs: a program that uses a type
/// CoreLib lacks fails in fsc. The others are what FSharp.Core references.
let facades =
    [ "System.Runtime", false
      "netstandard", true
      "System.Runtime.Numerics", true ]

/// Writes the whole F# reference folder.
let writeReferenceSet (coreLibReference: string) (referencePack: string) (outputDirectory: string) =
    Directory.CreateDirectory outputDirectory |> ignore
    let pe, core = openMetadata coreLibReference
    let defined =
        use _ = pe
        set [ for h in core.TypeDefinitions -> fullName core (core.GetTypeDefinition h) ]
    let shapes = readShapes referencePack
    let stubsFor (assembly: string) =
        [ for name in readSurface (Path.Combine(referencePack, assembly + ".dll")) do
              if not (defined.Contains name) then
                  let ns, simple = split name
                  let shape, arity =
                      match shapes.TryGetValue name with
                      | true, s -> s
                      | _ -> Class, 0
                  yield { Namespace = ns; Name = simple; Shape = shape; Arity = arity } ]
    let written =
        [ for name, withStubs in facades do
              let stubs = if withStubs then stubsFor name else []
              let forwarded = write coreLibReference name stubs (Path.Combine(outputDirectory, name + ".dll"))
              yield name, forwarded, stubs.Length ]
    File.Copy(coreLibReference, Path.Combine(outputDirectory, "NetWasm.CoreLib.dll"), true)
    written
