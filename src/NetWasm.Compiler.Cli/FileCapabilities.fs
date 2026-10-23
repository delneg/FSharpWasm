namespace NetWasm.Compiler.Cli

open System.IO

type internal ITextFileReader =
    abstract Read: path: string -> string

type internal ITextFileWriter =
    abstract Write: path: string * content: string -> unit

type internal IBinaryFileWriter =
    abstract Write: path: string * content: byte[] -> unit

type internal IFileDeleter =
    abstract Delete: path: string -> unit

[<Sealed>]
type internal SystemTextFileReader() =
    member _.Read(path: string) = File.ReadAllText path

    interface ITextFileReader with
        member this.Read path = this.Read path

[<Sealed>]
type internal SystemTextFileWriter() =
    member _.Write(path: string, content: string) = File.WriteAllText(path, content)

    interface ITextFileWriter with
        member this.Write(path, content) = this.Write(path, content)

[<Sealed>]
type internal SystemBinaryFileWriter() =
    member _.Write(path: string, content: byte[]) = File.WriteAllBytes(path, content)

    interface IBinaryFileWriter with
        member this.Write(path, content) = this.Write(path, content)

[<Sealed>]
type internal SystemFileDeleter() =
    member _.Delete(path: string) = File.Delete path

    interface IFileDeleter with
        member this.Delete path = this.Delete path
