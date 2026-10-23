namespace NetWasm.FSharp.Core

open System
open System.Text

/// FSharp.Core formats an argument-error message with String.Format right before it
/// throws (Array.zeroCreate with a negative count, List.item past the end, ...). On
/// NetWasm, String.Format brings the whole composite-formatting machinery, about
/// 220 KB, into a program that never formats anything else. The compiler
/// (FSharpCoreMessageFormatRewriter) sends those calls here: the same text, with each
/// "{n}" hole filled by the argument's ToString(); alignment and format strings are
/// ignored.
[<AbstractClass; Sealed>]
type Messages =
    static member private Fill(format: string, args: obj[]) : string =
        if isNull format then
            ""
        else
            let output = StringBuilder(format.Length + 16)
            let mutable i = 0
            while i < format.Length do
                let c = format[i]
                if (c = '{' || c = '}') && i + 1 < format.Length && format[i + 1] = c then
                    output.Append c |> ignore
                    i <- i + 2
                elif c = '{' then
                    let close = format.IndexOf('}', i)
                    let mutable index = 0
                    let mutable j = i + 1
                    while j < format.Length && Char.IsDigit format[j] do
                        index <- index * 10 + int format[j] - int '0'
                        j <- j + 1
                    if close < 0 || j = i + 1 || isNull args || index >= args.Length then
                        output.Append c |> ignore
                        i <- i + 1
                    else
                        match args[index] with
                        | null -> ()
                        | value -> output.Append(value.ToString()) |> ignore
                        i <- close + 1
                else
                    output.Append c |> ignore
                    i <- i + 1
            output.ToString()

    static member Format(format: string, arg0: obj) : string = Messages.Fill(format, [| arg0 |])

    static member Format(format: string, arg0: obj, arg1: obj) : string = Messages.Fill(format, [| arg0; arg1 |])

    static member Format(format: string, arg0: obj, arg1: obj, arg2: obj) : string =
        Messages.Fill(format, [| arg0; arg1; arg2 |])

    static member Format(format: string, args: obj[]) : string = Messages.Fill(format, args)
