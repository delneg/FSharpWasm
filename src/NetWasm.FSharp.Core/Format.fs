// The F# printf engine for NetWasm: formats a printf format string with no
// reflection, so the closed-world compiler can compile it. The compiler sends
// FSharp.Core's printf family (sprintf, printfn, failwithf, ksprintf, ...)
// here; see Printf.fs. Output follows FSharp.Core's printf, except that %A
// does no line layout and prints objects it cannot take apart (records,
// unions) with their ToString.
namespace NetWasm.FSharp.Core

open System
open System.Collections
open System.Globalization
open System.IO
open System.Runtime.CompilerServices
open System.Text

/// Where a printf family's %a and %t formatters write: a fresh state for each
/// call, and the text it produced.
[<AbstractClass>]
type Family<'State, 'Residue>() =
    /// unit's value is null, the default (F# cannot override this at 'State = unit).
    abstract NewState: unit -> 'State
    default _.NewState() = Unchecked.defaultof<'State>
    abstract Text: state: 'State * residue: 'Residue -> string

/// A TextWriter over a StringBuilder (CoreLib has no StringWriter).
[<Sealed>]
type internal BufferWriter() =
    inherit TextWriter(CultureInfo.InvariantCulture)
    let buffer = StringBuilder()
    override _.Encoding = Encoding.UTF8
    override _.Write(value: char) = buffer.Append value |> ignore
    override _.Write(value: string) = buffer.Append value |> ignore
    override _.ToString() = buffer.ToString()

[<Sealed>]
type internal StringFamily() =
    inherit Family<unit, string>()
    static member val Instance = StringFamily()
    override _.Text(_, residue) = residue

[<Sealed>]
type internal BuilderFamily() =
    inherit Family<StringBuilder, unit>()
    static member val Instance = BuilderFamily()
    override _.NewState() = StringBuilder()
    override _.Text(state, _) = state.ToString()

[<Sealed>]
type internal WriterFamily() =
    inherit Family<TextWriter, unit>()
    static member val Instance = WriterFamily()
    override _.NewState() = new BufferWriter() :> TextWriter
    override _.Text(state, _) = state.ToString()

/// One printf argument, with its static type, which tells None and () from
/// null, and which a %a formatter needs to be applied.
[<AbstractClass>]
type Arg<'State, 'Residue>() =
    abstract Value: obj
    abstract StaticType: Type
    /// Applies this argument, a %a formatter, to the next one.
    abstract ApplyFormatter: family: Family<'State, 'Residue> * value: Arg<'State, 'Residue> -> string
    abstract ApplyTo: family: Family<'State, 'Residue> * formatter: obj -> string
    /// Applies this argument, a %t formatter.
    abstract ApplyText: family: Family<'State, 'Residue> -> string

[<Sealed>]
type ArgOf<'T, 'State, 'Residue>(value: 'T) =
    inherit Arg<'State, 'Residue>()
    override _.Value = box value
    override _.StaticType = typeof<'T>
    override _.ApplyFormatter(family, next) = next.ApplyTo(family, box value)
    override _.ApplyTo(family, formatter) =
        let formatter = formatter :?> ('State -> 'T -> 'Residue)
        let state = family.NewState()
        family.Text(state, formatter state value)
    override _.ApplyText(family) =
        let formatter = box value :?> ('State -> 'Residue)
        let state = family.NewState()
        family.Text(state, formatter state)

/// An interpolated string's captured hole.
[<Sealed>]
type internal CapturedArg<'State, 'Residue>(value: obj, staticType: Type) =
    inherit Arg<'State, 'Residue>()
    override _.Value = value
    override _.StaticType = staticType
    override _.ApplyFormatter(_, _) = raise (NotSupportedException "%a in an interpolated string")
    override _.ApplyTo(_, _) = raise (NotSupportedException "%a in an interpolated string")
    override _.ApplyText(_) = raise (NotSupportedException "%t in an interpolated string")

[<Flags>]
type internal Flags =
    | None = 0
    | LeftJustify = 1
    | PadWithZeros = 2
    | PlusForPositives = 4
    | SpaceForPositives = 8

/// -1: not given; -2: '*', read from the arguments.
type internal Spec =
    { Flags: Flags
      Width: int
      Precision: int
      Kind: char
      /// %P(...): a .NET format string for an interpolated hole.
      Interpolation: string }

type internal Segment =
    | Literal of string
    | Hole of Spec

module internal Engine =

    let private invariant = CultureInfo.InvariantCulture
    let private star = -2

    let parse (format: string) : Segment[] =
        let segments = ResizeArray<Segment>()
        let literal = StringBuilder()
        let flush () =
            if literal.Length > 0 then
                segments.Add(Literal(literal.ToString()))
                literal.Clear() |> ignore
        let mutable i = 0
        let n = format.Length
        let number () =
            if i < n && format[i] = '*' then
                i <- i + 1
                star
            elif i < n && (format[i] >= '0' && format[i] <= '9') then
                let mutable value = 0
                while i < n && (format[i] >= '0' && format[i] <= '9') do
                    value <- value * 10 + int format[i] - int '0'
                    i <- i + 1
                value
            else -1
        while i < n do
            let c = format[i]
            if c <> '%' then
                literal.Append c |> ignore
                i <- i + 1
            elif i + 1 < n && format[i + 1] = '%' then
                literal.Append '%' |> ignore
                i <- i + 2
            else
                i <- i + 1
                let mutable flags = Flags.None
                let mutable reading = true
                while reading && i < n do
                    match format[i] with
                    | '-' -> flags <- flags ||| Flags.LeftJustify; i <- i + 1
                    | '0' -> flags <- flags ||| Flags.PadWithZeros; i <- i + 1
                    | '+' -> flags <- flags ||| Flags.PlusForPositives; i <- i + 1
                    | ' ' -> flags <- flags ||| Flags.SpaceForPositives; i <- i + 1
                    | _ -> reading <- false
                let width = number ()
                let precision =
                    if i < n && format[i] = '.' then
                        i <- i + 1
                        let p = number ()
                        if p = -1 then 0 else p
                    else -1
                if i >= n then raise (FormatException "Missing format specifier.")
                let kind = format[i]
                i <- i + 1
                let mutable interpolation = null
                if kind = 'P' && i < n && format[i] = '(' then
                    let close = format.IndexOf(')', i)
                    if close < 0 then raise (FormatException "Unclosed %P(.")
                    interpolation <- format.Substring(i + 1, close - i - 1)
                    i <- close + 1
                // In an interpolated string, a typed hole ("%d{x}") is the spec followed by a
                // "%P()" marker that takes no argument of its own.
                if kind <> 'P' && i + 3 < n && format[i] = '%' && format[i + 1] = 'P' && format[i + 2] = '(' then
                    let close = format.IndexOf(')', i + 2)
                    if close >= 0 then i <- close + 1
                flush ()
                segments.Add(Hole { Flags = flags; Width = width; Precision = precision; Kind = kind; Interpolation = interpolation })
        flush ()
        segments.ToArray()

    let private has (flag: Flags) (flags: Flags) = flags &&& flag = flag

    let private pad (spec: Spec) (width: int) (text: string) =
        if width <= text.Length then text
        elif has Flags.LeftJustify spec.Flags then text.PadRight width
        else text.PadLeft width

    /// Signs and pads a number given as its sign and digits.
    let private number (spec: Spec) (width: int) (negative: bool) (digits: string) =
        let sign =
            if negative then "-"
            elif has Flags.PlusForPositives spec.Flags then "+"
            elif has Flags.SpaceForPositives spec.Flags then " "
            else ""
        let length = sign.Length + digits.Length
        if width <= length then sign + digits
        elif has Flags.LeftJustify spec.Flags then (sign + digits).PadRight width
        elif has Flags.PadWithZeros spec.Flags then sign + String('0', width - length) + digits
        else (sign + digits).PadLeft width

    let private unsignedDigits (value: uint64) (radix: int) (upper: bool) =
        if value = 0UL then "0"
        else
            let chars = Array.zeroCreate<char> 64
            let mutable position = 64
            let mutable remaining = value
            let digits = if upper then "0123456789ABCDEF" else "0123456789abcdef"
            while remaining <> 0UL do
                position <- position - 1
                chars[position] <- digits[int (remaining % uint64 radix)]
                remaining <- remaining / uint64 radix
            String(chars, position, 64 - position)

    /// An integer as (negative, magnitude, bit pattern) when it is one.
    let private integer (value: obj) : struct (bool * uint64 * uint64) voption =
        match value with
        | :? int as v -> ValueSome(struct (v < 0, (if v < 0 then uint64 (-(int64 v)) else uint64 v), uint64 (uint32 v)))
        | :? int64 as v -> ValueSome(struct (v < 0, (if v < 0 then uint64 (-(v + 1L)) + 1UL else uint64 v), uint64 v))
        | :? byte as v -> ValueSome(struct (false, uint64 v, uint64 v))
        | :? sbyte as v -> ValueSome(struct (v < 0y, uint64 (abs (int v)), uint64 (byte v)))
        | :? int16 as v -> ValueSome(struct (v < 0s, uint64 (abs (int v)), uint64 (uint16 v)))
        | :? uint16 as v -> ValueSome(struct (false, uint64 v, uint64 v))
        | :? uint32 as v -> ValueSome(struct (false, uint64 v, uint64 v))
        | :? uint64 as v -> ValueSome(struct (false, v, v))
        | :? nativeint as v ->
            let v = int64 v
            ValueSome(struct (v < 0L, (if v < 0L then uint64 (-(v + 1L)) + 1UL else uint64 v), (if IntPtr.Size = 4 then uint64 (uint32 v) else uint64 v)))
        | :? unativeint as v -> ValueSome(struct (false, uint64 v, uint64 v))
        | _ -> ValueNone

    let private isFloat (value: obj) =
        match value with
        | :? float | :? float32 | :? decimal -> true
        | _ -> false

    let private floatText (kind: char) (precision: int) (value: obj) : struct (bool * string) =
        let precision = if precision < 0 then 6 else precision
        let format =
            match kind with
            | 'e' -> "e" + string precision
            | 'E' -> "E" + string precision
            | 'f' | 'F' -> "F" + string precision
            | 'g' -> "g" + string precision
            | _ -> "G" + string precision
        let special (text: string) = struct (false, text)
        // As FSharp.Core: the sign comes from x < 0, so -0.0 keeps its own "-" in the digits.
        match value with
        | :? float as v when Double.IsNaN v -> special "NaN"
        | :? float as v when Double.IsPositiveInfinity v -> special "Infinity"
        | :? float as v when Double.IsNegativeInfinity v -> special "-Infinity"
        | :? float as v -> struct (v < 0.0, (if v < 0.0 then -v else v).ToString(format, invariant))
        | :? float32 as v when Single.IsNaN v -> special "NaN"
        | :? float32 as v when Single.IsPositiveInfinity v -> special "Infinity"
        | :? float32 as v when Single.IsNegativeInfinity v -> special "-Infinity"
        | :? float32 as v -> struct (v < 0.0f, (if v < 0.0f then -v else v).ToString(format, invariant))
        | :? decimal as v -> struct (v < 0M, (abs v).ToString(format, invariant))
        | _ -> raise (FormatException("%" + string kind + " needs a floating-point argument"))

    let private isUnitType (t: Type) = obj.ReferenceEquals(t, typeof<unit>) || t = typeof<unit>

    let private typeName (t: Type) = if isNull t then "" else t.ToString()

    let private isOptionType (t: Type) =
        typeName(t).StartsWith("Microsoft.FSharp.Core.FSharpOption`1", StringComparison.Ordinal)

    let private typeIs (prefix: string) (value: obj) =
        typeName(value.GetType()).StartsWith(prefix, StringComparison.Ordinal)

    let private isListValue = typeIs "Microsoft.FSharp.Collections.FSharpList`1"

    /// F# lays out a long list over lines of at most 80 characters, each but the last
    /// ending with ";" and the next indented under the first element.
    let private layout (opening: string) (parts: ResizeArray<string>) (closing: string) =
        let output = StringBuilder(opening)
        let mutable line = opening.Length
        for i in 0 .. parts.Count - 1 do
            let part = parts[i]
            if i > 0 then
                if line + 1 + part.Length > 80 then
                    output.Append(';').Append('\n').Append(' ', opening.Length) |> ignore
                    line <- opening.Length
                else
                    output.Append("; ") |> ignore
                    line <- line + 2
            output.Append(part) |> ignore
            line <- line + part.Length
        output.Append(closing).ToString()

    /// FSharp.Core prints an option as "Some 3". Without runtime member lookup the value
    /// is reached only through the option's ToString, "Some(3)", so a string inside
    /// prints without its quotes.
    let rec private optionText (text: string) : string =
        if text.StartsWith("Some(", StringComparison.Ordinal) && text.EndsWith(")", StringComparison.Ordinal) then
            let inner = optionText (text.Substring(5, text.Length - 6))
            let bare = inner.IndexOf(' ') < 0 || inner.StartsWith("[") || inner.StartsWith("(") || inner.StartsWith("\"")
            "Some " + (if bare then inner else "(" + inner + ")")
        else text

    /// %A of a float, after FSharp.Core's anyToString: round-trip text, with ".0" when it
    /// would read as an integer.
    let private floatA (text: string) =
        if text.IndexOfAny([| '.'; 'E'; 'e'; 'n'; 'N'; 'I' |]) >= 0 then text else text + ".0"

    /// Objects being printed by %A now: a record or union's ToString calls back into %A for
    /// itself, and there this prints its type name instead of recursing. (NetWasm runs one
    /// thread.)
    let private active = ResizeArray<obj>()

    let rec structured (value: obj) (staticType: Type) : string =
        match value with
        | null ->
            if isUnitType staticType then "()"
            elif isOptionType staticType then "None"
            else "<null>"
        | :? string as s -> "\"" + s + "\""
        | :? char as c -> "'" + string c + "'"
        | :? bool as b -> if b then "true" else "false"
        | :? int as v -> v.ToString(invariant)
        | :? int64 as v -> v.ToString(invariant) + "L"
        | :? byte as v -> v.ToString(invariant) + "uy"
        | :? sbyte as v -> v.ToString(invariant) + "y"
        | :? int16 as v -> v.ToString(invariant) + "s"
        | :? uint16 as v -> v.ToString(invariant) + "us"
        | :? uint32 as v -> v.ToString(invariant) + "u"
        | :? uint64 as v -> v.ToString(invariant) + "UL"
        | :? nativeint as v -> v.ToString() + "n"
        | :? unativeint as v -> v.ToString() + "un"
        | :? float as v ->
            if Double.IsNaN v then "nan"
            elif Double.IsPositiveInfinity v then "infinity"
            elif Double.IsNegativeInfinity v then "-infinity"
            else floatA (v.ToString("g10", invariant).Replace('E', 'e'))
        | :? float32 as v ->
            if Single.IsNaN v then "nanf"
            elif Single.IsPositiveInfinity v then "infinityf"
            elif Single.IsNegativeInfinity v then "-infinityf"
            else floatA ((float v).ToString("g10", invariant).Replace('E', 'e')) + "f"
        | :? decimal as v -> v.ToString(invariant) + "M"
        | :? Array as array -> layout "[|" (elements array 100) "|]"
        | :? ITuple as tuple ->
            let parts = Array.init tuple.Length (fun i -> structured tuple[i] typeof<obj>)
            "(" + String.Join(", ", parts) + ")"
        | :? IEnumerable as items when isListValue value -> layout "[" (elements items 100) "]"
        | :? IEnumerable as items when typeIs "Microsoft.FSharp.Collections.FSharpSet`1" value ->
            layout "set [" (elements items 100) "]"
        | _ when typeIs "Microsoft.FSharp.Core.FSharpOption`1" value -> optionText (value.ToString())
        | _ ->
            if active.Exists(fun item -> obj.ReferenceEquals(item, value)) then
                let name = typeName (value.GetType())
                name.Substring(name.LastIndexOfAny([| '.'; '+' |]) + 1)
            else
                active.Add value
                try
                    match value with
                    | :? IEnumerable as items when not (value :? IFormattable) ->
                        let text = value.ToString()
                        if text = typeName (value.GetType()) then layout "seq [" (elements items 4) "]" else text
                    | _ -> value.ToString()
                finally
                    active.RemoveAt(active.Count - 1)

    and private elements (items: IEnumerable) (limit: int) =
        // The static type of the elements, so that a None element prints "None".
        let elementType =
            let t = items.GetType()
            if t.IsArray then typeof<obj> // NetWasm's Type has no GetElementType.
            else
                let arguments = t.GetGenericArguments()
                if arguments.Length = 1 then arguments[0] else typeof<obj>
        let parts = ResizeArray<string>()
        let e = items.GetEnumerator()
        let mutable more = e.MoveNext()
        while more && parts.Count < limit do
            parts.Add(structured e.Current elementType)
            more <- e.MoveNext()
        if more then parts.Add "..."
        parts

    let private text (spec: Spec) (width: int) (precision: int) (arg: Arg<'State, 'Residue>) : string =
        let value = arg.Value
        match spec.Kind with
        | 'b' ->
            match value with
            | :? bool as b -> pad spec width (if b then "true" else "false")
            | _ -> raise (FormatException "%b needs a bool argument")
        | 'c' -> pad spec width (string (value :?> char))
        | 's' ->
            let s =
                match value with
                | null -> ""
                | :? string as s -> s
                | v -> v.ToString()
            pad spec width s
        | 'd' | 'i' ->
            match integer value with
            | ValueSome(struct (negative, magnitude, _)) -> number spec width negative (magnitude.ToString(invariant))
            | ValueNone ->
                match value with
                | null -> raise (FormatException "%d needs an integer argument")
                | v ->
                    let s = v.ToString()
                    if s.StartsWith "-" then number spec width true (s.Substring 1) else number spec width false s
        | 'u' ->
            match integer value with
            | ValueSome(struct (_, _, bits)) -> number spec width false (bits.ToString(invariant))
            | ValueNone -> raise (FormatException "%u needs an integer argument")
        | 'x' | 'X' | 'o' | 'B' ->
            match integer value with
            | ValueSome(struct (_, _, bits)) ->
                let radix = match spec.Kind with 'o' -> 8 | 'B' -> 2 | _ -> 16
                number { spec with Flags = spec.Flags &&& ~~~(Flags.PlusForPositives ||| Flags.SpaceForPositives) } width false
                    (unsignedDigits bits radix (spec.Kind = 'X'))
            | ValueNone -> raise (FormatException("%" + string spec.Kind + " needs an integer argument"))
        | 'e' | 'E' | 'f' | 'F' | 'g' | 'G' ->
            let struct (negative, digits) = floatText spec.Kind precision value
            if digits = "NaN" || digits.EndsWith("Infinity", StringComparison.Ordinal) then pad spec width digits
            else number spec width negative digits
        | 'M' ->
            match value with
            | :? decimal as v ->
                // FSharp.Core ignores the precision of %M.
                number spec width (v < 0M) ((abs v).ToString(invariant))
            | _ -> raise (FormatException "%M needs a decimal argument")
        | 'O' -> pad spec width (match value with null -> "<null>" | v -> v.ToString())
        | 'A' -> pad spec width (structured value arg.StaticType)
        | 'P' ->
            let s =
                match value with
                | null -> ""
                | :? IFormattable as f when not (String.IsNullOrEmpty spec.Interpolation) ->
                    f.ToString(spec.Interpolation, invariant)
                | :? IFormattable as f -> f.ToString(null, invariant)
                | v -> v.ToString()
            pad spec width s
        | kind -> raise (FormatException("Unknown printf format specifier '" + string kind + "'."))

    let render (segments: Segment[]) (captures: obj[]) (captureTypes: Type[]) (args: Arg<'State, 'Residue>[]) (family: Family<'State, 'Residue>) : string =
        let args =
            if isNull captures || captures.Length = 0 then args
            else
                // fsc lists CaptureTypes for the %A holes only, in order.
                let staticTypes = Array.create captures.Length typeof<obj>
                if not (isNull captureTypes) then
                    let mutable capture = 0
                    let mutable typed = 0
                    for segment in segments do
                        match segment with
                        | Hole spec ->
                            let used = (if spec.Width = star then 1 else 0) + (if spec.Precision = star then 1 else 0)
                            capture <- capture + used
                            if spec.Kind = 'A' && typed < captureTypes.Length && capture < staticTypes.Length then
                                if not (isNull captureTypes[typed]) then staticTypes[capture] <- captureTypes[typed]
                                typed <- typed + 1
                            capture <- capture + (match spec.Kind with 'a' -> 2 | _ -> 1)
                        | Literal _ -> ()
                Array.init captures.Length (fun i -> CapturedArg<'State, 'Residue>(captures[i], staticTypes[i]) :> Arg<'State, 'Residue>)
        let output = StringBuilder()
        let mutable next = 0
        let take () =
            let arg = args[next]
            next <- next + 1
            arg
        for segment in segments do
            match segment with
            | Literal s -> output.Append s |> ignore
            | Hole spec ->
                let width = if spec.Width = star then unbox<int> (take ()).Value else spec.Width
                let precision = if spec.Precision = star then unbox<int> (take ()).Value else spec.Precision
                match spec.Kind with
                | 'a' ->
                    let formatter = take ()
                    let value = take ()
                    output.Append(pad spec width (formatter.ApplyFormatter(family, value))) |> ignore
                | 't' -> output.Append(pad spec width ((take ()).ApplyText family)) |> ignore
                | _ -> output.Append(text spec width precision (take ())) |> ignore
        output.ToString()
